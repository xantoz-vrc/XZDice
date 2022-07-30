using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;

/*
#if VITDECK_HIDE_MENUITEM
*/
namespace Vket2022Summer.Circle314
/*
#else
namespace XZDice
#endif
*/
{
/*
#if VITDECK_HIDE_MENUITEM
*/
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
/*
#else
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
#endif
*/
    public class DiceDebug : UdonSharpBehaviour
    {
        [SerializeField]
        private Die[] dice;

        [SerializeField]
        TextMeshProUGUI textMeshPro = null;

        [SerializeField]
        private Text text = null;

/*
#if VITDECK_HIDE_MENUITEM
#else
        [UdonSynced]
#endif
*/
        private string output;

/*
#if VITDECK_HIDE_MENUITEM
*/
        public void _VketStart()
/*
#else
        private void Start()
#endif
*/
        {
/*
#if VITDECK_HIDE_MENUITEM
*/
            UpdateText("Start");
/*
#else
            if (textMeshPro == null) {
                textMeshPro = GetComponent<TextMeshProUGUI>();
            }

            if(text == null) {
                text = GetComponent<Text>();
            }

            if (Networking.IsOwner(gameObject))
                UpdateText("Start");
#endif
*/
            foreach (Die die in dice) {
                die._AddListener(this);
            }
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
/*
#if VITDECK_HIDE_MENUITEM
#else
        public override void OnDeserialization()
        {
            ApplyText();
        }
#endif
*/

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
/*
#if VITDECK_HIDE_MENUITEM
#else
            if (!Networking.IsOwner(gameObject) && Networking.LocalPlayer != null)
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
#endif
*/

            string playerName = (Networking.LocalPlayer != null) ? Networking.LocalPlayer.displayName : "";
            output = extra + "\n" + "Thrower: " + playerName + "\n";
            foreach (Die die in dice) {
                output += die.name + " " + die._GetResult().ToString() + " " + die._GetThrown().ToString() + "\n";
            }

            ApplyText();
/*
#if VITDECK_HIDE_MENUITEM
#else
            RequestSerialization();
#endif
*/
        }
    }
}
