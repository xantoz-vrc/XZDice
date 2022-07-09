using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

#if VITDECK_HIDE_MENUITEM
namespace Vket2022Summer.Circle314
#else
namespace XZDice
#endif
{
    //[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class DieGrabSphere : UdonSharpBehaviour
    {
        [SerializeField]
        private GameObject[] dice;

        [Tooltip("When set DieGrabSphere will hide and become ungrabbable after being released")]
        public bool hideOnThrow = false;

        private VRC_Pickup pickup;
        private Rigidbody rigidbody;

        // Toggled on when FixedUpdate should make the dice follow the Sphere
        private bool diceFollow = false;

        [SerializeField]
        private Collider insideBowlCollider = null;
        private bool[] dieReadResult;

        private UdonSharpBehaviour[] listeners;

        private void Start()
        {
            pickup = (VRC_Pickup)gameObject.GetComponent(typeof(VRC_Pickup));
            rigidbody = gameObject.GetComponent<Rigidbody>();

            listeners = new UdonSharpBehaviour[0];
            dieReadResult = new bool[dice.Length];
            foreach (GameObject die in dice) {
                Die d = (Die)die.GetComponent(typeof(UdonBehaviour));
                if (d != null)
                    d._AddListener(this);
            }
        }

        public void _AddListener(UdonSharpBehaviour newlistener)
        {
            if (listeners == null)
                listeners = new UdonSharpBehaviour[0];
            int newlength = listeners.Length + 1;
            UdonSharpBehaviour[] newlisteners = new UdonSharpBehaviour[newlength];
            listeners.CopyTo(newlisteners, 0);
            newlisteners[newlength - 1] = newlistener;
            listeners = newlisteners;
        }

        public int _GetLength()
        {
            return dice.Length;
        }

        public void _SetThrown()
        {
            // Do nothing
        }

        // DiceListener
        public void _SetHeld()
        {
            // Do nothing
        }

        private bool isInsideBowl(GameObject die)
        {
            if (insideBowlCollider == null)
                return true;

            Collider[] colliders = Physics.OverlapBox(die.transform.position, die.GetComponent<Collider>().bounds.extents);
            // Collider[] colliders = Physics.OverlapSphere(die.transform.position, 0.05f);
            foreach (Collider col in colliders) {
                if (System.Object.ReferenceEquals(insideBowlCollider, col)) {
                    return true;
                }
            }

            return false;
        }

        // DiceListener
        public void _DiceResult()
        {
            // Actually I think this would all probably be easier if we just handled the dice directly instead of being a listener of it...
            for (int i = 0; i < dice.Length; ++i) {
                GameObject die = dice[i];
                Die d = (Die)die.GetComponent(typeof(UdonBehaviour));
                if (!dieReadResult[i] && d._GetResult() != -1) {
                    dieReadResult[i] = true;
                    int result = d._GetResult();
                    if (!isInsideBowl(die)) {
                        result = 0;
                    }
                    string fnname = "_DiceResult" + result.ToString();
                    foreach (UdonSharpBehaviour lis in listeners) {
                        lis.SendCustomEvent(fnname);
                    }
                }
            }
        }

        public void _BecomeOwner()
        {
            if (Utilities.IsValid(Networking.LocalPlayer) && !Networking.IsOwner(gameObject)) {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

                foreach (GameObject die in dice) {
                    if (!Networking.IsOwner(die))
                        Networking.SetOwner(Networking.LocalPlayer, die);
                }
            }
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(Networking.LocalPlayer))
                return;

            if (player.playerId != Networking.LocalPlayer.playerId)
                return;

            foreach (GameObject die in dice) {
                if (!Networking.IsOwner(die)) {
                    Networking.SetOwner(player, die);
                }
            }
        }

        public void _Hide()
        {
            MeshRenderer mr = GetComponent<MeshRenderer>();
            mr.enabled = false;
            _SetPickupable(false);
        }

        public void HideGlobal()
        {
            _Hide();
        }

        public void _HideWithDice()
        {
            _Hide();
            foreach (GameObject die in dice) {
                MeshRenderer mr = die.GetComponentInChildren<MeshRenderer>();
                mr.enabled = false;
                Rigidbody rb = die.GetComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = true;
            }
        }

        private bool acquiredOriginalColor = false;
        private Color originalColor;
        private Color originalEmissionColor;
        public void _SetPickupable(bool val)
        {
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr != null) {
                Material mat = mr.material;
                if (!acquiredOriginalColor) {
                    originalColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.magenta;
                    originalEmissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.magenta;
                    acquiredOriginalColor = true;
                }

                if (mat.HasProperty("_Color")) {
                    mat.SetColor("_Color", (val) ? originalColor : Color.grey);
                }

                if (mat.HasProperty("_EmissionColor")) {
                    mat.SetColor("_EmissionColor", (val) ? originalEmissionColor : Color.grey);
                }
            }

            VRC_Pickup p = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            p.pickupable = val;
        }

        public void _SetPickupableTrue() { _SetPickupable(true); }
        public void _SetPickupableFalse() { _SetPickupable(false); }

        public void _Show()
        {
            MeshRenderer mr = GetComponent<MeshRenderer>();
            mr.enabled = true;

            foreach (GameObject die in dice) {
                MeshRenderer die_mr = die.GetComponentInChildren<MeshRenderer>();
                die_mr.enabled = true;
            }
        }

        public void _TeleportTo(Transform tf)
        {
            VRCObjectSync os = (VRCObjectSync)GetComponent(typeof(VRCObjectSync));
            if (os != null)
                os.FlagDiscontinuity();

            rigidbody.position = tf.position;
            rigidbody.rotation = tf.rotation;
        }

        // Park the dice floating in the grab sphere prior to it being picked up
        // Use when presenting the dice to be thrown to a player
        public void _ParkDice()
        {
            int idx = 0;
            foreach (GameObject die in dice) {
                VRC_Pickup p = (VRC_Pickup)die.GetComponent(typeof(VRC_Pickup));
                if (p != null) {
                    p.Drop();
                    p.pickupable = false;
                }

                Rigidbody rb = die.GetComponent<Rigidbody>();
                rb.position = rigidbody.position + rigidbody.rotation*(new Vector3(0.03f*(idx - dice.Length/2), 0.0f, 0.0f));
                rb.useGravity = false;
                rb.rotation = Random.rotation;
                rb.isKinematic = true;

                Collider c = die.GetComponent<Collider>();
                c.enabled = false;

                VRCObjectSync os = (VRCObjectSync)die.GetComponent(typeof(VRCObjectSync));
                if (os != null)
                    os.FlagDiscontinuity();

                ++idx;
            }
        }

        // TODO: replace with parentconstraint usage
        private void FixedUpdate()
        {
            int idx = 0;
            if (diceFollow) {
                foreach (GameObject die in dice) {
                    Rigidbody rb = die.GetComponent<Rigidbody>();
                    rb.position = rigidbody.position + rigidbody.rotation*(new Vector3(0.03f*(idx - dice.Length/2), 0.0f, 0.0f));
                    rb.useGravity = false;
                    rb.rotation = Random.rotation;
                    ++idx;
                }
            }
        }

        public override void OnPickup()
        {
            // Reset dieReadResult
            for (int i = 0; i < dieReadResult.Length; ++i) {
                dieReadResult[i] = false;
            }

            int idx = 0;
            foreach (GameObject die in dice) {
                if (Utilities.IsValid(Networking.LocalPlayer) && !Networking.IsOwner(die))
                    Networking.SetOwner(Networking.LocalPlayer, die);

                VRC_Pickup p = (VRC_Pickup)die.GetComponent(typeof(VRC_Pickup));
                if (p != null) {
                    p.Drop();
                    p.pickupable = false;
                }

                Collider c = die.GetComponent<Collider>();
                c.enabled = false;

                Rigidbody rb = die.GetComponent<Rigidbody>();
                rb.position = rigidbody.position + new Vector3(0.03f*(idx - dice.Length/2), 0.0f, 0.0f);
                rb.useGravity = false;

                VRCObjectSync os = (VRCObjectSync)die.GetComponent(typeof(VRCObjectSync));
                if (os != null)
                    os.FlagDiscontinuity();

                Die d = (Die)die.GetComponent(typeof(UdonBehaviour));
                if (d != null)
                    d._SetHeld();

                ++idx;
            }

            diceFollow = true;

            // Send _SetHeld to all listeners
            foreach (UdonSharpBehaviour lis in listeners) {
                lis.SendCustomEvent("_SetHeld");
            }
        }

        public override void OnDrop()
        {
            int i = 0;
            foreach (GameObject die in dice) {
                if (Utilities.IsValid(Networking.LocalPlayer) && !Networking.IsOwner(die))
                    Networking.SetOwner(Networking.LocalPlayer, die);

                VRC_Pickup p = (VRC_Pickup)die.GetComponent(typeof(VRC_Pickup));
                if (p != null)
                    p.pickupable = true;

                Collider c = die.GetComponent<Collider>();
                c.enabled = true;

                Rigidbody rb = die.GetComponent<Rigidbody>();
                rb.position = rigidbody.position + rigidbody.rotation*(new Vector3(0.03f*(i - dice.Length/2), 0.0f, 0.0f));
                rb.rotation = Random.rotation;
                rb.velocity = rigidbody.velocity;
                rb.angularVelocity = rigidbody.angularVelocity;
                rb.useGravity = true;

                Die d = (Die)die.GetComponent(typeof(UdonBehaviour));
                if (d != null) {
                    d._SetThrown();
                }
                if (d == null || !d.onlyPhysicsWhenThrown) {
                    rb.isKinematic = false;
                    rb.WakeUp();
                }


                ++i;
            }

            diceFollow = false;

            if (hideOnThrow) {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(HideGlobal));
            }

            foreach (UdonSharpBehaviour lis in listeners) {
                lis.SendCustomEvent("_SetThrown");
            }
        }
    }
}
