using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

namespace XZDice
{
    public class GameLog : UdonSharpBehaviour
    {
        [SerializeField] private int numlines = 20;

        [SerializeField] private Text[] screens;

        private string[] lines;
        private int insertPos = 0;
        private int startPos = 0;
        private int totalLines = 0;

        bool ready = false;

        private void Start()
        {
            lines = new string[numlines];
            foreach (Text t in screens) {
                t.text = "";
            }
        }

        public void Log(string message)
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
