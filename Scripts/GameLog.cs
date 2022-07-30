using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

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
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GameLog : UdonSharpBehaviour
    {
        [SerializeField] private int numlines = 20;

        [SerializeField] private Text[] screens;

        [FieldChangeCallback(nameof(lines))]
        private string[] _lines;
        private string[] lines => (_lines != null) ? _lines : (_lines = new string[numlines]);

        private int insertPos = 0;
        private int startPos = 0;
        private int totalLines = 0;

        bool ready = false;

        [SerializeField]
        private bool visible = true;

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
            _Clear();
            _SetVisible(visible);
        }

        public void _Clear()
        {
            for (int i = 0; i < lines.Length; ++i) {
                lines[i] = "";
            }
            foreach (Text t in screens) {
                t.text = "";
            }
            insertPos = 0;
            startPos = 0;
            totalLines = 0;
        }

        public void _Log(string message)
        {
            lines[insertPos] = message;
            insertPos = (insertPos + 1) % numlines;
            totalLines++;

            // Start scrolling
            if (totalLines > numlines) {
                startPos = (startPos + 1) % numlines;
            }

            ApplyText();
        }

        private void ApplyText()
        {
            string contents = "";

            int idx = startPos;
            for (int i = 0; i < numlines; ++i) {
                contents += lines[idx] + "\n";
                idx = (idx + 1) % numlines;
            }

            foreach (Text t in screens) {
                t.text = contents;
            }
        }

        public void _SetVisible(bool val)
        {
            foreach (Text t in screens) {
                t.enabled = val;
            }
            visible = val;
        }

        public void _ToggleVisible()
        {
            _SetVisible(!visible);
        }
    }
}
