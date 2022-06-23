// TODO: make a queue for opcodes and deal em out whenever state machine goes
// into wait
// TODO: in table empty state there should just be a simple start button,
// different from join buttons?

// TODO: much better than start button: kick everyone except oya out after finished round,
// everyone has to rejoin. rejoining is the same as placing a bet: press join
// and the betscreen shows locally, you're considered as when you've entered a
// bet and press done. oya can press start round whenever there is at least one
// bet.

// TODO: oya and playerActive needs to be in every message (7 bits). always
// update buttons?

// TODO: alternative: always disable buttons in OnDeserializatizon, making them
// only appear when a command explicitly enabling them arrives?

// TODO: oyareport or enable_bet should inform how much money oya has, so we can have better feedback about how much you can bet

// TODO: * we need an outgoing opcode queue. while we can control how fast ops exit the state machine,
//         it's not possible to control how fast players might press the bet button
//       * alternatively if we make the state machine 100% event driven with a queue for incoming events instead (but how to handle the outgoing bet opcodes is still tricky)


// TODO: throw people out when they have too little money

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using UnityEngine.UI;
using TMPro;
using UCS;

namespace XZDice
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class Chinchirorin : UdonSharpBehaviour
    {
        [SerializeField]
        private DieGrabSphere dieGrabSphere;

        [SerializeField]
        private GameObject[] joinButtons;

        [SerializeField]
        private GameObject[] betScreens;

        [SerializeField]
        private TextMeshProUGUI[] betLabels;

        [SerializeField]
        [Tooltip("Positions where the dice spawn for each player")]
        private Transform[] diceSpawns;

        [SerializeField]
        [Tooltip("Button dealer presses to start a round. One per player (because different spots).")]
        private GameObject[] startRoundButtons;

        [SerializeField]
        [Tooltip("Timeout displays that count locally to indicate to the player they're on a timeout")]
        private GameObject[] timeoutDisplays;

        [SerializeField]
        private GameLog gameLog = null;

        [SerializeField]
        [Tooltip("GameLog which gets debug logs written to it as well")]
        private GameLog gameLogDebug = null;

        [SerializeField]
        [Tooltip("Enable debugging (also make sure to set gameLogDebug)")]
        private bool DEBUG = true;


        private UdonChips udonChips = null;

        private bool langJp = false;

        // Max bet in part makes sure we do not go above 2^16 (even when tripled), as we serialize bets as 16 bit uint
        //   2*20000 = 60000 < 2^16 = 65536
        private readonly float MAXBET = 20000.0f;
        private readonly int MAX_PLAYERS = 4;
        private readonly int MAX_RETHROWS = 3;

        // Client variables (also used on server)
        private int iAmPlayer = -1;
        private int oya = -1;
        private float totalBet = 0.0f;
        private float oyaMaxBet = 0.0f;

        // Used mainly on server, but some use also in client
        private float[] bets;
        private bool[] playerActive;

        // Server variables (only used on server)
        private bool[] betDone; // Used only by owner
        private int[] betMultiplier;

        private int[] oyaResult; // Use only by owner
        private uint oyaThrowType; // Use only by owner
        private int[] recvResult; // Used only by owner
        int recvResult_cntr = 0; // Used only by owner
        private int rethrowCount = 0; // Used only by owner
        private int currentPlayer = -1; // Used only by owner
        private bool oyaLost = false;

        private int state = -1; // Used only by owner (drives the oya statemachine)

        // These variables are used when the oya sends messages to other players.
        // E.g. when to change udonchips balances;
        [UdonSynced] private uint arg0;

        private void GameLog(string message)
        {
            if (gameLog != null) {
                gameLog._Log(message);
            }
            if (gameLogDebug != null) {
                gameLogDebug._Log(message);
            }
        }

        private void GameLogDebug(string message)
        {
            if (DEBUG && gameLogDebug != null) {
                gameLogDebug._Log("<color=\"grey\">" + message + "</color>");
            }
        }

        private void GameLogWarn(string message)
        {
            if (DEBUG && gameLogDebug != null) {
                gameLogDebug._Log("<color=\"yellow\">WARN: " + message + "</color>");
            }
        }

        private void GameLogError(string message)
        {
            GameLog("<color=\"red\">ERR: " + message + "</color>");
        }

        private string _jp(string str)
        {
            if (!langJp)
                return str;

            // This will translate strings to the active
            // language when there is one

            // Fallback
            return str;
        }

        private void Start()
        {
            if (dieGrabSphere._GetLength() != 3)
                Debug.LogError("Must be three dice");

            dieGrabSphere._AddListener(this);
            dieGrabSphere.hideOnThrow = true; // Ensure hideOnThrow is set

            if (joinButtons.Length != MAX_PLAYERS)
                Debug.LogError(string.Format("joinButtons must be {0} long", MAX_PLAYERS));

            if (betScreens.Length != MAX_PLAYERS)
                Debug.LogError(string.Format("betScreens must be {0} long", MAX_PLAYERS));

            if (timeoutDisplays.Length != MAX_PLAYERS)
                Debug.LogError(string.Format("timeoutDisplays must be {0} long", MAX_PLAYERS));

            if (diceSpawns.Length != MAX_PLAYERS)
                Debug.LogError(string.Format("diceSpawns must be {0} long", MAX_PLAYERS));

            udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();

            ResetClientVariables();
            ResetServerVariables();
            opqueue_Reset();
            ResetTable();

            // Delay this so first enabling of join buttons is likely to happen after serialization on late
            // joiners. Unfortunately cannot currently do it in a smarter way,
            // since the first joiner of instance (first master) does not get
            // OnDeserializatizon.
            // Maybe something with OnPlayerJoined could be done, though?
            SendCustomEventDelayedSeconds(nameof(_MaybeEnableJoinButtonsOnStart), 2.0f);
        }

        public void _MaybeEnableJoinButtonsOnStart()
        {
            // When entering instance, show join buttons if table is currently inactive
            if (op_getop(arg0) == OPCODE_NOOYA)
            {
                UpdateJoinButtons(playerActive);
            }
        }

        private void ResetClientVariables()
        {
            totalBet = 0.0f;
            oyaMaxBet = 0.0f;
            // Below is left out on purpose
            // oya = -1;
        }

        private void ResetServerVariables()
        {
            recvResult = new int[dieGrabSphere._GetLength()];
            recvResult_cntr = 0;
            oyaResult = new int[dieGrabSphere._GetLength()];
            oyaThrowType = THROW_INVALID;
            rethrowCount = 0;
            currentPlayer = -1;
            playerActive = new bool[MAX_PLAYERS];
            bets = new float[MAX_PLAYERS];
            betDone = new bool[MAX_PLAYERS];
            betMultiplier = new int[MAX_PLAYERS];
            oyaLost = false;
            state = -1;
        }

        // Client to server communication (but sent locally if we are master)
        private void SendToOya(string fnname)
        {
            if (isOwner()) {
                SendCustomEvent(fnname);
            } else {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, fnname);
            }
        }

        // Server to everybody communication. Actions are also taken on server
        private void Broadcast(uint op)
        {
            opqueue_Queue(op);
        }


        private void SendPlayerLeaveEvent(int player)
        {
            string fnname = "EventPlayerLeave" + player.ToString();
            SendToOya(fnname);
        }

        public void EventPlayerLeave1() { RecvEventPlayerLeave(1); }
        public void EventPlayerLeave2() { RecvEventPlayerLeave(2); }
        public void EventPlayerLeave3() { RecvEventPlayerLeave(3); }
        public void EventPlayerLeave4() { RecvEventPlayerLeave(4); }

        private void SendPlayerJoinEvent(int player)
        {
            string fnname = "EventPlayerJoin" + player.ToString();
            SendToOya(fnname);
        }

        public void EventPlayerJoin1() { RecvEventPlayerJoin(1); }
        public void EventPlayerJoin2() { RecvEventPlayerJoin(2); }
        public void EventPlayerJoin3() { RecvEventPlayerJoin(3); }
        public void EventPlayerJoin4() { RecvEventPlayerJoin(4); }

        private int getActivePlayerCount()
        {
            int result = 0;

            foreach (bool b in playerActive)
                if (b)
                    result++;

            return result;
        }

        private float getTotalBets()
        {
            float result = 0.0f;
            for (int i = 0; i < MAX_PLAYERS; ++i) {
                if (playerActive[i])
                    result += bets[i];
            }

            return result;
        }

        private void JoinGame(int player)
        {
            // TODO: do not set this here, but instead set it after we get confirmation from owner
            //       that we can actually join. This would require some sort of two-step thing so we
            //       can still identify ourselves in OnDeserialization
            iAmPlayer = player;

            // First person joining when table is empty is oya
            if (op_getop(arg0) == OPCODE_NOOYA) {
                GameLogDebug(string.Format("First person joining the table (arg0={0:X})", arg0));
                oya = iAmPlayer; // Set this already here so OnOwnerShipTransferred knows this wasn't the oya leaving the instance
                if (!Networking.IsOwner(gameObject))
                    Networking.SetOwner(Networking.LocalPlayer, gameObject);

                for (int i = 0; i < MAX_PLAYERS; ++i) {
                    playerActive[i] = false;
                }
                playerActive[iAmPlayer - 1] = true;
                // TODO: we should probably do the equivalent of this inline here instead
                Broadcast(mkop_oyachange(0, iAmPlayer, playerActive));

                // We do not need to do the playerjoin event in this special
                // case. The oyachange thing takes its place.

            } else {
                SendPlayerJoinEvent(iAmPlayer);
            }
        }

        private void LeaveGame(int player)
        {
            SendPlayerLeaveEvent(player);

            iAmPlayer = -1;
            ResetServerVariables();
            ResetClientVariables();
        }

        private void JoinPlayerBtn(int player)
        {
            // Disable all buttons to prevent double-click events (some buttons
            // might be re-enabled once we get ACKed)
            foreach (GameObject btn in joinButtons) {
                btn.SetActive(false);
            }

            if (iAmPlayer > 0) {
                // Already joined means we leave
                LeaveGame(iAmPlayer);
            } else {
                JoinGame(player);
            }
        }

        public void _BtnJoinPlayer1() { JoinPlayerBtn(1); }
        public void _BtnJoinPlayer2() { JoinPlayerBtn(2); }
        public void _BtnJoinPlayer3() { JoinPlayerBtn(3); }
        public void _BtnJoinPlayer4() { JoinPlayerBtn(4); }

        private void SetBetScreenButtons(GameObject bs, bool val, bool enableDone)
        {
            Button[] buttons = bs.GetComponentsInChildren<Button>();

            foreach (Button btn in buttons) {
                if (btn.name == "DoneButton") { // Simply match by name
                    btn.interactable = val && enableDone;
                } else {
                    btn.interactable = val;
                }
            }
        }

        private void UpdateBetScreens()
        {
            // TODO: update the maxbet displayed on betscreens based on oyaMaxbet minus all the bets
            //       in the bets array, except for your own
        }

        private void SendBetEvent(int player, int bet)
        {
            GameLogDebug(string.Format("SendBetEvent({0}, {1}), totalBet={2}",
                                       player, bet, totalBet));

            if ((totalBet + bet)*3 > udonChips.money) {
                // TODO: local error sound effect and more obvious display than GameLog
                GameLog(string.Format("<color=\"red\">You can at most bet a third of your total ({0})</color>",
                                      formatChips(udonChips.money/3.0f)));
                return;
            }

            // Temporarily disable betscreen buttons here until we get a
            // message back from the owner to avoid double-presses.
            SetBetScreenButtons(betScreens[player - 1], false, false);

            string fnname = string.Format("EventPlayer{0}Bet{1}", player, bet);
            SendToOya(fnname);
        }

        private void SendBetUndoEvent(int player)
        {
            SetBetScreenButtons(betScreens[player - 1], false, false);

            string fnname = string.Format("EventPlayer{0}BetUndo", player);
            SendToOya(fnname);
        }

        private void SendBetDoneEvent(int player)
        {
            SetBetScreenButtons(betScreens[player - 1], false, false);

            string fnname = string.Format("EventPlayer{0}BetDone", player);
            SendToOya(fnname);
        }

        public void _BtnPlayer1Bet10()   { SendBetEvent(1, 10); }
        public void _BtnPlayer1Bet50()   { SendBetEvent(1, 50); }
        public void _BtnPlayer1Bet100()  { SendBetEvent(1, 100); }
        public void _BtnPlayer1Bet500()  { SendBetEvent(1, 500); }
        public void _BtnPlayer1BetUndo() { SendBetUndoEvent(1); }
        public void _BtnPlayer1BetDone() { SendBetDoneEvent(1); }

        public void _BtnPlayer2Bet10()  { SendBetEvent(2, 10); }
        public void _BtnPlayer2Bet50()  { SendBetEvent(2, 50); }
        public void _BtnPlayer2Bet100() { SendBetEvent(2, 100); }
        public void _BtnPlayer2Bet500() { SendBetEvent(2, 500); }
        public void _BtnPlayer2BetUndo() { SendBetUndoEvent(2); }
        public void _BtnPlayer2BetDone() { SendBetDoneEvent(2); }

        public void _BtnPlayer3Bet10()  { SendBetEvent(3, 10); }
        public void _BtnPlayer3Bet50()  { SendBetEvent(3, 50); }
        public void _BtnPlayer3Bet100() { SendBetEvent(3, 100); }
        public void _BtnPlayer3Bet500() { SendBetEvent(3, 500); }
        public void _BtnPlayer3BetUndo() { SendBetUndoEvent(3); }
        public void _BtnPlayer3BetDone() { SendBetDoneEvent(3); }

        public void _BtnPlayer4Bet10()  { SendBetEvent(4, 10); }
        public void _BtnPlayer4Bet50()  { SendBetEvent(4, 50); }
        public void _BtnPlayer4Bet100() { SendBetEvent(4, 100); }
        public void _BtnPlayer4Bet500() { SendBetEvent(4, 500); }
        public void _BtnPlayer4BetUndo() { SendBetUndoEvent(4); }
        public void _BtnPlayer4BetDone() { SendBetDoneEvent(4); }

        // EventPlayerXBetY
        public void EventPlayer1Bet10()   { RecvBetEvent(1, 10.0f); }
        public void EventPlayer1Bet50()   { RecvBetEvent(1, 50.0f); }
        public void EventPlayer1Bet100()  { RecvBetEvent(1, 100.0f); }
        public void EventPlayer1Bet500()  { RecvBetEvent(1, 500.0f); }
        public void EventPlayer1BetUndo() { RecvBetUndoEvent(1); }
        public void EventPlayer1BetDone() { RecvBetDoneEvent(1); }

        public void EventPlayer2Bet10()   { RecvBetEvent(2, 10.0f); }
        public void EventPlayer2Bet50()   { RecvBetEvent(2, 50.0f); }
        public void EventPlayer2Bet100()  { RecvBetEvent(2, 100.0f); }
        public void EventPlayer2Bet500()  { RecvBetEvent(2, 500.0f); }
        public void EventPlayer2BetUndo() { RecvBetUndoEvent(2); }
        public void EventPlayer2BetDone() { RecvBetDoneEvent(2); }

        public void EventPlayer3Bet10()   { RecvBetEvent(3, 10.0f); }
        public void EventPlayer3Bet50()   { RecvBetEvent(3, 50.0f); }
        public void EventPlayer3Bet100()  { RecvBetEvent(3, 100.0f); }
        public void EventPlayer3Bet500()  { RecvBetEvent(3, 500.0f); }
        public void EventPlayer3BetUndo() { RecvBetUndoEvent(3); }
        public void EventPlayer3BetDone() { RecvBetDoneEvent(3); }

        public void EventPlayer4Bet10()   { RecvBetEvent(4, 10.0f); }
        public void EventPlayer4Bet50()   { RecvBetEvent(4, 50.0f); }
        public void EventPlayer4Bet100()  { RecvBetEvent(4, 100.0f); }
        public void EventPlayer4Bet500()  { RecvBetEvent(4, 500.0f); }
        public void EventPlayer4BetUndo() { RecvBetUndoEvent(4); }
        public void EventPlayer4BetDone() { RecvBetDoneEvent(4); }

        private bool isOya()
        {
            return iAmPlayer > 0 && iAmPlayer <= MAX_PLAYERS && iAmPlayer == oya;
        }

        private bool isOwner()
        {
            // The oya owns the gameobject, and acts as "master" of the game
            return Networking.IsOwner(gameObject);
        }

        // DiGrabSphereListener
        public void _SetThrown()
        {
            // Do nothing
        }

        // DieGrabSphereListener
        public void _SetHeld()
        {
            // Do nothing
        }

        // DieGrabSphereListener
        public void _DiceResult0() { SendDiceResultEvent(0, iAmPlayer); }
        public void _DiceResult1() { SendDiceResultEvent(1, iAmPlayer); }
        public void _DiceResult2() { SendDiceResultEvent(2, iAmPlayer); }
        public void _DiceResult3() { SendDiceResultEvent(3, iAmPlayer); }
        public void _DiceResult4() { SendDiceResultEvent(4, iAmPlayer); }
        public void _DiceResult5() { SendDiceResultEvent(5, iAmPlayer); }
        public void _DiceResult6() { SendDiceResultEvent(6, iAmPlayer); }

        // We have unique network events per player so the server can tel if the result we got was
        // from the player we expected results for.
        private void SendDiceResultEvent(int result, int player)
        {
            if (player < 1 || player > MAX_PLAYERS) {
                string str = string.Format("SendDiceResultEvent({0},{1}): invalid player number",
                                           result, player);
                Debug.LogError(str);
                GameLogError(str);
                return;
            }

            // Sends a single dice result to Oya (owner)
            string fnname = string.Format("EventDiceResult{0}Player{1}", result, player);
            SendToOya(fnname);
        }

        private string formatChips(float amount)
        {
            return string.Format(udonChips.format, amount);
        }

        // EventDiceResultXPlayerY
        public void EventDiceResult0Player1() { RecvEventDiceResult(0, 1); }
        public void EventDiceResult1Player1() { RecvEventDiceResult(1, 1); }
        public void EventDiceResult2Player1() { RecvEventDiceResult(2, 1); }
        public void EventDiceResult3Player1() { RecvEventDiceResult(3, 1); }
        public void EventDiceResult4Player1() { RecvEventDiceResult(4, 1); }
        public void EventDiceResult5Player1() { RecvEventDiceResult(5, 1); }
        public void EventDiceResult6Player1() { RecvEventDiceResult(6, 1); }

        public void EventDiceResult0Player2() { RecvEventDiceResult(0, 2); }
        public void EventDiceResult1Player2() { RecvEventDiceResult(1, 2); }
        public void EventDiceResult2Player2() { RecvEventDiceResult(2, 2); }
        public void EventDiceResult3Player2() { RecvEventDiceResult(3, 2); }
        public void EventDiceResult4Player2() { RecvEventDiceResult(4, 2); }
        public void EventDiceResult5Player2() { RecvEventDiceResult(5, 2); }
        public void EventDiceResult6Player2() { RecvEventDiceResult(6, 2); }

        public void EventDiceResult0Player3() { RecvEventDiceResult(0, 3); }
        public void EventDiceResult1Player3() { RecvEventDiceResult(1, 3); }
        public void EventDiceResult2Player3() { RecvEventDiceResult(2, 3); }
        public void EventDiceResult3Player3() { RecvEventDiceResult(3, 3); }
        public void EventDiceResult4Player3() { RecvEventDiceResult(4, 3); }
        public void EventDiceResult5Player3() { RecvEventDiceResult(5, 3); }
        public void EventDiceResult6Player3() { RecvEventDiceResult(6, 3); }

        public void EventDiceResult0Player4() { RecvEventDiceResult(0, 4); }
        public void EventDiceResult1Player4() { RecvEventDiceResult(1, 4); }
        public void EventDiceResult2Player4() { RecvEventDiceResult(2, 4); }
        public void EventDiceResult3Player4() { RecvEventDiceResult(3, 4); }
        public void EventDiceResult4Player4() { RecvEventDiceResult(4, 4); }
        public void EventDiceResult5Player4() { RecvEventDiceResult(5, 4); }
        public void EventDiceResult6Player4() { RecvEventDiceResult(6, 4); }

        private void SetButtonText(GameObject btn, string str)
        {
            // TODO: switch to GetComponentInChildren ?
            GameObject txtObj = btn.transform.GetChild(0).gameObject;
            TextMeshProUGUI text = txtObj.GetComponent<TextMeshProUGUI>();
            text.text = str;
        }

        private void SetBetLabel(int player, float amount)
        {
            betLabels[player - 1].text =
                string.Format("Player {0}\nBet: {1}", player, formatChips(amount));
        }

        private void UpdateJoinButtons(bool[] pa)
        {
            for (int i = 0; i < MAX_PLAYERS; ++i) {
                if (iAmPlayer <= 0) {
                    joinButtons[i].SetActive(!pa[i]);
                    SetButtonText(joinButtons[i], "Join");
                } else {
                    joinButtons[i].SetActive(i + 1 == iAmPlayer);
                    SetButtonText(joinButtons[i], "Leave");
                }
            }
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (Networking.IsOwner(gameObject)) {
                // If we were explicitly instructed to become oya, iAmPlayer
                // should equal oya for the owner of the object. If this is not
                // the case, this means the oya left the instance.

                if (iAmPlayer <= 0 || iAmPlayer != oya) {
                    GameLog("Oya disappeared: Game reset");
                    Broadcast(mkop_nooya());
                }
            }
        }

        public override void OnDeserialization()
        {
            if (op_getop(arg0) == OPCODE_OYAREPORT) {
                int player = opoyareport_oya(arg0);
                ResetTable(); // Reset the bet displays and such
                ResetClientVariables();

                oya = player;
                bool[] pa = opoyareport_playerActive(arg0);
                GameLog(string.Format("P{0} is oya (playerActive: {1},{2},{3},{4})",
                                      oya, pa[0], pa[1], pa[2], pa[3]));
                UpdateJoinButtons(pa);
            } else if (op_getop(arg0) == OPCODE_WAITINGFORROUNDSTART) {
                GameLog("Waiting for oya to start the round...");
                if (isOya()) {
                    startRoundButtons[oya - 1].SetActive(true);
                } else {
                    oya = opwaitingforroundstart_oya(arg0);
                }
            } else if (op_getop(arg0) == OPCODE_ENABLE_BET) {
                int player = openable_bet_getplayer(arg0);
                float maxbet = openable_bet_getoyamaxbet(arg0);
                GameLogDebug(string.Format("Bet screen enabled for P{0} (maxbet={1})",
                                           player, formatChips(maxbet)));

                GameObject bs = betScreens[player - 1];
                bs.SetActive(true);
                SetBetScreenButtons(bs, false, false);
                if (iAmPlayer == player) {
                    oyaMaxBet = maxbet;
                    SetBetScreenButtons(bs, true, false);
                    totalBet = 0.0f;
                }
                timeoutDisplays[player - 1].SetActive(true);
                UpdateBetScreens();
            } else if (op_getop(arg0) == OPCODE_BET) {
                int player = opbet_getplayer(arg0);
                float total = opbet_gettotal(arg0);
                GameLogDebug(string.Format("P{0} increased their bet to {1}", player, formatChips(total)));
                // Here we could display chips or something coming up for each press
                SetBetLabel(player, total);
                if (iAmPlayer == player) {
                    totalBet = total;
                    SetBetScreenButtons(betScreens[player - 1], true, total > 0.0f);
                }
                if (!isOya()) {
                    bets[player - 1] = total;
                }
                UpdateBetScreens();
            } else if (op_getop(arg0) == OPCODE_BETUNDO) {
                int player = opbet_getplayer(arg0);
                GameLogDebug(string.Format("P{0} reset their bet", player));
                // Here we would reset any chips displayed or so
                SetBetLabel(player, 0.0f);
                if (iAmPlayer == player) {
                    totalBet = 0.0f;
                    SetBetScreenButtons(betScreens[player - 1], true, false);
                }
                if (!isOya()) {
                    bets[player - 1] = 0.0f;
                }
                UpdateBetScreens();
            } else if (op_getop(arg0) == OPCODE_BETDONE) {
                int player = opbet_getplayer(arg0);
                float total = opbet_gettotal(arg0);
                GameObject bs = betScreens[player - 1];
                bs.SetActive(false);
                GameLog(string.Format("P{0} bet {1}", player, formatChips(total)));
                SetBetLabel(player, total);
                if (iAmPlayer == player) {
                    totalBet = total;
                    SetBetScreenButtons(bs, false, false);
                }
                if (!isOya()) {
                    bets[player - 1] = total;
                }
                timeoutDisplays[player - 1].SetActive(false);
                joinButtons[player - 1].SetActive(false); // Disable leave button at this time (apply globally just in case)
                UpdateBetScreens();
            } else if (op_getop(arg0) == OPCODE_BETREJECT) {
                int player = opbet_getplayer(arg0);
                float total = opbet_gettotal(arg0);
                GameLogDebug(string.Format("Oya rejected bet from P{0}", player, formatChips(total)));
                SetBetLabel(player, total);
                if (iAmPlayer == player) {
                    totalBet = total;
                    // TODO: local error sound effect and more obvious display than GameLog
                    GameLog("<color=\"red\">Oya rejected bet as too big</color>");
                    GameObject bs = betScreens[player - 1];
                    SetBetScreenButtons(bs, true, total > 0.0f);
                }
                if (!isOya()) {
                    bets[player - 1] = total;
                }
                UpdateBetScreens();
            } else if (op_getop(arg0) == OPCODE_PLAYERJOIN) {
                int player = opplayer_player(arg0);
                oya = opplayerjoin_oya(arg0); // Syncs up this variable in case we don't have it
                bool[] pa = opplayer_playerActive(arg0);
                bool showbuttons = opplayer_showbuttons(arg0);
                GameLog(string.Format("P{0} entered the game (playerActive: {1},{2},{3},{4})",
                                      player, pa[0], pa[1], pa[2], pa[3]));
                if (showbuttons)
                    UpdateJoinButtons(pa);
            } else if (op_getop(arg0) == OPCODE_PLAYERLEAVE) {
                int player = opplayer_player(arg0);
                bool[] pa = opplayer_playerActive(arg0);
                bool showbuttons = opplayer_showbuttons(arg0);
                GameLog(string.Format("P{0} left the game (playerActive: {1},{2},{3},{4})",
                                      player, pa[0], pa[1], pa[2], pa[3]));
                if (!isOya()) {
                    bets[player - 1] = 0.0f;
                }
                SetBetLabel(player, 0.0f);
                GameObject bs = betScreens[player - 1];
                bs.SetActive(false);
                UpdateBetScreens();

                if (iAmPlayer == player)
                    iAmPlayer = -1;
                if (showbuttons)
                    UpdateJoinButtons(pa); // TODO: need to get an indication from server whether to show buttons or not

                if (!showbuttons && iAmPlayer == player) {
                    joinButtons[player - 1].SetActive(false);
                }
            } else if (op_getop(arg0) == OPCODE_DISABLE_JOINBUTTONS) {
                GameLogDebug("Disable join buttons");
                foreach (GameObject btn in joinButtons) {
                    btn.SetActive(false);
                }
            } else if (op_getop(arg0) == OPCODE_YOURTHROW) {
                int player = opyourthrow_player(arg0);
                int rethrow = opyourthrow_rethrow(arg0);

                if (iAmPlayer == player) {
                    dieGrabSphere._BecomeOwner();
                    dieGrabSphere._TeleportTo(diceSpawns[player - 1]);
                    dieGrabSphere._ParkDice();
                    // Make it pickupable only for the player whose turn it is
                    dieGrabSphere._SetPickupable(true);
                }

                timeoutDisplays[player-1].SetActive(true); // Show the timeout display to everyone
                dieGrabSphere._Show();

                if (rethrow > 0) {
                    GameLog(string.Format("P{0} rethrow ({1}/3)", player, rethrow + 1));
                } else {
                    GameLog(string.Format("P{0} it is your turn", player));
                }
                // TODO: perhaps a sound effect to draw the users attention? (it
                // should be fine just to do it inline here)
            } else if (op_getop(arg0) == OPCODE_THROWRESULT) {
                int player = opthrow_player(arg0);
                int[] result = opthrow_result(arg0);
                uint throw_type = opthrow_type(arg0);
                if (throw_type == THROW_SHONBEN)
                    GameLog(string.Format("P{0} threw outside", player));
                else
                    GameLog(string.Format("P{0} threw {1} {2} {3}: {4}",
                                          player, result[0], result[1], result[2],
                                          formatThrowType(throw_type)));

                // TODO: specific display for the result
                timeoutDisplays[player - 1].SetActive(false);
            } else if (op_getop(arg0) == OPCODE_OYATHROWRESULT) {
                int player = opthrow_player(arg0);
                int[] result = opthrow_result(arg0);
                uint throw_type = opthrow_type(arg0);
                if (throw_type == THROW_SHONBEN)
                    GameLog(string.Format("P{0} (oya) threw outside", player));
                else
                    GameLog(string.Format("P{0} (oya) threw {1} {2} {3}: {4}",
                                          player, result[0], result[1], result[2],
                                          formatThrowType(throw_type)));

                // TODO: specific display for the result
                timeoutDisplays[player - 1].SetActive(false);
            } else if (op_getop(arg0) == OPCODE_BALANCE) {
                int player = opbalance_player(arg0);
                float amount = opbalance_amount(arg0);
                if (iAmPlayer > 0 && player == iAmPlayer)
                    udonChips.money += amount;

                if (amount >= 0.0f)
                    GameLog(string.Format("P{0} won {1}", player, formatChips(Mathf.Abs(amount))));
                else
                    GameLog(string.Format("P{0} lost {1}", player, formatChips(Mathf.Abs(amount))));
            } else if (op_getop(arg0) == OPCODE_OYACHANGE) {
                int toPlayer = opoyachange_toplayer(arg0);
                int fromPlayer = opoyachange_fromplayer(arg0);
                bool[] pa = opoyachange_playerActive(arg0);
                oya = toPlayer;

                // TODO: some sort of sound-effect or other clearly easy-to-understand thing to inform everyone

                // TODO: display clearly who is oya (change playerlabel?)
                if (fromPlayer <= 0)
                    GameLog(string.Format("P{0} became oya (playerActive: {1},{2},{3},{4})",
                                          toPlayer, pa[0], pa[1], pa[2], pa[3]));
                else
                    GameLog(string.Format("Oya P{0} -> P{1} (playerActive: {2},{3},{4},{5})",
                                          fromPlayer, toPlayer, pa[0], pa[1], pa[2], pa[3]));

                // Nobody gets to leave or join until oyareport, because it can
                // break the state machine and cause an infinite loop (TODO:
                // reatructure state-machine to be explicitly event-driven and
                // in general suck less. That way the state-machine should be
                // able to reject events in states it can't handle them)
                foreach (GameObject btn in joinButtons)
                    btn.SetActive(false);

                SetBetLabel(toPlayer, 0.0f);
                GameObject bs = betScreens[toPlayer - 1];
                bs.SetActive(false);
                bets[toPlayer - 1] = 0.0f;
                UpdateBetScreens();

                if (iAmPlayer > 0 && toPlayer == iAmPlayer) {
                    // Become owner/oya
                    if (!Networking.IsOwner(gameObject))
                        Networking.SetOwner(Networking.LocalPlayer, gameObject);

                    ResetServerVariables();
                    if (fromPlayer > 0)
                        opqueue_Reset();
                    // Set playerActive based on what previous oya told us
                    playerActive = pa;

                    // Start the oya state machine here
                    state = STATE_FIRST;
                    SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
                }
            } else if (op_getop(arg0) == OPCODE_NOOYA) {
                // reset various things like text displays, betting screens etc.

                // clear non-debug GameLog
                gameLog._Clear();

                GameLog("Table is empty");
                ResetServerVariables();
                opqueue_Reset();
                ResetClientVariables();
                oya = -1;
                iAmPlayer = -1;

                ResetTable();
                UpdateJoinButtons(playerActive);
            }
        }

        private void ResetTable()
        {
            for (int i = 0; i < MAX_PLAYERS; ++i) {
                betScreens[i].SetActive(false);
                startRoundButtons[i].SetActive(false);
                timeoutDisplays[i].SetActive(false);

                bets[i] = 0.0f;

                // Join  buttons handled separately
                //  joinButtons[i].SetActive(true);
                //  SetButtonText(joinButtons[i], "Join");

                int player = i + 1;
                SetBetLabel(player, 0.0f);
            }
            dieGrabSphere._HideWithDice();
        }

        #region states
        private readonly int STATE_FIRST = 1;
        private readonly int STATE_OYAREPORT = 1;
        private readonly int STATE_WAITINGFORPLAYERS = 5;
        private readonly int STATE_WAITINGFORROUNDSTART = 6;
        private readonly int STATE_WAITINGFORBETS = 8;
        private readonly int STATE_PREPARE_OYATHROW = 10;
        private readonly int STATE_OYATHROW = 11;
        private readonly int STATE_PREPARE_THROWS = 20;
        private readonly int STATE_PREPARE_THROW = 21;
        private readonly int STATE_THROW = 22;
        private readonly int STATE_BALANCE = 31;

        private void PrepareRecvThrow()
        {
            for (int i = 0; i < recvResult.Length; ++i)
                recvResult[i] = -1;
            recvResult_cntr = 0;
        }

        public bool _ToNextOya()
        {
            int idx = iAmPlayer - 1;
            for (int i = 0; i < MAX_PLAYERS; ++i) {
                if ((idx % MAX_PLAYERS) + 1 != iAmPlayer && playerActive[idx % MAX_PLAYERS]) {
                    int newOya = (idx % MAX_PLAYERS) + 1;
                    oya = newOya; // Set this already here so OnOwnershipTransferred knows this wasn't the oya leaving the instance
                    Broadcast(mkop_oyachange(iAmPlayer, newOya, playerActive));
                    ResetServerVariables();
                    return true;
                }
                idx++;
            }

            return false;
        }

        public void _OyaStateMachine()
        {
            if (!Networking.IsOwner(gameObject)) {
                Debug.LogError("OyaStateMachine called by non-owner");
                GameLogError("OyaStateMachine called by non-owner");
                return;
            }

            if (iAmPlayer != oya) {
                Debug.LogError("OyaStateMachine called by non-oya");
                GameLogError("OyaStateMachine called by non-oya");
                return;
            }

            // We use continue to go directly to another state and return in
            // case we need to wait on some event (reception of those events is
            // also responsible for calling the state machine again)
            while (true) {
                if (state == STATE_OYAREPORT) {
                    GameLogDebug("state = STATE_OYAREPORT");
                    oyaLost = false;


                    Broadcast(mkop_oyareport(iAmPlayer, playerActive));

                    // Show betscreen to everyone except oya
                    for (int i = 0; i < MAX_PLAYERS; ++i) {
                        if ((oya - 1) != i && playerActive[i]) {
                            Broadcast(mkop_enable_bet(i + 1, getOyaMaxBet()));
                        }
                    }

                    state = STATE_WAITINGFORPLAYERS;
                    continue;
                } else if (state == STATE_WAITINGFORPLAYERS) {
                    GameLogDebug("state = STATE_WAITINGFORPLAYERS");

                    if (getActivePlayerCount() >= 2) {
                        for (int i = 0; i < MAX_PLAYERS; ++i) {
                            bets[i] = 0.0f;
                            betDone[i] = false;
                            betMultiplier[i] = 0;
                        }
                        betDone[oya - 1] = true; // Oya doesn't bet, so they're "done"

                        state = STATE_WAITINGFORBETS;
                        continue;
                    }

                    // TODO: A clearer way to indicate this?
                    GameLog("<color=yellow>Waiting for players to join...</color>");
                    return; // Wait for players to increase
                } else if (state == STATE_WAITINGFORBETS) {
                    GameLogDebug("state = STATE_WAITINGFORBETS");

                    // We might come in here from STATE_WAITINGFORROUNDSTART in case a new player
                    // joined. Thus need to make sure this button dissappears.
                    startRoundButtons[oya - 1].SetActive(false);

                    bool allbetsin = true;
                    for (int i = 0; i < MAX_PLAYERS; ++i)
                        if (playerActive[i] && !betDone[i])
                            allbetsin = false;

                    if (allbetsin) {
                        state = STATE_WAITINGFORROUNDSTART;
                        continue;
                    } else {
                        // TODO: a clearer indication?
                        GameLog("<color=yellow>Waiting for players to bet...</color>");
                        return; // Wait for bets
                    }
                } else if (state == STATE_WAITINGFORROUNDSTART) {
                    GameLogDebug("state = STATE_WAITINGFORROUNDSTART");

                    Broadcast(mkop_waitingforroundstart(oya));
                    return; // Wait for button press / any additional joins coming through
                } else if (state == STATE_PREPARE_OYATHROW) {
                    GameLogDebug("state = STATE_PREPARE_OYATHROW");
                    rethrowCount = 0;
                    state = STATE_OYATHROW;
                    continue;
                } else if (state == STATE_OYATHROW) {
                    GameLogDebug("state = STATE_OYATHROW");

                    if (rethrowCount < MAX_RETHROWS) {
                        PrepareRecvThrow();
                        Broadcast(mkop_yourthrow(oya, rethrowCount));
                        return; // Wait on throw result
                    } else {
                        oyaLost = true;
                        for (int i = 0; i < betMultiplier.Length; ++i)
                            betMultiplier[i] = 1;
                        state = STATE_BALANCE;
                        continue;
                    }
                } else if (state == STATE_PREPARE_THROWS) {
                    GameLogDebug("state = STATE_PREPARE_THROWS");

                    for (int i = 0; i < betMultiplier.Length; ++i)
                        betMultiplier[i] = 0;
                    currentPlayer = 1;
                    state = STATE_PREPARE_THROW;
                    continue;
                } else if (state == STATE_PREPARE_THROW) {
                    GameLogDebug(string.Format("state = STATE_PREPARE_THROW, currentPlayer={0}", currentPlayer));
                    rethrowCount = 0;
                    state = STATE_THROW;
                    continue;
                } else if (state == STATE_THROW) {
                    GameLogDebug(string.Format("state = STATE_THROW, currentPlayer={0}", currentPlayer));

                    if (currentPlayer > MAX_PLAYERS) {
                        currentPlayer = -1; // Reset this so we get obvious errors if this gets used in the wrong place
                        state = STATE_BALANCE;
                        continue;
                    }

                    // Skip if oya, or not active
                    if (oya == currentPlayer || !playerActive[currentPlayer - 1]) {
                        ++currentPlayer;
                        state = STATE_PREPARE_THROW;
                        continue;
                    }

                    if (rethrowCount < MAX_RETHROWS) {
                        PrepareRecvThrow();
                        Broadcast(mkop_yourthrow(currentPlayer, rethrowCount));
                        return; // Wait on throw result
                    } else {
                        betMultiplier[currentPlayer - 1] = -1;
                        // TODO: event/opcode to inform everyone about the fail? Maybe a sound effect? Throwresult?
                        ++currentPlayer;
                        state = STATE_PREPARE_THROW;
                        continue;
                    }
                } else if (state == STATE_BALANCE) {
                    GameLogDebug("state = STATE_BALANCE");

                    for (int i = 0; i < MAX_PLAYERS; ++i) {
                        if (i + 1 != oya && playerActive[i]) {
                            float amount = bets[i]*betMultiplier[i];
                            GameLogDebug(string.Format("amount={1}, bets[{0}]={2}, betMultiplier[{0}]={3}",
                                                       i, amount, bets[i], betMultiplier[i]));
                            udonChips.money -= amount;
                            Broadcast(mkop_balance(i+1, amount));
                        }
                    }

                    if (oyaLost) {
                        // We failed. All gets reset and oya gets passed on
                        // TODO: Better indication that oya lost to everyone (display and sound effect?)
                        SendCustomEventDelayedSeconds(nameof(_ToNextOya), 3);
                        // After the above gets to run we are no longer oya, and no longer run the
                        // state machine
                    } else {
                        // Round is done. Start over from the top
                        state = STATE_FIRST;
                        SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 3);
                    }
                    return;
                }
            }
        }

        private float getOyaMaxBet()
        {
            return System.Math.Min(udonChips.money/3, MAXBET);
        }

        // TODO: perhaps limit the states during which a player can join? (would require a way to
        // communicate to a joining player that they couldn't join)
        private void RecvEventPlayerJoin(int player)
        {
            // Nominally takes place during any of STATE_WAITINGFORPLAYERS, STATE_WAITINGFORBETS or STATE_WAITINGFORROUNDSTART

            GameLogDebug(string.Format("RecvEventPlayerJoin({0}), state={1}", player, state));

            if (player < 1 || player > MAX_PLAYERS) {
                Debug.LogError("invalid player variable");
                GameLogError("invalid player variable");
                return;
            }

            playerActive[player - 1] = true;
            bool showButtons = (state == STATE_WAITINGFORPLAYERS ||
                                state == STATE_WAITINGFORBETS ||
                                state == STATE_WAITINGFORROUNDSTART);
            Broadcast(mkop_playerjoin(player, oya, playerActive, showButtons));

            // Open the bet screen for the player, also tell them how much money oya has (can
            // display max bet locally)
            Broadcast(mkop_enable_bet(player, getOyaMaxBet()));

            // State waitingforplayers needs this here to be able to proceed to the next state
            if (state == STATE_WAITINGFORPLAYERS) {
                _OyaStateMachine();
            } else if (state == STATE_WAITINGFORROUNDSTART) {
                // Player joined before oya started round. We need to go back a bit and wait for them to bet.
                state = STATE_WAITINGFORBETS;
                _OyaStateMachine();
            }
        }

        // TODO: perhaps it would make sense to switch to state machine function
        // that as arguments takes event and event arguments. Then we could
        // handle tricky things like this inline in OyaStateMachine
        private void RecvEventPlayerLeave(int player)
        {
            // Nominally takes place during any of STATE_WAITINGFORPLAYERS, STATE_WAITINGFORBETS or
            // STATE_WAITINGFORROUNDSTART, but could potentially happen elsewhere when shenanigans abound.

            GameLogDebug(string.Format("RecvEventPlayerLeave({0}), state={1}", player, state));

            if (player < 1 || player > MAX_PLAYERS) {
                Debug.LogError("invalid player variable");
                GameLogError("invalid player variable");
                return;
            }

            playerActive[player - 1] = false;
            bets[player - 1] = 0.0f;
            betMultiplier[player - 1] = 0;
            betDone[player - 1] = false;

            // If oya left
            // If not set up arg0 so that the game is obviously unoccupied
            if (player == oya && player == iAmPlayer) {
                startRoundButtons[oya - 1].SetActive(false); // Disable this button early, in case it was enabled

                // TODO: opcode/event that informs everyone that the oya is cowardly fleeing?

                // We try to transfer to next player (we can't transfer to the
                // leaving player by virtue of having removed them from playerActive)
                bool found = _ToNextOya();
                // If we didn't find another player to transfer to we are
                // obviously the last person leaving, and the game should be
                // reset so that the first person to join becomes owner
                if (!found) {
                    Broadcast(mkop_nooya());
                }
            } else {
                bool showButtons = (state == STATE_WAITINGFORPLAYERS ||
                                    state == STATE_WAITINGFORBETS ||
                                    state == STATE_WAITINGFORROUNDSTART);
                Broadcast(mkop_playerleave(player, playerActive, showButtons));

                if (getActivePlayerCount() < 2) {
                    // Too few to play. We have to go back to STATE_FIRST
                    state = STATE_FIRST;
                    _OyaStateMachine();
                } else {
                    // Handle leaving during certain states etc.
                    if ((state == STATE_PREPARE_THROW || state == STATE_THROW) && player == currentPlayer) {
                        ++currentPlayer;
                        _OyaStateMachine();
                    } else if (state == STATE_WAITINGFORBETS) {
                        // One player that hasn't betted leaving means we might be able to go to STATE_WAITINGFORROUNDSTART
                        _OyaStateMachine();
                    }
                }
            }
        }

        // This button is only ever visible to the owner (oya) so there's no need for networking.
        public void _BtnStartRound()
        {
            if (state == STATE_WAITINGFORROUNDSTART) {
                startRoundButtons[oya - 1].SetActive(false);

                // Disable join/leave buttons everywhere
                Broadcast(mkop_disablejoinbuttons());
                
                state = STATE_PREPARE_OYATHROW;
                _OyaStateMachine();
            }
        }

        private void RecvBetEvent(int player, float amount)
        {
            GameLogDebug(string.Format("RecvBetEvent({0}, {1})", player, amount));

            if (state == STATE_WAITINGFORBETS) {
                bool reject = false;

                float totalBets = getTotalBets() + amount;
                if (totalBets*3.0f > udonChips.money) {
                    GameLogDebug(string.Format("Reject bet, because oya might not be able to pay (getTotalBets()+amount={0}, udonChips.money={1})",
                                               formatChips(totalBets), formatChips(udonChips.money)));
                    reject = true;
                }

                if (bets[player - 1] + amount > MAXBET) {
                    GameLogDebug("Reject bet, because it is over MAXBET");
                    reject = true;
                }

                uint op;
                if (reject) {
                    op = mkop_betreject(player, bets[player - 1]);
                } else {
                    bets[player - 1] += amount;
                    op = mkop_bet(player, bets[player - 1]);
                }
                Broadcast(op);
            }
        }

        private void RecvBetUndoEvent(int player)
        {
            if (state == STATE_WAITINGFORBETS) {
                bets[player - 1] = 0.0f;
                Broadcast(mkop_betundo(player));
            }
        }

        private void RecvBetDoneEvent(int player)
        {
            GameLogDebug("RecvBetDoneEvent");

            // TODO: reject if players bet is too small (0.0f)

            if (state == STATE_WAITINGFORBETS) {
                betDone[player - 1] = true;
                Broadcast(mkop_betdone(player, bets[player - 1]));
                _OyaStateMachine();
            }
        }

        private bool validDiceResult(int[] array)
        {
            foreach (int ele in array) {
                if (ele < 0 || ele > 6) // 0 is "valid" in that it means outside
                    return false;
            }
            return true;
        }

        private uint _ProcessDiceResult_throw_type;
        private int _ProcessDiceResult_player;
        private void ProcessDiceResult()
        {
            GameLogDebug("ProcessDiceResult");
            
            // Assert: recvResult_cntr == 3 and that recvResult contains valid numbers
            if (recvResult_cntr != 3 || !validDiceResult(recvResult))
            {
                Debug.LogError("Invalid dice result");
                GameLogError("Invalid dice result");
                // Give user another throw
                _OyaStateMachine();
            }

            // Send a throwresult to everyone
            uint throw_type = classify_throw(recvResult);
            int player = 0;
            uint op;
            if (state == STATE_OYATHROW) {
                GameLogDebug("ProcessDiceResult, STATE_OYATHROW");
                player = oya;
                op = mkop_oyathrowresult(oya, recvResult, throw_type);
                for (int i = 0; i < oyaResult.Length; ++i) {
                    oyaResult[i] = recvResult[i];
                }
                oyaThrowType = throw_type;
                GameLogDebug(string.Format("Wrote oyaThrowType: {0}", oyaThrowType));
            } else {
                GameLogDebug(string.Format("ProcessDiceResult, STATE_THROW, currentPlayer={0}", currentPlayer));
                player = currentPlayer;
                op = mkop_throwresult(player, recvResult, throw_type);
            }
            Broadcast(op);

            // Delay here before we advance the state machine

            _ProcessDiceResult_throw_type = throw_type; // Save some vars for the continuation
            _ProcessDiceResult_player = player;
            SendCustomEventDelayedSeconds(nameof(_ProcessDiceResult_Continuation), 1.0f);
        }

        public void _ProcessDiceResult_Continuation()
        {
            GameLogDebug("_ProcessDiceResult_Continuation");

            if (!(state == STATE_OYATHROW || state == STATE_THROW))
                return;
            
            // Below here we advance the state machine based on the result
            uint throw_type = _ProcessDiceResult_throw_type;
            int player = _ProcessDiceResult_player;

            // Save the oya result for later reference
            if (state == STATE_OYATHROW) {
                if (throw_type == THROW_MENASHI) {
                    // Re-throw
                    rethrowCount++;
                    _OyaStateMachine();
                    return;
                }

                if (throw_type == THROW_SHONBEN) {
                    // Shonben insta-fail, pay-out and oya-switch
                    oyaLost = true;
                    for (int i = 0; i < betMultiplier.Length; ++i)
                        betMultiplier[i] = 1;
                    state = STATE_BALANCE;
                    _OyaStateMachine();
                    return;
                }

                if (throw_type == THROW_1 || throw_type == THROW_123) {
                    // Insta-fail and payout to everybody (different multiplier)
                    oyaLost = true;
                    int mult = (throw_type == THROW_123) ? 2 : 1;
                    for (int i = 0; i < betMultiplier.Length; ++i)
                        betMultiplier[i] = mult;
                    state = STATE_BALANCE;
                    _OyaStateMachine();
                    return;
                }

                // 6 points, 456 or Triple: Oya wins instantly (different multipliers)
                if (throw_type == THROW_6 || throw_type == THROW_456 || throw_type == THROW_ZOROME) {
                    int mult = (throw_type == THROW_456)    ? -2 :
                               (throw_type == THROW_ZOROME) ? -3 : -1;
                    for (int i = 0; i < betMultiplier.Length; ++i)
                        betMultiplier[i] = mult;
                    state = STATE_BALANCE;
                    _OyaStateMachine();
                    return;
                }

                // Is a result with between 2-5 points. Continue to STATE_PREPARE_THROWS
                state = STATE_PREPARE_THROWS;
                _OyaStateMachine();
            } else {
                if (throw_type == THROW_MENASHI) {
                    // Re-throw
                    rethrowCount++;
                    _OyaStateMachine();
                    return;
                }

                GameLogDebug(string.Format("throw_type: {0}, oyaThrowType: {1}", throw_type, oyaThrowType));

                if (throw_type == THROW_SHONBEN) {
                    betMultiplier[player-1] = -1;
                } else if (throw_type == THROW_6) {
                    oyaLost = true;
                    betMultiplier[player-1] = 1;
                } else if (throw_type == THROW_456) {
                    oyaLost = true;
                    betMultiplier[player-1] = 2;
                } else if (throw_type == THROW_ZOROME) {
                    oyaLost = true;
                    betMultiplier[player-1] = 3;
                } else if (throw_type == THROW_123) {
                    betMultiplier[player-1] = -2;
                } else if (throw_type == THROW_1) {
                    betMultiplier[player-1] = -1;
                } else if (throw_type > oyaThrowType) {
                    oyaLost = true;
                    betMultiplier[player-1] = 1;
                } else if (throw_type < oyaThrowType) {
                    betMultiplier[player-1] = -1;
                } else {
                    // wakare
                    betMultiplier[player-1] = 0;
                }

                // Run state machine for the next player (going to STATE_PREPARE_THROW)
                ++currentPlayer;
                state = STATE_PREPARE_THROW;
                _OyaStateMachine();
            }
        }

        private void RecvEventDiceResult(int result, int player)
        {
            GameLogDebug(string.Format("RecvEventDiceResult({0}, {1})", result, player));
            
            // Ignore the result if we got a result, but not for the player we expected (can happen
            // if a throw times out at a very inopportune moment)
            if (state == STATE_OYATHROW) {
                if (player != oya) {
                    GameLogDebug(string.Format("Ignore result: Expected from P{0} (oya) but got from P{1}", oya, player));
                    return;
                }
            } else if (state == STATE_THROW) {
                if (player != currentPlayer) {
                    GameLogDebug(string.Format("Ignore result: Expected from P{0} but got from P{1}", currentPlayer, player));
                    return;
                }
            }

            if (state == STATE_OYATHROW || state == STATE_THROW) {
                if (result < 0 || result > 6) { // 0 is "valid" in that it represents being outside
                    Debug.LogError("Invalid dice result: " + result.ToString());
                    GameLogError("Invalid dice result: " + result.ToString());
                    return;
                }

                int length = dieGrabSphere._GetLength();

                if (!(recvResult_cntr < length))
                    return;

                recvResult[recvResult_cntr++] = result;

                if (recvResult_cntr == length) {
                    ProcessDiceResult();
                }
            }
        }
        #endregion

        #region throw
        // Helpers for a simple little format that we use to compare dice results
        private uint diceres(uint d1, uint d2, uint d3) {
            uint d1part = d1 & 0xFFu;
            uint d2part = d2 & 0xFFu;
            uint d3part = d3 & 0xFFu;
            return d1part | d2part << 8 | d3part << 16;
        }

        private uint diceres_fromArray(int[] array)
        {
            return diceres((uint)array[0], (uint)array[1], (uint)array[2]);
        }

        // Throw result "enum":
        // The integers 1 to 6 represent that many points (pair + that die)
        // The values are chosen to fit in a nibble (4 bits), since they are
        // sent in OPCODE_THROWRESULT
        // Special values are:
        private readonly uint THROW_INVALID = 0;
        private readonly uint THROW_1 = 1;
        private readonly uint THROW_2 = 2;
        private readonly uint THROW_3 = 3;
        private readonly uint THROW_4 = 4;
        private readonly uint THROW_5 = 5;
        private readonly uint THROW_6 = 6;
        private readonly uint THROW_ZOROME = 7;
        private readonly uint THROW_456 = 8;
        private readonly uint THROW_123 = 9;
        private readonly uint THROW_MENASHI = 10;
        private readonly uint THROW_SHONBEN = 11;

        private uint classify_throw(int[] A)
        {
            GameLogDebug(string.Format("classify_throw(A=[{0},{1},{2}])", A[0], A[1], A[2]));

            if (A[0] == 0 || A[1] == 0 || A[2] == 0)
                return THROW_SHONBEN;

            uint res = diceres_fromArray(A);

            if (res == diceres(1,1,1) ||
                res == diceres(2,2,2) ||
                res == diceres(3,3,3) ||
                res == diceres(4,4,4) ||
                res == diceres(5,5,5) ||
                res == diceres(6,6,6)) {
                return THROW_ZOROME;
            }

            if (res == diceres(4,5,6) ||
                res == diceres(4,6,5) ||
                res == diceres(5,4,6) ||
                res == diceres(5,6,4) ||
                res == diceres(6,4,5) ||
                res == diceres(6,5,4)) {
                return THROW_456;
            }

            if (res == diceres(1,2,3) ||
                res == diceres(1,3,2) ||
                res == diceres(2,1,3) ||
                res == diceres(2,3,1) ||
                res == diceres(3,1,2) ||
                res == diceres(3,2,1)) {
                return THROW_123;
            }

            // Check for a pair
            if (A[0] == A[1])
                return (uint)A[2];

            if (A[0] == A[2])
                return (uint)A[1];

            if (A[1] == A[2])
                return (uint)A[0];

            return THROW_MENASHI;
        }

        private string formatThrowType(uint throw_type)
        {
            if (throw_type == THROW_INVALID)
                return "INVALID THROW TYPE";

            if (langJp) {
                if (throw_type == THROW_1)
                    return "一の目";
                if (throw_type == THROW_2)
                    return "二の目";
                if (throw_type == THROW_3)
                    return "三の目";
                if (throw_type == THROW_4)
                    return "四の目";
                if (throw_type == THROW_5)
                    return "五の目";
                if (throw_type == THROW_6)
                    return "六の目";

                if (throw_type == THROW_ZOROME)
                    return "ゾロ目";

                if (throw_type == THROW_456)
                    return "シゴロ";

                if (throw_type == THROW_123)
                    return "ヒフミ";

                if (throw_type == THROW_MENASHI)
                    return "目なし";

                if (throw_type == THROW_SHONBEN)
                    return "ションベン";
            } else {
                if (throw_type == 1)
                    return "1 point";

                if (throw_type >= 2 && throw_type <= 6)
                    return throw_type.ToString() + " points";

                if (throw_type == THROW_ZOROME)
                    return "Zorome";

                if (throw_type == THROW_456)
                    return "Shigoro";

                if (throw_type == THROW_123)
                    return "Hifumi";

                if (throw_type == THROW_MENASHI)
                    return "No points";

                if (throw_type == THROW_SHONBEN)
                    return "Shonben";
            }

            return "???";
        }
        #endregion

        #region opcodes
        // Opcodes for all broadcasts from Oya to everyone else. sent in arg0
        private readonly uint OPCODE_WAITINGFORROUNDSTART = 0x1u;
        private readonly uint OPCODE_ENABLE_BET = 0x2u; // Enable bet panels everywhere
        private readonly uint OPCODE_BET = 0x3u;
        private readonly uint OPCODE_BETUNDO = 0x4u;
        private readonly uint OPCODE_BETDONE = 0x5u; // Display that a particular player is done betting
        private readonly uint OPCODE_BETREJECT = 0x6u; // Oya did not allow bet for some reason
        private readonly uint OPCODE_OYAREPORT = 0xF0u; // update the oya variable &c.
        private readonly uint OPCODE_PLAYERJOIN = 0x10u; // A player has joined: Switch their join button to leave, and make it so only that player can press it
        private readonly uint OPCODE_PLAYERLEAVE = 0x11u;
        private readonly uint OPCODE_DISABLE_JOINBUTTONS = 0x12u;
        private readonly uint OPCODE_YOURTHROW = 0x20u; // This enables the dice for one particular player, but disables them (pickup disabled) for everyone else (if player nbr is 0 just disable)
        private readonly uint OPCODE_THROWRESULT = 0x21u;
        private readonly uint OPCODE_OYATHROWRESULT = 0x22u;
        private readonly uint OPCODE_BALANCE = 0x30u;   // This applies the change to a particular players udonChips balance (Oya applies changes to own balance on their own)
        private readonly uint OPCODE_OYACHANGE = 0xF1u; // Requests that another player take over as oya
        private readonly uint OPCODE_NOOYA     = 0x00u; // Sent by the oya when it is the last person leaving, resetting the game. Also the value in arg0 on start
        private readonly uint OPCODE_NOOP      = 0xFFu;

        uint op_getop(uint op)
        {
            return op & 0xFFu;
        }

        private uint mkop_waitingforroundstart(int oya)
        {
            uint oyapart = (uint)oya & 0b111u;
            return OPCODE_WAITINGFORROUNDSTART | oyapart << 8;
        }

        private int opwaitingforroundstart_oya(uint op)
        {
            return (int)((op >> 8) & 0b111u);
        }

        private uint mkop_enable_bet(int player, float oyamaxbet)
        {
            uint playerpart = (uint)player & 0b111u;   // Player numbers are three bit
            uint oyamaxbetpart = (uint)oyamaxbet & 0xFFFFu;  // 16 bit
            return OPCODE_ENABLE_BET | playerpart << 8 | oyamaxbetpart << 16;
        }

        private int openable_bet_getplayer(uint op)
        {
            return (int)((op >> 8) & 0b111u);
        }

        private float openable_bet_getoyamaxbet(uint op)
        {
            return (float)((op >> 16) & 0xFFFFu);
        }

        private uint mkop_bet(int player, float total)
        {
            uint playerpart = (uint)player & 0b111u;   // Player numbers are three bit
            uint totalpart = (uint)total & 0xFFFFu;  // 16 bit
            return OPCODE_BET | playerpart << 8 | totalpart << 16;
        }

        private uint mkop_betundo(int player)
        {
            uint playerpart = (uint)player & 0b111u;   // Player numbers are three bit
            return OPCODE_BETUNDO | playerpart << 8;
        }

        private uint mkop_betdone(int player, float total)
        {
            uint playerpart = (uint)player & 0b111u;
            uint totalpart = (uint)total & 0xFFFFu;
            return OPCODE_BETDONE | playerpart << 8 | totalpart << 16;
        }

        private uint mkop_betreject(int player, float total) // TODO: also inform about max bet? (requires switch to ulong)
        {
            uint playerpart = (uint)player & 0b111u;   // Player numbers are three bit
            uint totalpart = (uint)total & 0xFFFFu;  // 16 bit
            return OPCODE_BETREJECT | playerpart << 8 | totalpart << 16;
        }

        int opbet_getplayer(uint op)
        {
            return (int)((op >> 8) & 0b111u);
        }

        float opbet_gettotal(uint op)
        {
            return (float)((op >> 16) & 0xFFFFu);
        }

        private uint mkop_playerjoin(int player, int oya, bool[] playerActive, bool showbuttons)
        {
            uint playerpart = (uint)player & 0b111u;
            uint oyapart = (uint)oya & 0b111u;
            uint playerActivePart = _mk_playerActivePart(playerActive);
            uint showbuttonspart = (showbuttons) ? 1u : 0u;
            return OPCODE_PLAYERJOIN | playerpart << 8 | oyapart << 11 | playerActivePart << 14 | showbuttonspart << 18;
        }

        private int opplayerjoin_oya(uint op)
        {
            return (int)((op >> 11) & 0b111u);
        }

        private uint mkop_playerleave(int player, bool[] playerActive, bool showbuttons)
        {
            uint playerpart = (uint)player & 0b111u;
            uint playerActivePart = _mk_playerActivePart(playerActive);
            uint showbuttonspart = (showbuttons) ? 1u : 0u;
            return OPCODE_PLAYERLEAVE | playerpart << 8 | playerActivePart << 14 | showbuttonspart << 18;
        }

        private int opplayer_player(uint op)
        {
            return (int)((op >> 8) & 0b111u);
        }

        private bool[] opplayer_playerActive(uint op)
        {
            return _get_playerActive(op, 14);
        }

        private bool opplayer_showbuttons(uint op)
        {
            return ((op >> 18) & 0x1u) == 0x1u;
        }

        private uint mkop_disablejoinbuttons()
        {
            return OPCODE_DISABLE_JOINBUTTONS;
        }

        private uint mkop_yourthrow(int player, int rethrow)
        {
            uint playerpart = (uint)player & 0b111u;
            uint rethrowpart = (uint)rethrow & 0b111u;
            return OPCODE_YOURTHROW | playerpart << 8 | rethrowpart << 25;
        }

        private int opyourthrow_player(uint op)
        {
            return (int)((op >> 8) & 0b111u);
        }

        private int opyourthrow_rethrow(uint op)
        {
            return (int)((op >> 25) & 0b111u);
        }

        private uint _mkop_throw_helper(uint opcode, int player, int[] result, uint throw_type)
        {
            uint playerpart = (uint)player & 0b111u;
            uint resultpart =
                ((uint)result[0] & 0b111u) | ((uint)result[1] & 0b111u) << 3 | ((uint)result[2] & 0b111u) << 6;
            uint throwpart = throw_type & 0b1111u;
            // 8 + 3 + 3*3 + 4 = 24 bits
            return (opcode & 0xFFu) | playerpart << 8 | resultpart << 11 | throwpart << 20;
        }

        private uint mkop_throwresult(int player, int[] result, uint throw_type)
        {
            return _mkop_throw_helper(OPCODE_THROWRESULT, player, result, throw_type);
        }

        private uint mkop_oyathrowresult(int player, int[] result, uint throw_type)
        {
            return _mkop_throw_helper(OPCODE_OYATHROWRESULT, player, result, throw_type);
        }

        private int opthrow_player(uint op)
        {
            return (int)((op >> 8) & 0b111u);
        }

        private int[] opthrow_result(uint op)
        {
            int[] result = new int[3];
            uint resultpart = (op >> 11);
            result[0] = (int)(resultpart & 0b111u);
            result[1] = (int)((resultpart >> 3) & 0b111u);
            result[2] = (int)((resultpart >> 6) & 0b111u);
            return result;
        }

        private uint opthrow_type(uint op)
        {
            uint throwpart = (op >> 20) & 0b1111u;
            return throwpart;
        }

        private uint mkop_balance(int player, float amount)
        {
            uint playerpart = (uint)player & 0b111u;
            uint signpart = (amount < 0.0f) ? 1u : 0u;
            uint amountpart = (uint)(Mathf.Abs(amount)) & 0xFFFFu;
            return OPCODE_BALANCE | playerpart << 8 | signpart << 15 | amountpart << 16;
        }

        private int opbalance_player(uint op)
        {
            return (int)((op >> 8) & 0b111u);
        }

        private float opbalance_amount(uint op)
        {
            uint signpart = (op >> 15) & 0b1u;
            uint amountpart = (op >> 16) & 0xFFFFu;
            float sign = (signpart == 0) ? 1.0f : -1.0f;
            return sign * (float)amountpart;
        }

        private uint _mk_playerActivePart(bool [] playerActive)
        {
            uint playerActivePart = 0;
            // This is MAX_PLAYERS bits long
            for (int i = 0; i < MAX_PLAYERS; ++i) {
                playerActivePart |= ((playerActive[i] ? 1u : 0u) << i);
            }
            return playerActivePart;
        }

        private uint mkop_oyareport(int player, bool[] playerActive)
        {
            uint playerPart = (uint)player & 0b111u;
            uint playerActivePart = _mk_playerActivePart(playerActive);

            return OPCODE_OYAREPORT | playerPart << 8 | playerActivePart << 14;
        }

        private int opoyareport_oya(uint op)
        {
            return opoyachange_toplayer(op);
        }

        private bool[] opoyareport_playerActive(uint op)
        {
            return _get_playerActive(op, 14);
        }

        private uint mkop_oyachange(int fromPlayer, int toPlayer, bool[] playerActive)
        {
            uint toPlayerPart = (uint)toPlayer & 0b111u;
            uint fromPlayerPart = (uint)fromPlayer & 0b111u;
            uint playerActivePart = _mk_playerActivePart(playerActive);
            return OPCODE_OYACHANGE | toPlayerPart << 8 | fromPlayerPart << 11 | playerActivePart << 14;
        }

        private int opoyachange_toplayer(uint op) {
            return (int)((op >> 8) & 0b111u);
        }

        private int opoyachange_fromplayer(uint op) {
            return (int)((op >> 11) & 0b111u);
        }

        private bool[] _get_playerActive(uint op, int shift) {
            bool[] result = new bool[MAX_PLAYERS];
            uint playerActivePart = (op >> shift) & 0b1111u;
            for (int i = 0; i < MAX_PLAYERS; ++i) {
                if (((playerActivePart >> i) & 1u) == 1)
                    result[i] = true;
            }

            return result;
        }

        private bool[] opoyachange_playerActive(uint op)
        {
            return _get_playerActive(op, 14);
        }

        private uint mkop_nooya() {
            return OPCODE_NOOYA;
        }
        #endregion

        #region opqueue
        private bool serializing = false;
        private float serialization_timeout_time = float.NaN;
        private readonly float SERIALIZATION_TIMEOUT = 1.0f;

        private readonly int OPQUEUE_LENGTH = 32; // This should be more than large enough that normal play won't see lost commands
        private uint[] outgoing_ops;
        private int outgoing_ops_pending = 0;
        private int outgoing_ops_insert = 0; // Insert head
        private int outgoing_ops_read = 0; // Read head

        private bool opqueue_Pending()
        {
            return outgoing_ops_pending > 0;
        }

        private void opqueue_Reset()
        {
            GameLogDebug("opqueue_Reset");

            outgoing_ops = new uint[OPQUEUE_LENGTH];
            outgoing_ops_pending = 0;
            outgoing_ops_insert = 0;
            outgoing_ops_read = 0;

            serializing = false;
            serialization_timeout_time = float.NaN;
        }

        private void opqueue_Queue(uint op)
        {
            GameLogDebug(string.Format("opqueue_Queue(0x{0:X})", op));

            outgoing_ops[outgoing_ops_insert] = op;
            outgoing_ops_insert = (outgoing_ops_insert + 1) % OPQUEUE_LENGTH;
            ++outgoing_ops_pending;
        }

        private uint opqueue_Peek()
        {
            if (outgoing_ops_pending <= 0) {
                Debug.LogError("opqueue_Peek called while no ops pending");
                GameLogError("opqueue_Peek called while no ops pending");
                return 0;
            }

            return outgoing_ops[outgoing_ops_read];
        }

        private uint opqueue_Dequeue()
        {
            if (outgoing_ops_pending <= 0) {
                Debug.LogError("opqueue_Dequeue called while no ops pending");
                GameLogError("opqueue_Dequeue called while no ops pending");
                return 0;
            }

            uint result;
            result = outgoing_ops[outgoing_ops_read];
            outgoing_ops_read = (outgoing_ops_read + 1) % OPQUEUE_LENGTH;
            --outgoing_ops_pending;

            return result;
        }

        // TODO: consider just having Broadcast cause a SendCustomEventDelayedSeconds-based thread
        //       do these as some sort of optimization?
        private void Update()
        {
            if (!isOwner())
                return;

            if (opqueue_Pending() && !serializing) {
                arg0 = opqueue_Peek();
                GameLogDebug(string.Format("Serializing arg0=0x{0:X} ...", arg0));
                serializing = true;

                // Prepare the timeout function. Note that we still need to use a timestamp, since
                // there is no way to cancel an event pending with SendCustomEventDelayedSeconds.
                serialization_timeout_time = Time.time + SERIALIZATION_TIMEOUT;
                SendCustomEventDelayedSeconds(nameof(_SerializationTimeout), SERIALIZATION_TIMEOUT + 0.1f);

                RequestSerialization();
                OnDeserialization();
            }
        }

        public override void OnPostSerialization(VRC.Udon.Common.SerializationResult result)
        {
            GameLogDebug(string.Format("OnPostSerialization, serializing={0}, success={1}, byteCount={2}",
                                       serializing, result.success, result.byteCount));

            if (serializing && result.success) {
                opqueue_Dequeue();
            }

            serializing = false;
            serialization_timeout_time = float.NaN;
        }

        public void _SerializationTimeout()
        {
            // GameLogDebug(string.Format("_SerializationTimeout, Time.time={0}, serializing={1}, serialization_timeout_time={2}",
            //                            Time.time, serializing, serialization_timeout_time));

            if (serializing && Time.time > serialization_timeout_time) {
                GameLogWarn(string.Format("Serializing 0x{0:X} timed out...", opqueue_Peek()));
                opqueue_Dequeue();
                serializing = false;
                serialization_timeout_time = float.NaN;
            }
        }
        #endregion
    }
}
