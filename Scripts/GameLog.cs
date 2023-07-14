using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

namespace XZDice
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

#if VITDECK_HIDE_MENUITEM
        public void _VketStart()
#else
        private void Start()
#endif
        {
            _Clear();
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
    }
}
