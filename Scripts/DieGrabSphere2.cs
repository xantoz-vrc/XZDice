using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace XZDice
{
    // Optimized variant of DieGrabSphere that works with dice without their own UdonBehaviour.
    // It assumes that the dice are not individually pickupable (no VRC_Pickup).
    // It will always toggle the isKinematic of the dice (only enables physics when the dice are
    // being actually thrown)
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class DieGrabSphere2 : UdonSharpBehaviour
    {
        [SerializeField]
        private GameObject[] dice;

        [Tooltip("When set DieGrabSphere will hide and become ungrabbable after being released")]
        public bool hideOnThrow = false;

        [FieldChangeCallback(nameof(pickup))]
        private VRC_Pickup _pickup;
        private VRC_Pickup pickup => _pickup ? _pickup : (_pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup)));

        [FieldChangeCallback(nameof(rigidbody))]
        private Rigidbody _rigidbody;
        private Rigidbody rigidbody => _rigidbody ? _rigidbody : (_rigidbody = gameObject.GetComponent<Rigidbody>());

        [SerializeField]
        private Collider insideBowlCollider = null;

        private UdonSharpBehaviour[] listeners;

        // Used to give dice one fixed update before we start checking whether they are still or
        // not. Without this they can sometimes get stuck in mid-air, if they start out from a
        // complete stand-still.
        private bool firstFixedUpdate = false;

        // Toggled on when FixedUpdate should make the dice follow the Sphere
        private bool diceFollow = false;

        private uint thrown = 0;
        private void SetThrown(int i) { thrown = thrown | (1u << i); }
        private void ClrThrown(int i) { thrown = thrown & ~(1u << i); }
        private bool GetThrown(int i) { return (thrown & (1u << i)) != 0; }

        public void _AddListener(UdonSharpBehaviour newlistener)
        {
            if (listeners == null) {
                listeners = new UdonSharpBehaviour[1];
                listeners[0] = newlistener;
                return;
            }

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

        private void SendToListeners(string fnname)
        {
            if (listeners != null) {
                foreach (UdonSharpBehaviour lis in listeners) {
                    lis.SendCustomEvent(fnname);
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

                ClrThrown(idx);

                ++idx;
            }
        }

        private int ListMinIndex(float[] list)
        {
            float smallest = float.PositiveInfinity;
            int smallest_idx = -1;

            for (int i = 0; i < list.Length; ++i) {
                if (list[i] < smallest) {
                    smallest = list[i];
                    smallest_idx = i;
                }
            }

            return smallest_idx;
        }

        private int CalculateResult(GameObject die)
        {
            if (!isInsideBowl(die)) {
                return 0;
            }

            float[] angles = new float[] {
                Vector3.Angle(die.transform.up, Vector3.up),       //1
                Vector3.Angle(-die.transform.forward, Vector3.up), //2
                Vector3.Angle(die.transform.right, Vector3.up),    //3
                Vector3.Angle(-die.transform.right, Vector3.up),   //4
                Vector3.Angle(die.transform.forward, Vector3.up),  //5
                Vector3.Angle(-die.transform.up, Vector3.up)       //6
            };

            return ListMinIndex(angles) + 1;
        }

        private void FixedUpdate()
        {
            // Have the dice follow the grabsphere while it is being grabbed
            // TODO: replace with parentconstraint usage?
            if (diceFollow) {
                int idx = 0;
                foreach (GameObject die in dice) {
                    Rigidbody rb = die.GetComponent<Rigidbody>();
                    rb.position = rigidbody.position + rigidbody.rotation*(new Vector3(0.03f*(idx - dice.Length/2), 0.0f, 0.0f));
                    rb.useGravity = false;
                    rb.rotation = Random.rotation;
                    ++idx;
                }
            }

            // One or more dice not at rest, and not the very first fixed update after it has been thrown
            if (!firstFixedUpdate && thrown !=0) {
                int idx = 0;
                foreach (GameObject die in dice) {
                    Rigidbody rb = die.GetComponent<Rigidbody>();

                    if (GetThrown(idx) &&
                        rb.velocity.sqrMagnitude < 0.0001 &&
                        rb.angularVelocity.sqrMagnitude < 0.0001) {
                        ClrThrown(idx);
                        rb.isKinematic = true;
                        int result = CalculateResult(die);
                        SendToListeners("_DiceResult" + result.ToString());
                    }

                    ++idx;
                }
            }

            firstFixedUpdate = false;
        }

        public override void OnPickup()
        {
            int idx = 0;
            foreach (GameObject die in dice) {
                if (Utilities.IsValid(Networking.LocalPlayer) && !Networking.IsOwner(die))
                    Networking.SetOwner(Networking.LocalPlayer, die);

                Collider c = die.GetComponent<Collider>();
                c.enabled = false;

                Rigidbody rb = die.GetComponent<Rigidbody>();
                rb.position = rigidbody.position + new Vector3(0.03f*(idx - dice.Length/2), 0.0f, 0.0f);
                rb.useGravity = false;
                rb.isKinematic = true;

                VRCObjectSync os = (VRCObjectSync)die.GetComponent(typeof(VRCObjectSync));
                if (os != null)
                    os.FlagDiscontinuity();

                ClrThrown(idx);

                ++idx;
            }

            diceFollow = true;

            SendToListeners("_SetHeld");
        }

        public override void OnDrop()
        {
            int idx = 0;
            foreach (GameObject die in dice) {
                if (Utilities.IsValid(Networking.LocalPlayer) && !Networking.IsOwner(die))
                    Networking.SetOwner(Networking.LocalPlayer, die);

                Collider c = die.GetComponent<Collider>();
                c.enabled = true;

                Rigidbody rb = die.GetComponent<Rigidbody>();
                rb.position = rigidbody.position + rigidbody.rotation*(new Vector3(0.03f*(idx - dice.Length/2), 0.0f, 0.0f));
                rb.rotation = Random.rotation;
                rb.velocity = rigidbody.velocity;
                rb.angularVelocity = rigidbody.angularVelocity;
                rb.useGravity = true;

                rb.isKinematic = false;
                rb.WakeUp();

                SetThrown(idx);

                ++idx;
            }

            diceFollow = false;

            if (hideOnThrow) {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(HideGlobal));
            }

            SendToListeners("_SetThrown");

            firstFixedUpdate = true;
        }
    }
}
