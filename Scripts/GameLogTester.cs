using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace Vket2023Winter.Circle504
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
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
            log._Log(cntr.ToString() + "Derp");
            cntr++;
            SendCustomEventDelayedSeconds("Derp", 2);
        }
    }
}
