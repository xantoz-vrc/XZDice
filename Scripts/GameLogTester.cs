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
