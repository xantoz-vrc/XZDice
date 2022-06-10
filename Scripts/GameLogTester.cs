
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace XZDice
{
    public class GameLogTester : UdonSharpBehaviour
    {
        [SerializeField] private GameLog log;

        void Start()
        {
            SendCustomEventDelayedSeconds("Derp", 2);
        }

        private uint cntr = 0;
        public void Derp()
        {
            log.Log(cntr.ToString() + "Derp");
            cntr++;
            SendCustomEventDelayedSeconds("Derp", 2);
        }
    }
}