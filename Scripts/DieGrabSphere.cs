using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace XZDice
{
    //[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class DieGrabSphere : UdonSharpBehaviour
    {
        public GameObject[] dice;

        [Tooltip("When set DieGrabSphere will hide and become ungrabbable after being released")]
        public bool hideOnThrow = false;

        //[SerializeField]
        //private DiceGroup dicegroup;

        private VRC_Pickup pickup;
        private Rigidbody rigidbody;
        //private Transform transform;
        //private GameObject parent;

        private bool diceFollow = false;

        private void Start()
        {
            pickup = (VRC_Pickup)gameObject.GetComponent(typeof(VRC_Pickup));
            rigidbody = gameObject.GetComponent<Rigidbody>();
        }

        public void _BecomeOwner()
        {
            if (!Networking.IsOwner(gameObject)) {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (player.isLocal) {
                foreach (GameObject die in dice) {
                    if (!player.IsOwner(die))
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

        public void _SetPickupable(bool val)
        {
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
            int i = 0;
            foreach (GameObject die in dice) {
                VRC_Pickup p = (VRC_Pickup)die.GetComponent(typeof(VRC_Pickup));
                if (p != null) {
                    p.Drop();
                    p.pickupable = false;
                }

                Rigidbody rb = die.GetComponent<Rigidbody>();
                rb.position = rigidbody.position + rigidbody.rotation*(new Vector3(0.03f*(i - dice.Length/2), 0.0f, 0.0f));
                rb.useGravity = false;
                rb.rotation = Random.rotation;

                Collider c = die.GetComponent<Collider>();
                c.enabled = false;

                VRCObjectSync os = (VRCObjectSync)die.GetComponent(typeof(VRCObjectSync));
                if (os != null)
                    os.FlagDiscontinuity();

                ++i;
            }
        }

        // TODO: replace with parentconstraint usage
        private void FixedUpdate()
        {
            int i = 0;
            if (diceFollow) {
                foreach (GameObject die in dice) {
                    Rigidbody rb = die.GetComponent<Rigidbody>();
                    rb.position = rigidbody.position + rigidbody.rotation*(new Vector3(0.03f*(i - dice.Length/2), 0.0f, 0.0f));
                    rb.useGravity = false;
                    rb.rotation = Random.rotation;
                    ++i;
                }
            }
        }

        public override void OnPickup()
        {
            int i = 0;
            foreach (GameObject die in dice) {
                if (!Networking.IsOwner(die))
                    Networking.SetOwner(Networking.LocalPlayer, die);

                VRC_Pickup p = (VRC_Pickup)die.GetComponent(typeof(VRC_Pickup));
                if (p != null) {
                    p.Drop();
                    p.pickupable = false;
                }

                Collider c = die.GetComponent<Collider>();
                c.enabled = false;

                Rigidbody rb = die.GetComponent<Rigidbody>();
                rb.position = rigidbody.position + new Vector3(0.03f*(i - dice.Length/2), 0.0f, 0.0f);
                rb.useGravity = false;

                VRCObjectSync os = (VRCObjectSync)die.GetComponent(typeof(VRCObjectSync));
                if (os != null)
                    os.FlagDiscontinuity();

                Die d = (Die)die.GetComponent(typeof(UdonBehaviour));
                if (d != null)
                    d.SetHeld();

                ++i;
            }

            diceFollow = true;
        }

        public override void OnDrop()
        {
            int i = 0;
            foreach (GameObject die in dice) {
                if (!Networking.IsOwner(die))
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
                    d.SetThrown();
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
        }
    }
}
