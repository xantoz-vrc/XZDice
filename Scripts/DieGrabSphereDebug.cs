using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;

namespace XZDice
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    // [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DieGrabSphereDebug : UdonSharpBehaviour
    {
        [SerializeField]
        private DieGrabSphere2 dieGrabSphere;

        [SerializeField]
        TextMeshProUGUI textMeshPro = null;

        // [SerializeField]
        // private Text text = null;

        // [UdonSynced]
        private string output = "";

        void Start()
        {
            if (textMeshPro == null) {
                textMeshPro = GetComponent<TextMeshProUGUI>();
            }

            // if(text == null) {
            //     text = GetComponent<Text>();
            // }

            dieGrabSphere._AddListener(this);

            AddText("Start");
        }

        public void ApplyText()
        {
            // if (text != null) {
            //     text.text = output;
            // }

            if (textMeshPro != null) {
                textMeshPro.text = output;
            }
        }

        private void BecomeOwner()
        {
            // if (Utilities.IsValid(Networking.LocalPlayer) && !Networking.IsOwner(gameObject))
            //     Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        // public override void OnDeserialization()
        // {
        //     ApplyText();
        // }

        private void ClearText()
        {
            BecomeOwner();

            output = "";
            ApplyText();
            // RequestSerialization();
        }

        private void AddText(string add)
        {
            BecomeOwner();

            output += add;
            output += "\n";
            ApplyText();
            // RequestSerialization();
        }

        // DiGrabSphereListener
        public void _SetThrown()
        {
            AddText(string.Format("{0:F2} SetThrown", Time.time));
        }

        // DieGrabSphereListener
        public void _SetHeld()
        {
            ClearText();
            AddText(string.Format("{0:F2} SetHeld", Time.time));
        }

        // DieGrabSphereListener
        public void _DiceResult0() { DiceResult(0); }
        public void _DiceResult1() { DiceResult(1); }
        public void _DiceResult2() { DiceResult(2); }
        public void _DiceResult3() { DiceResult(3); }
        public void _DiceResult4() { DiceResult(4); }
        public void _DiceResult5() { DiceResult(5); }
        public void _DiceResult6() { DiceResult(6); }

        private void DiceResult(int result)
        {
            AddText(string.Format("{0:F2} DiceResult({1})", Time.time, result));
        }
    }
}
