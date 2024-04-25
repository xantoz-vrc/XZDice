using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using TMPro;

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

        [Tooltip("Whether hide on throw behaviour is networked via a network event")]
        public bool networked = true;

        [FieldChangeCallback(nameof(pickup))]
        private VRC_Pickup _pickup;
        private VRC_Pickup pickup => _pickup ? _pickup : (_pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup)));

        [FieldChangeCallback(nameof(rigidbody))]
        private Rigidbody _rigidbody;
        private new Rigidbody rigidbody => _rigidbody ? _rigidbody : (_rigidbody = gameObject.GetComponent<Rigidbody>());

        [SerializeField]
        [Tooltip("(optional) Collider the dice need to land inside of to generate a valid result")]
        private Collider insideBowlCollider = null;

        [SerializeField]
        [Tooltip("(optional) debugging text field")]
        TextMeshProUGUI debugText = null;

        private UdonSharpBehaviour[] listeners;

        // Used to give dice one fixed update before we start checking whether they are still or
        // not. Without this they can sometimes get stuck in mid-air, if they start out from a
        // complete stand-still.
        private int firstFixedUpdate = 0;

	private bool scheduleDrop;
	private bool schedulePickup;

	private Vector3 prevPos;
	private Vector3 velocity;
	private Vector3 prevVelocity;
	private Vector3 prevRBVelocity;

        // Toggled on when FixedUpdate should make the dice follow the Sphere
        private bool diceFollow = false;

#if VITDECK_HIDE_MENUITEM
        private bool inBooth = false;
