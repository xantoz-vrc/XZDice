using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

#if VITDECK_HIDE_MENUITEM
namespace Vket2022Summer.Circle314
#else
namespace XZDice
#endif
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
    public class Die : UdonSharpBehaviour
    {
        [Tooltip("Optimization where we only turn off the isKinematic flag on the rigidbody when it is thrown until it has settled")]
        public bool onlyPhysicsWhenThrown = false;

        private UdonSharpBehaviour[] listeners;

        private Rigidbody rigidbody;
        private bool thrown = false;
        private bool firstFixedUpdate = false;

        [UdonSynced]
        private int result = -1;

        private void Start()
        {
            rigidbody = this.GetComponent<Rigidbody>();
            listeners = new UdonSharpBehaviour[0];

            if (onlyPhysicsWhenThrown)
                rigidbody.isKinematic = true;
        }

        private int _ListMinIndex(float[] list)
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

        private void CalculateResult()
        {
            float[] angles = new float[] {
                Vector3.Angle(transform.up, Vector3.up),       //1
                Vector3.Angle(-transform.forward, Vector3.up), //2
                Vector3.Angle(transform.right, Vector3.up),    //3
                Vector3.Angle(-transform.right, Vector3.up),   //4
                Vector3.Angle(transform.forward, Vector3.up),  //5
                Vector3.Angle(-transform.up, Vector3.up)       //6
            };

            result = _ListMinIndex(angles) + 1;
        }

        private void FixedUpdate()
        {
            if (!firstFixedUpdate && thrown) {
                if (rigidbody.velocity.sqrMagnitude < 0.0001 && rigidbody.angularVelocity.sqrMagnitude < 0.0001) {
                    thrown = false;
                    CalculateResult();
                    if (onlyPhysicsWhenThrown)
                        rigidbody.isKinematic = true;
                    if (listeners != null) {
                        foreach (UdonSharpBehaviour lis in listeners) {
                            lis.SendCustomEvent("_DiceResult");
                        }
                    }
                }
            }
            firstFixedUpdate = false;
        }

        public void _SetThrown()
        {
            if (onlyPhysicsWhenThrown) {
                rigidbody.isKinematic = false;
                rigidbody.WakeUp();
            }

            firstFixedUpdate = true; // Wait one fixed update before checking if we have stopped
            thrown = true;
            result = -1;
            if (listeners == null)
                return;
            foreach (UdonSharpBehaviour lis in listeners) {
                lis.SendCustomEvent("_SetThrown");
            }
        }

        public void _SetHeld()
        {
            if (onlyPhysicsWhenThrown)
                rigidbody.isKinematic = true;

            thrown = false;
            result = -1;
            if (listeners == null)
                return;
            foreach (UdonSharpBehaviour lis in listeners) {
                lis.SendCustomEvent("_SetHeld");
            }
        }

        public override void OnDrop()
        {
            _SetThrown();
        }

        public override void OnPickup()
        {
            _SetHeld();
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

        public int _GetResult()
        {
            return result;
        }

        public bool _GetThrown()
        {
            return thrown;
        }
    }
}
