using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;

#if VITDECK_HIDE_MENUITEM
namespace Vket2022Summer.Circle314
#else
namespace XZDice
#endif
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class DiceDebug : UdonSharpBehaviour
    {
        [SerializeField]
        private Die[] dice;

        [SerializeField]
        TextMeshProUGUI textMeshPro = null;

        [SerializeField]
        private Text text = null;

        [UdonSynced]
        private string output;

        private void Start()
        {
            if (textMeshPro == null) {
                textMeshPro = GetComponent<TextMeshProUGUI>();
            }

            if(text == null) {
                text = GetComponent<Text>();
            }

            foreach (Die die in dice) {
                die._AddListener(this);
            }

            if (Networking.IsOwner(gameObject))
                UpdateText("Start");
        }

        // DiceListener
        public void _SetThrown()
        {
            UpdateText("SetThrown");
        }

        // DiceListener
        public void _SetHeld()
        {
            UpdateText("SetHeld");
        }

        // DiceListener
        public void _DiceResult()
        {
            UpdateText("DiceResult");
        }

        public override void OnDeserialization()
        {
            ApplyText();
        }

        private void ApplyText()
        {
            if (text != null) {
                text.text = output;
            }

            if (textMeshPro != null) {
                textMeshPro.text = output;
            }
        }

        private void UpdateText(string extra)
        {
            if (!Networking.IsOwner(gameObject) && Networking.LocalPlayer != null)
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            string playerName = (Networking.LocalPlayer != null) ? Networking.LocalPlayer.displayName : "";
            output = extra + "\n" + "Thrower: " + playerName + "\n";
            foreach (Die die in dice) {
                output += die.name + " " + die._GetResult().ToString() + " " + die._GetThrown().ToString() + "\n";
            }

            ApplyText();
            RequestSerialization();
        }
    }
}