#endif

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

            pickup.pickupable = val;
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

	    prevPos = tf.position;
	    velocity = new Vector3(0,0,0);
	    prevVelocity = new Vector3(0,0,0);
            rigidbody.position = tf.position;
            rigidbody.rotation = tf.rotation;
        }

        // Park the dice floating in the grab sphere prior to it being picked up
        // Use when presenting the dice to be thrown to a player
        public void _ParkDice()
        {
            int idx = 0;
            foreach (GameObject die in dice) {
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

#if VITDECK_HIDE_MENUITEM
        public void _VketOnBoothEnter()
        {
            inBooth = true;
        }

        public void _VketOnBoothExit()
        {
            inBooth = false;
            diceFollow = false;
            firstFixedUpdate = false;

            if (!Networking.IsOwner(gameObject))
                return;

            pickup.Drop();

            VRCObjectSync objectSync = (VRCObjectSync)GetComponent(typeof(VRCObjectSync));
            if (objectSync != null)
                objectSync.Respawn();

            int idx = 0;
            foreach (GameObject die in dice) {
                Rigidbody rb = die.GetComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = true;

                VRCObjectSync os = (VRCObjectSync)die.GetComponent(typeof(VRCObjectSync));
                if (os != null) {
                    os.Respawn();
                } else {
                    rb.position = rigidbody.position + rigidbody.rotation*(new Vector3(0.03f*(idx - dice.Length/2), 0.0f, 0.0f));
                    rb.rotation = Random.rotation;
                }

                Collider c = die.GetComponent<Collider>();
                c.enabled = false;

                ClrThrown(idx);

                ++idx;
            }
        }
#endif

#if VITDECK_HIDE_MENUITEM
        public void _VketFixedUpdate()
#else
        private void FixedUpdate()
        // private void Update()
#endif
	{
	    velocity = (rigidbody.position - prevPos)/Time.fixedDeltaTime;

            // Have the dice follow the grabsphere while it is being grabbed
            // TODO: replace with parentconstraint usage? or just plain old reparenting somehow?
            if (diceFollow) {
		doDebugText();

                int idx = 0;
                foreach (GameObject die in dice) {
                    Rigidbody rb = die.GetComponent<Rigidbody>();
                    rb.position = rigidbody.position + rigidbody.rotation*(new Vector3(0.03f*(idx - dice.Length/2), 0.0f, 0.0f));
                    rb.useGravity = false;
                    rb.rotation = Random.rotation;
                    ++idx;
                }
            }

	    if (scheduleDrop) {
		doDrop();
		scheduleDrop = false;
	    }

	    if (schedulePickup) {
		doPickup();
		schedulePickup = false;
	    }
	     
	    
            // One or more dice not at rest, and not the very first fixed update after it has been thrown
            if (firstFixedUpdate <= 0 && thrown != 0) {
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

	    if (firstFixedUpdate > 0) {
		--firstFixedUpdate;
	    }

	    prevPos = rigidbody.position;
	    prevVelocity = velocity;
	    prevRBVelocity = rigidbody.velocity;
        }

	private void doPickup()
	{
#if VITDECK_HIDE_MENUITEM
            if (!inBooth) return;
#endif

	    rigidbody.isKinematic = false;
	    rigidbody.WakeUp();

            int idx = 0;
            foreach (GameObject die in dice) {
                if (Utilities.IsValid(Networking.LocalPlayer) && !Networking.IsOwner(die))
                    Networking.SetOwner(Networking.LocalPlayer, die);

                Collider c = die.GetComponent<Collider>();
                c.enabled = false;

                Rigidbody rb = die.GetComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.position = rigidbody.position + new Vector3(0.03f*(idx - dice.Length/2), 0.0f, 0.0f);
                rb.useGravity = false;

                VRCObjectSync os = (VRCObjectSync)die.GetComponent(typeof(VRCObjectSync));
                if (os != null)
                    os.FlagDiscontinuity();

                ClrThrown(idx);

                ++idx;
            }

            diceFollow = true;

            SendToListeners("_SetHeld");
	}

        public override void OnPickup()
        {
#if VITDECK_HIDE_MENUITEM
            if (!inBooth) return;
#endif

	    schedulePickup = true;
//	    doPickup();
        }

	private void doDebugText()
	{
	    if (debugText == null)
		return;

	    debugText.text =
		string.Format(
		    "rigidbody.position: {0}\n" +
		    "rigidbody.rotation: {1}\n" +
		    "rigidbody.velocity: {2}\n" +
		    "rigidbody.angularVelocity: {3}\n" + 
		    "GetAccumulatedForce: {4}\n" +
		    "GetPointVelocity: {5}\n" +
		    "velocity: {6}\n" +
		    "prevVelocity: {7}\n" +
		    "prevRBVelocity: {8}\n",
		    rigidbody.position,
		    rigidbody.rotation,
		    rigidbody.velocity, 
		    rigidbody.angularVelocity,
		    rigidbody.GetAccumulatedForce(),
		    rigidbody.GetPointVelocity(rigidbody.position),
		    velocity,
		    prevVelocity,
		    prevRBVelocity
		    );

	}

	private void doDrop()
	{
#if VITDECK_HIDE_MENUITEM
            if (!inBooth) return;
#endif

            int idx = 0;
            foreach (GameObject die in dice) {
                if (Utilities.IsValid(Networking.LocalPlayer) && !Networking.IsOwner(die))
                    Networking.SetOwner(Networking.LocalPlayer, die);

                Collider c = die.GetComponent<Collider>();
                c.enabled = true;

                Rigidbody rb = die.GetComponent<Rigidbody>();
                rb.isKinematic = false;

                rb.position = rigidbody.position + rigidbody.rotation*(new Vector3(0.03f*(idx - dice.Length/2), 0.0f, 0.0f));
                rb.rotation = Random.rotation;
//                rb.velocity = prevVelocity;
		rb.velocity = rigidbody.velocity;
		rb.angularVelocity = rigidbody.angularVelocity;
		
                rb.useGravity = true;

                rb.WakeUp();

                SetThrown(idx);

                ++idx;
            }

            diceFollow = false;

            if (hideOnThrow) {
                if (networked) {
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(HideGlobal));
                } else {
                    _Hide();
                }
            }

            SendToListeners("_SetThrown");

            firstFixedUpdate = 3;

	    rigidbody.isKinematic = true;
	}

        public override void OnDrop()
        {
#if VITDECK_HIDE_MENUITEM
            if (!inBooth) return;
#endif

	    scheduleDrop = true;
	    // doDrop();
        }
    }
}
