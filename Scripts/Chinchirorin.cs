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
// update buttons

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
        private Die[] dice;  // Simply set to dieGrabSphere.dice in Start

        [SerializeField]
        private Collider insideBowlCollider;

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
        private GameLog gameLog;

        private UdonChips udonChips = null;

        private readonly bool DEBUG = true;

        private bool langJp = false;


        // Max bet in part makes sure we do not go above 2^16 (even when tripled), as we serialize bets as 16 bit uint
        //   2*20000 = 60000 < 2^16 = 65536
        private readonly float MAXBET = 20000.0f;
        private readonly int MAX_PLAYERS = 4;
        private readonly int MAX_RETHROWS = 3;

        // Client variables (also used on server)
        private int iAmPlayer = -1;
        private int oya = -1;
        private bool[] dieReadResult;

        private bool[] playerActive; // Used frequently by both, but client side
                                     // is just a cached version of server
                                     // (TODO: consider syncing this)

        // Server variables (only used on server)
        private float[] bets; // Used only by owner
        private bool[] betDone; // Used only by owner
        private int[] betMultiplier;
        private int oyaPayoutMultiplier;

        private int[] oyaResult; // Use only by owner
        private uint oyaThrowType; // Use only by owner
        private int[] recvResult; // Used only by owner
        int recvResult_cntr = 0; // Used only by owner
        private int rethrowCount = 0; // Used only by owner
        private int currentPlayer = -1; //


        private int state = -1; // Used only by owner (drives the oya statemachine)


        // These variables are used when the oya sends messages to other players.
        // E.g. when to change udonchips balances;
        [UdonSynced] private uint arg0;
        //[UdonSynced] private uint arg1;

        private void GameLog(string message)
        {
            if (gameLog) {
                gameLog.Log(message);
            }
        }

        private void GameLogDebug(string message)
        {
            if (DEBUG)
                GameLog("<color=\"grey\">" + message + "</color>");
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

        private int _test_x = 0;
        public void _Test()
        {
            GameLogDebug("test " + _test_x.ToString());
            _test_x++;
        }

        private void Start()
        {
            if (dieGrabSphere.dice.Length != 3)
                Debug.LogError("Must be three dice");

            // Get the dice from dieGrabSphere
            dice = new Die[dieGrabSphere.dice.Length];
            for (int i = 0; i < dieGrabSphere.dice.Length; ++i) {
                dice[i] = (Die)dieGrabSphere.dice[i].GetComponent(typeof(UdonBehaviour));
            }
            dieGrabSphere.hideOnThrow = true; // Ensure hideOnThrow is set

            if (joinButtons.Length != MAX_PLAYERS)
                Debug.LogError(string.Format("joinButtons must be {0} long", MAX_PLAYERS));

            if (betScreens.Length != MAX_PLAYERS)
                Debug.LogError(string.Format("betScreens must be {0} long", MAX_PLAYERS));

            if (diceSpawns.Length != MAX_PLAYERS)
                Debug.LogError(string.Format("diceSpawns must be {0} long", MAX_PLAYERS));


            foreach (Die die in dice) {
                die.AddListener(this);
            }

            udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();

            // TODO: removeme (ttest code)
            SendCustomEventDelayedSeconds(nameof(_Test), 1.0f);
            SendCustomEventDelayedSeconds(nameof(_Test), 2.0f);
            SendCustomEventDelayedSeconds(nameof(_Test), 3.0f);
            SendCustomEventDelayedSeconds(nameof(_Test), 4.0f);

            ResetClientVariables();
            ResetServerVariables();
            ResetTable();

            // When entering instance, show join buttons if table is currently inactive
            if (op_getop() == OPCODE_NOOYA)
            {
                UpdateJoinButtons(playerActive);
            }
        }

        private void ResetClientVariables()
        {
            dieReadResult = new bool[dice.Length];
            // Below is left out on purpose
            // oya = -1;
        }

        private void ResetServerVariables()
        {
            recvResult = new int[dice.Length];
            recvResult_cntr = 0;
            oyaResult = new int[dice.Length];
            oyaThrowType = THROW_INVALID;
            rethrowCount = 0;
            currentPlayer = -1;
            playerActive = new bool[MAX_PLAYERS];
            bets = new float[MAX_PLAYERS];
            betDone = new bool[MAX_PLAYERS];
            betMultiplier = new int[MAX_PLAYERS];
            oyaPayoutMultiplier = 0;
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
        private void Broadcast()
        {
            RequestSerialization();
            if (isOwner()) {
                OnDeserialization();
            }
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

        private void SendPlayerJoinEventDelayedSeconds(int player, float seconds)
        {
            string fnname = "EventPlayerJoin" + player.ToString();
            SendCustomEventDelayedSeconds(fnname, seconds);
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

        private void JoinGame(int player)
        {
            iAmPlayer = player;
            Networking.LocalPlayer.SetPlayerTag("iAmPlayer", iAmPlayer.ToString());

            // First person joining when table is empty is oya
            if (op_getop() == OPCODE_NOOYA) {
                GameLogDebug(string.Format("First person joining the table (arg0={0:X})", arg0));
                if (!Networking.IsOwner(gameObject))
                    Networking.SetOwner(Networking.LocalPlayer, gameObject);

                for (int i = 0; i < MAX_PLAYERS; ++i) {
                    playerActive[i] = false;
                }
                playerActive[iAmPlayer - 1] = true;
                // TODO: we should probably do the equivalent of this inline here instead
                mkop_oyachange(0, iAmPlayer, playerActive);
                Broadcast();

                // We do not need to do the playerjoin event in this special
                // case. The oyachange thing takes its place.
                
                /*
                // Need to wait a bit to make sure oyachange event gets around
                // TODO: should really wait on OnPostSerialization
                SendPlayerJoinEventDelayedSeconds(iAmPlayer, 1.0f);
                */
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
            if (iAmPlayer > 0) {
                // Already joined means we leave
                LeaveGame(iAmPlayer);
            } else {
                JoinGame(player);
            }

            // Disable all buttons to prevent double-click events (some buttons
            // might be re-enabled once we get ACKed)
            for (int i = 0; i < MAX_PLAYERS; ++i) {
                joinButtons[i].SetActive(false);
            }
        }

        public void _BtnJoinPlayer1() { JoinPlayerBtn(1); }
        public void _BtnJoinPlayer2() { JoinPlayerBtn(2); }
        public void _BtnJoinPlayer3() { JoinPlayerBtn(3); }
        public void _BtnJoinPlayer4() { JoinPlayerBtn(4); }

        /*
        public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner)
        {

            if (requester.playerId == newOwner.playerId) {
                // Somebody joining attempting to grab ownership for themselves, in case there is no active owner
                if (isOya()) {
                    // Owner branch
                    if (iAmPlayer <= 0) {
                        // Owner isn't active in the game, it's fine
                        return true;
                    }
                } else {
                    //
                }
            } else {

            }
        }
        */

        private void SendBetEvent(int player, int bet)
        {
            string fnname = string.Format("EventPlayer{0}Bet{1}", player, bet);
            SendToOya(fnname);
        }

        private void SendBetUndoEvent(int player)
        {
            string fnname = string.Format("EventPlayer{0}BetUndo", player);
            SendToOya(fnname);
        }

        private void SendBetDoneEvent(int player)
        {
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
            return iAmPlayer == oya;
        }

        private bool isOwner()
        {
            // The oya owns the gameobject, and acts as "master" of the game
            return Networking.IsOwner(gameObject);
        }

        // DiceListener
        /*
        public void SetThrown()
        {
            for (int i = 0; i < dieReadResult.Length; ++i) {
                dieReadResult[i] = false;
            }

            if (isOwner()) {
                for (int i = 0; i < recvResult.Length; ++i)
                    recvResult[i] = -1;
                recvResult_cntr = 0;
                recvDieOutside = false;
            } else {
                // FIXME: Actually this might not be neccessary, since the oya should know that a dice throw is about to happen
                // If we aren't the oya, we must inform the oya that we just threw, so the oya can prepare to receive the result events.
                // TODO: make sure there are no races... alternatively switch to a method where all 3 dice are sent at once (then using events won't be realistic)
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(SetThrown));
            }
        }
        */

        public void SetThrown()
        {
            // Do nothing
        }

        // DiceListener
        public void SetHeld()
        {
            // Do nothing
        }

        // DiceListener
        public void DiceResult()
        {
            GameLogDebug("DiceResult");

            // Actually I think this would all probably be easier if we just handled the dice directly instead of being a listener of it...
            for (int i = 0; i < dice.Length; ++i) {
                Die die = dice[i];
                if (!dieReadResult[i] && die.GetResult() != -1) {
                    dieReadResult[i] = true;
                    int result = die.GetResult();
                    if (!insideBowlCollider.bounds.Contains(die.transform.position)) { // TODO: Or should we use the rigidbody position?
                        result = 0; // 0 is used to indicate outside
                    }
                    SendDiceResultEvent(result);
                }
            }
        }

        private void SendDiceResultEvent(int result)
        {
            switch (result) {
            case 0:
            case 1:
            case 2:
            case 3:
            case 4:
            case 5:
            case 6:
                // Sends a single dice result to Oya (owner)
                string fnname = "EventDiceResult" + result.ToString();
                SendToOya(fnname);
                break;
            default:
                Debug.LogError("Invalid dice result: " + result.ToString());
                GameLogError("Invalid dice result: " + result.ToString());
                break;
            }
        }

        private string formatChips(float amount)
        {
            return string.Format(udonChips.format, amount);
        }

        // EventDiceResultX
        public void EventDiceResult0() { RecvEventDiceResult(0); }
        public void EventDiceResult1() { RecvEventDiceResult(1); }
        public void EventDiceResult2() { RecvEventDiceResult(2); }
        public void EventDiceResult3() { RecvEventDiceResult(3); }
        public void EventDiceResult4() { RecvEventDiceResult(4); }
        public void EventDiceResult5() { RecvEventDiceResult(5); }
        public void EventDiceResult6() { RecvEventDiceResult(6); }

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

        // TODO: need to verify that this actually runs on the leaving player.
        //       if it doesn't, we might need another synced var :/
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            string tag = player.GetPlayerTag("iAmPlayer") ?? "-1";
            if (int.Parse(tag) == iAmPlayer) {
                LeaveGame(iAmPlayer);
            }
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

        public override void OnDeserialization()
        {
            if (op_getop() == OPCODE_RESET_TABLE) {
                GameLogDebug("Got reset table");
                ResetTable();
            } else if (op_getop() == OPCODE_OYAREPORT) {
                int player = opoyareport_oya();
                
                oya = player;
                bool[] pa = opoyareport_playerActive();
                GameLogDebug(string.Format("P{0} is oya (playerActive: {1},{2},{3},{4})",
                                           oya, pa[0], pa[1], pa[2], pa[3]));

                // if (iAmPlayer <= 0)
                //     UpdateJoinButtons(pa);
                UpdateJoinButtons(pa);
            } else if (op_getop() == OPCODE_ENABLE_BET) {
                if (!isOya() && iAmPlayer > 0 && iAmPlayer <= MAX_PLAYERS) {
                    // For now we only enable the bet screen locally. In the
                    // future maybe we should enable them globally as a way to
                    // indicate to everyone that someone is betting.

                    // That would require a method to only enable interacting
                    // with it for the player that owns it, however. (loop
                    // through all button components in children or something?)
                    GameObject bs = betScreens[iAmPlayer - 1];
                    bs.SetActive(true);
                }
                
                // Disallow anyone from leaving/joining
                foreach (GameObject btn in joinButtons)
                    btn.SetActive(false);

                // if (iAmPlayer > 0 && iAmPlayer <= MAX_PLAYERS) {
                //     joinButtons[iAmPlayer - 1].SetActive(false);
                // }
            } else if (op_getop() == OPCODE_BET) {
                int player = opbet_getplayer();
                float total = opbet_gettotal();
                GameLogDebug(string.Format("P{0} increased their bet to {1}", player, formatChips(total)));
                // Here we could display chips or something coming up for each press
                SetBetLabel(player, total);
            } else if (op_getop() == OPCODE_BETUNDO) {
                int player = opbet_getplayer();
                GameLogDebug(string.Format("P{0} reset their bet", player));
                // Here we would reset any chips displayed or so
                SetBetLabel(player, 0.0f);
            } else if (op_getop() == OPCODE_BETDONE) {
                int player = opbet_getplayer();
                float total = opbet_gettotal();
                betScreens[player - 1].SetActive(false);
                GameLog(string.Format("P{0} bet {1}", player, formatChips(total)));
                SetBetLabel(player, total);
            } else if (op_getop() == OPCODE_PLAYERJOIN) {
                int player = opplayer_player();
                oya = opplayerjoin_oya(); // Syncs up this variable in case we don't have it
                bool[] pa = opplayer_playerActive();
                GameLog(string.Format("P{0} entered the game (playerActive: {1},{2},{3},{4})",
                                      player, pa[0], pa[1], pa[2], pa[3]));

                /*
                if (iAmPlayer == player) {
                    // Turn the button into a leave button, and disable other join buttons
                    GameObject btn = joinButtons[player-1];
                    SetButtonText(btn, "Leave");
                    for (int i = 0; i < MAX_PLAYERS; ++i) {
                        if (i + 1 != iAmPlayer)
                            joinButtons[i].SetActive(false);
                    }
                } else {
                    // Disable buttons based on playerActive elsewhere
                    UpdateJoinButtons(pa);
                }
                */
                UpdateJoinButtons(pa);
            } else if (op_getop() == OPCODE_PLAYERLEAVE) {
                int player = opplayer_player();
                bool[] pa = opplayer_playerActive();
                GameLog(string.Format("P{0} left the game (playerActive: {1},{2},{3},{4})",
                                      player, pa[0], pa[1], pa[2], pa[3]));

                /*
                if (iAmPlayer == player) {
                    GameObject btn = joinButtons[player-1];
                    btn.SetActive(true);
                    SetButtonText(btn, "Join");
                } else if (iAmPlayer <= 0) {
                    // Update buttons for non-players
                    UpdateJoinButtons(pa);
                }
                */
                UpdateJoinButtons(pa);
            } else if (op_getop() == OPCODE_YOURTHROW) {
                int player = opyourthrow_player();
                int rethrow = opyourthrow_rethrow();

                for (int i = 0; i < dieReadResult.Length; ++i)
                    dieReadResult[i] = false;

                if (iAmPlayer == player) {
                    dieGrabSphere._BecomeOwner();
                    dieGrabSphere._TeleportTo(diceSpawns[player - 1]);
                    dieGrabSphere._ParkDice();
                    // Make it pickupable only for the player whose turn it is
                    dieGrabSphere._SetPickupable(true);
                }

                dieGrabSphere._Show();

                if (rethrow > 0) {
                    GameLog(string.Format("P{0} rethrow ({1}/3)", player, rethrow + 1));
                } else {
                    GameLog(string.Format("P{0} it is your turn", player));
                }
                // TODO: perhaps a sound effect to draw the users attention? (it
                // should be fine just to do it inline here)
            } else if (op_getop() == OPCODE_THROWRESULT) {
                int player = opthrow_player();
                int[] result = opthrow_result();
                uint throw_type = opthrow_type();
                if (throw_type == THROW_SHONBEN)
                    GameLog(string.Format("P{0} threw outside", player));
                else
                    GameLog(string.Format("P{0} threw {1} {2} {3}: {4}",
                                          player, result[0], result[1], result[2],
                                          formatThrowType(throw_type)));
                // TODO: specific display for the result
            } else if (op_getop() == OPCODE_OYATHROWRESULT) {
                int player = opthrow_player();
                int[] result = opthrow_result();
                uint throw_type = opthrow_type();
                if (throw_type == THROW_SHONBEN)
                    GameLog(string.Format("P{0} (oya) threw outside", player));
                else
                    GameLog(string.Format("P{0} (oya) threw {1} {2} {3}: {4}",
                                          player, result[0], result[1], result[2],
                                          formatThrowType(throw_type)));
                // TODO: specific display for the result
            } else if (op_getop() == OPCODE_BALANCE) {
                int player = opbalance_player();
                float amount = opbalance_amount();
                if (iAmPlayer > 0 && player == iAmPlayer)
                    udonChips.money += amount;  // TODO: handle no negative

                if (amount >= 0.0f)
                    GameLog(string.Format("P{0} won {1}", player, formatChips(Mathf.Abs(amount))));
                else
                    GameLog(string.Format("P{0} lost {1}", player, formatChips(Mathf.Abs(amount))));
            } else if (op_getop() == OPCODE_OYACHANGE) {
                int toPlayer = opoyachange_toplayer();
                int fromPlayer = opoyachange_fromplayer();
                bool[] pa = opoyachange_playerActive();
                oya = toPlayer;

                // TODO: display clearly who is oya (change playerlabel?)
                if (fromPlayer <= 0)
                    GameLog(string.Format("P{0} became oya (playerActive: {1},{2},{3},{4})",
                                          toPlayer, pa[0], pa[1], pa[2], pa[3]));
                else
                    GameLog(string.Format("Oya P{0} -> P{1} (playerActive: {2},{3},{4},{5})",
                                          fromPlayer, toPlayer, pa[0], pa[1], pa[2], pa[3]));
                UpdateJoinButtons(pa);

                if (iAmPlayer > 0 && toPlayer == iAmPlayer) {
                    // Become owner/oya
                    if (!Networking.IsOwner(gameObject))
                        Networking.SetOwner(Networking.LocalPlayer, gameObject);

                    ResetServerVariables();
                    // Set playerActive based on what previous oya told us
                    playerActive = pa;

                    // Start the oya state machine here
                    state = STATE_FIRST;
                    // TODO: should use OnPostSerialization ?
                    SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
                }
            } else if (op_getop() == OPCODE_NOOYA) {
                // TODO: reset various things like text displays, betting screens etc.
                GameLogDebug("Table is empty");
                ResetTable();
                ResetServerVariables();
                ResetClientVariables();
                oya = -1;
                iAmPlayer = -1;
                //UpdateJoinButtons(playerActive);
                foreach (GameObject btn in joinButtons) {
                    btn.SetActive(true);
                    SetButtonText(btn, "Join");
                }
            }
        }

        private void ResetTable()
        {
            for (int i = 0; i < MAX_PLAYERS; ++i) {
                betScreens[i].SetActive(false);
                startRoundButtons[i].SetActive(false);

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
        private readonly int STATE_RESET_TABLE = 1;
        private readonly int STATE_OYAREPORT = 2;
        private readonly int STATE_WAITINGFORPLAYERS = 5;
        private readonly int STATE_WAITINGFORROUNDSTART = 6;
        private readonly int STATE_PREPAREBETS = 7;
        private readonly int STATE_WAITINGFORBETS = 8;
        private readonly int STATE_PREPARE_OYATHROW = 10;
        private readonly int STATE_OYATHROW = 11;
        private readonly int STATE_PREPARE_P1THROW = 12; // TODO: perhaps we could condense all the player throws into one state plus an active player var
        private readonly int STATE_P1THROW = 13;
        private readonly int STATE_PREPARE_P2THROW = 14;
        private readonly int STATE_P2THROW = 15;
        private readonly int STATE_PREPARE_P3THROW = 16;
        private readonly int STATE_P3THROW = 17;
        private readonly int STATE_PREPARE_P4THROW = 18;
        private readonly int STATE_P4THROW = 19;
        private readonly int STATE_PREPARE_BALANCE = 30;
        private readonly int STATE_BALANCE = 31; // "Normal" round with individual results for everybody
        private readonly int STATE_PREPARE_OYAPAYOUT = 40;
        private readonly int STATE_OYAPAYOUT = 41; // Oya failed and must pay everybody, followed by switching

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
                    mkop_oyachange(iAmPlayer, (idx % MAX_PLAYERS) + 1, playerActive);
                    Broadcast();
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
                if (state == STATE_RESET_TABLE) {
                    GameLogDebug("state = STATE_RESET_TABLE");
                    mkop_reset_table(); // TODO: merge this and oyareport
                    Broadcast();

                    // TODO: wait with OnPostSerialization instead
                    state = STATE_OYAREPORT;
                    SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
                    return;
                } else if (state == STATE_OYAREPORT) {
                    GameLogDebug("state = STATE_OYAREPORT");
                    mkop_oyareport(iAmPlayer, playerActive);
                    Broadcast();

                    // TODO: wait with OnPostSerialization instead
                    state = STATE_WAITINGFORPLAYERS;
                    SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
                    return;
                } else if (state == STATE_WAITINGFORPLAYERS) {
                    GameLogDebug("state = STATE_WAITINGFORPLAYERS");

                    if (getActivePlayerCount() >= 2) {
                        state = STATE_WAITINGFORROUNDSTART;
                        continue;
                    }
                    // TODO: here a way to indicate to everyone (or at least oya) that we need more players
                    return; // Wait for players to increase
                } else if (state == STATE_WAITINGFORROUNDSTART) {
                    GameLogDebug("state = STATE_WAITINGFORROUNDSTART");

                    startRoundButtons[oya - 1].SetActive(true);
                    return; // Wait for button press + playerjoin events during STATE_WAITINGFORPLAYERS percolate through
                } else if (state == STATE_PREPAREBETS) {
                    GameLogDebug("state = STATE_PREPAREBETS");

                    for (int i = 0; i < MAX_PLAYERS; ++i) {
                        bets[i] = 0.0f;
                        betDone[i] = false;
                        betMultiplier[i] = 0;
                    }
                    betDone[oya - 1] = true; // Oya doesn't bet, so they're "done"
                    oyaPayoutMultiplier = 0;

                    mkop_enable_bet(); // TODO: perhaps this could be an event
                    Broadcast();

                    // TODO: should probably factor in OnPostSerialization or something
                    state = STATE_WAITINGFORBETS;
                    SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
                    return;
                } else if (state == STATE_WAITINGFORBETS) {
                    GameLogDebug("state = STATE_WAITINGFORBETS");

                    bool allbetsin = true;
                    for (int i = 0; i < MAX_PLAYERS; ++i)
                        if (playerActive[i] && !betDone[i])
                            allbetsin = false;

                    if (allbetsin) {
                        GameLogDebug(string.Format("bets=[{0},{1},{2},{3}]", bets[0], bets[1], bets[2], bets[3]));
                        state = STATE_PREPARE_OYATHROW;
                        continue;
                    } else {
                        return; // Wait for bets
                    }
                } else if (state == STATE_PREPARE_OYATHROW) {
                    GameLogDebug("state = STATE_PREPARE_OYATHROW");

                    rethrowCount = 0;
                    oyaPayoutMultiplier = 0;

                    state = STATE_OYATHROW;
                    continue;
                } else if (state == STATE_OYATHROW) {
                    GameLogDebug("state = STATE_OYATHROW");

                    if (rethrowCount < MAX_RETHROWS) {
                        PrepareRecvThrow();
                        mkop_yourthrow(oya, rethrowCount);
                        Broadcast();
                        return; // Wait on throw result
                    } else {
                        oyaPayoutMultiplier = 1;
                        state = STATE_PREPARE_OYAPAYOUT;
                        continue;
                    }
                } else if (state == STATE_PREPARE_P1THROW) {
                    GameLogDebug("state = STATE_PREPARE_P1THROW");

                    rethrowCount = 0;
                    betMultiplier[0] = 0;

                    state = STATE_P1THROW;
                    continue;
                } else if (state == STATE_P1THROW) {
                    GameLogDebug("state = STATE_P1THROW");

                    // Skip if oya, or not active
                    if (oya == 1 || !playerActive[0]) {
                        state = STATE_PREPARE_P2THROW;
                        continue;
                    }

                    if (rethrowCount < MAX_RETHROWS) {
                        PrepareRecvThrow();
                        mkop_yourthrow(1, rethrowCount);
                        Broadcast();
                        return; // Wait on throw result
                    } else {
                        betMultiplier[0] = -1;
                        // TODO: event/opcode to inform everyone about the fail? Maybe a sound effect? Throwresult?
                        state = STATE_PREPARE_P2THROW;
                        continue;
                    }
                } else if (state == STATE_PREPARE_P2THROW) {
                    GameLogDebug("state = STATE_PREPARE_P2THROW");

                    rethrowCount = 0;
                    betMultiplier[1] = 0;

                    state = STATE_P2THROW;
                    continue;
                } else if (state == STATE_P2THROW) {
                    GameLogDebug("state = STATE_P2THROW");

                    if (oya == 2 || !playerActive[1]) {
                        state = STATE_PREPARE_P3THROW;
                        continue;
                    }

                    if (rethrowCount < 3) {
                        PrepareRecvThrow();
                        mkop_yourthrow(2, rethrowCount);
                        Broadcast();
                        return; // Wait on throw result
                    } else {
                        betMultiplier[1] = -1;
                        // TODO: event/opcode to inform everyone about the fail? Maybe a sound effect?
                        state = STATE_PREPARE_P3THROW;
                        continue;
                    }
                } else if (state == STATE_PREPARE_P3THROW) {
                    GameLogDebug("state = STATE_PREPARE_P3THROW");

                    rethrowCount = 0;
                    betMultiplier[2] = 0;

                    state = STATE_P3THROW;
                    continue;
                } else if (state == STATE_P3THROW) {
                    GameLogDebug("state = STATE_P3THROW");

                    if (oya == 3 || !playerActive[2]) {
                        state = STATE_PREPARE_P4THROW;
                        continue;
                    }

                    if (rethrowCount < 3) {
                        PrepareRecvThrow();
                        mkop_yourthrow(3, rethrowCount);
                        Broadcast();
                        return; // Wait on throw result
                    } else {
                        betMultiplier[2] = -1;
                        // TODO: event/opcode to inform everyone about the fail? Maybe a sound effect?
                        state = STATE_PREPARE_P4THROW;
                        continue;
                    }
                } else if (state == STATE_PREPARE_P4THROW) {
                    GameLogDebug("state = STATE_PREPARE_P4THROW");

                    rethrowCount = 0;
                    betMultiplier[3] = 0;

                    state = STATE_P4THROW;
                    continue;
                } else if (state == STATE_P4THROW) {
                    GameLogDebug("state = STATE_P4THROW");

                    if (oya == 4 || !playerActive[3]) {
                        state = STATE_PREPARE_BALANCE;
                        continue;
                    }
                    if (rethrowCount < 3) {
                        PrepareRecvThrow();
                        mkop_yourthrow(4, rethrowCount);
                        Broadcast();
                        return; // Wait on throw result
                    } else {
                        betMultiplier[3] = -1;
                        // TODO: event/opcode to inform everyone about the fail? Maybe a sound effect?
                        state = STATE_PREPARE_BALANCE;
                        continue;
                    }
                } else if (state == STATE_PREPARE_BALANCE) {
                    GameLogDebug("state = STATE_PREPARE_BALANCE");
                    currentPlayer = 1;
                    state = STATE_BALANCE;
                    continue;
                } else if (state == STATE_BALANCE) {
                    GameLogDebug(string.Format("state = STATE_BALANCE, currentPlayer={0}", currentPlayer));

                    int i = currentPlayer - 1;
                    if (i < MAX_PLAYERS) {
                        if (i + 1 != oya && playerActive[i]) {
                            float amount = bets[i]*betMultiplier[i];
                            GameLogDebug(string.Format("amount={1}, bets[{0}]={2}, betMultiplier[{0}]={3}",
                                                       i, amount, bets[i], betMultiplier[i]));
                            udonChips.money -= amount;
                            mkop_balance(i+1, amount);
                            Broadcast();

                            ++currentPlayer;
                            // TODO: OnPostSerialization ?
                            SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
                            return;
                        } else {
                            ++currentPlayer;
                            continue;
                        }
                    } else {
                        // Round is done. Start over from the top
                        state = STATE_FIRST;
                        SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 3);
                        return;
                    }
                } else if (state == STATE_PREPARE_OYAPAYOUT) {
                    GameLogDebug("state = STATE_PREPARE_OYAPAYOUT");
                    currentPlayer = 1;
                    state = STATE_OYAPAYOUT;
                    continue;
                } else if (state == STATE_OYAPAYOUT) {
                    GameLogDebug(string.Format("state = STATE_OYAPAYOUT, currentPlayer={0}", currentPlayer));

                    int i = currentPlayer - 1;
                    if (i < MAX_PLAYERS) {
                        // payout based on oyaPayoutMultiplier here
                        if (i + 1 != oya && playerActive[i]) {
                            float amount = oyaPayoutMultiplier*bets[i];
                            udonChips.money -= amount; // Remove from ourselves
                            mkop_balance(i+1, amount); // Increase on remote player
                            Broadcast();

                            ++currentPlayer;
                            // TODO: OnPostSerialization ?
                            SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
                            return;
                        } else {
                            ++currentPlayer;
                            continue;
                        }
                    } else {
                        // We failed. All gets reset and oya gets passed on
                        // TODO: event/opcode to inform everyone about this?

                        SendCustomEventDelayedSeconds(nameof(_ToNextOya), 3);
                        return; // We are no longer oya, so we no longer run the state machine
                    }
                }
/* 
                } else if (state == STATE_BALANCE) {
                    GameLogDebug(string.Format("state = STATE_BALANCE", currentPlayer));
                    for (int i = 0; i < MAX_PLAYERS; ++i) {
                        if (i + 1 != oya && playerActive[i]) {
                            float amount = bets[i]*betMultiplier[i];
                            GameLogDebug(string.Format("amount={1}, bets[{0}]={2}, betMultiplier[{0}]={3}",
                                                       i, amount, bets[i], betMultiplier[i]));
                            udonChips.money -= amount;
                            mkop_balance(i+1, amount);
                            Broadcast();
                            // TODO: this state needs to be broken up into one
                            // for each player, alternatively if we can do it
                            // with one balance opcode
                        }
                    }
                    // Round is done. Start over from the top
                    state = STATE_FIRST;
                    SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 3);
                    return;
                } else if (state == STATE_OYAPAYOUT) {
                    GameLogDebug("state = STATE_OYAPAYOUT");

                    // payout based on oyaPayoutMultiplier here
                    for (int i = 0; i < MAX_PLAYERS; ++i) {
                        if (i + 1 != oya && playerActive[i]) {
                            float amount = oyaPayoutMultiplier*bets[i];
                            udonChips.money -= amount; // Remove from ourselves
                            mkop_balance(i+1, amount); // Increase on remote player
                            Broadcast();
                            // TODO: this state needs to be broken up into one for each player
                        }
                    }

                    // We failed. All gets reset and oya gets passed on
                    // TODO: event/opcode to inform everyone about this?

                    SendCustomEventDelayedSeconds(nameof(_ToNextOya), 3);
                    return; // We are no longer oya, so we no longer run the state machine
                }
*/
            }
        }

        // TODO: perhaps limit the states during which a player can join?
        private void RecvEventPlayerJoin(int player)
        {
            playerActive[player - 1] = true;
            mkop_playerjoin(player, oya, playerActive);
            Broadcast();

            // TODO: fire of opcodes that will update all state (bet labels etc.), in case the newly
            // joining player joined the instance late?

            if (state == STATE_WAITINGFORPLAYERS) {
                _OyaStateMachine();
            }
        }

        // TODO: perhaps it would make sense to switch to state machine function
        // that as arguments takes event and event arguments. Then we could
        // handle tricky things like this inline in OyaStateMachine
        private void RecvEventPlayerLeave(int player)
        {
            GameLogDebug(string.Format("RecvEventPlayerLeave({0})", player));

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
                // TODO: opcode/event that informs everyone that the oya is cowardly fleeing?

                // We try to transfer to next player (we can't transfer to the
                // leaving player by virtue of having removed them from playerActive)
                bool found = _ToNextOya();
                // If we didn't find another player to transfer to we are
                // obviously the last person leaving, and the game should be
                // reset so that the first person to join becomes owner
                if (!found) {
                    mkop_nooya();
                    Broadcast();
                }
            } else {
                mkop_playerleave(player, playerActive);
                Broadcast();

                // Need to wait on serialization of the playerleave (maybe superfluous)
                SendCustomEventDelayedSeconds("_RecvEventPlayer" + player.ToString() + "Leave_Continuation", 0.5f);
            }
        }

        public void _RecvEventPlayer1Leave_Continuation() { _RecvEventPlayerLeave_Continuation(1); }
        public void _RecvEventPlayer2Leave_Continuation() { _RecvEventPlayerLeave_Continuation(2); }
        public void _RecvEventPlayer3Leave_Continuation() { _RecvEventPlayerLeave_Continuation(3); }
        public void _RecvEventPlayer4Leave_Continuation() { _RecvEventPlayerLeave_Continuation(4); }

        private void _RecvEventPlayerLeave_Continuation(int player)
        {
            GameLogDebug(string.Format("_RecvEventPlayerLeave_Continuation({0})", player));

            if (getActivePlayerCount() < 2) {
                // Too few to play. We have to go back to STATE_FIRST
                state = STATE_FIRST;
                SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
            } else {
                // Handle leaving during certain states etc.
                if (state == STATE_P1THROW) {
                    state = STATE_PREPARE_P2THROW;
                    SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
                } else if (state == STATE_P2THROW) {
                    state = STATE_PREPARE_P3THROW;
                    SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
                } else if (state == STATE_P3THROW) {
                    state = STATE_PREPARE_P4THROW;
                    SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
                } else if (state == STATE_P4THROW) {
                    state = STATE_PREPARE_BALANCE;
                    SendCustomEventDelayedSeconds(nameof(_OyaStateMachine), 0.5f);
                }
            }
        }

        // This button is only ever visible to the owner (oya) so there's no need for networking.
        public void _BtnStartRound()
        {
            if (state == STATE_WAITINGFORROUNDSTART) {
                startRoundButtons[oya - 1].SetActive(false);
                
                state = STATE_PREPAREBETS;
                _OyaStateMachine();
            }
        }

        private void RecvBetEvent(int player, float amount)
        {
            if (state == STATE_WAITINGFORBETS) {
                bets[player - 1] += amount;
                mkop_bet(player, bets[player - 1]);
                Broadcast();
            }
        }

        private void RecvBetUndoEvent(int player)
        {
            if (state == STATE_WAITINGFORBETS) {
                bets[player - 1] = 0.0f;
                mkop_betundo(player);
                Broadcast();
            }
        }

        private void RecvBetDoneEvent(int player)
        {
            GameLogDebug("RecvBetDoneEvent");

            if (state == STATE_WAITINGFORBETS) {
                betDone[player - 1] = true;
                mkop_betdone(player, bets[player - 1]);
                Broadcast();

                // Delay before continuing
                SendCustomEventDelayedSeconds(nameof(_RecvBetDone_Continuation), 1);
            }
        }

        public void _RecvBetDone_Continuation()
        {
            GameLogDebug("_RecvBetDone_Continuation");
            
            // We could theoretically end up with several calls into here, some
            // potentially arriving late, so we must check the state
            if (state == STATE_WAITINGFORBETS) {
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
            if (state == STATE_OYATHROW) {
                GameLogDebug("ProcessDiceResult, STATE_OYATHROW");
                player = oya;
                mkop_oyathrowresult(oya, recvResult, throw_type);
                // GameLogDebug(string.Format("oya={0}, recvResult=[{1},{2},{3}], recvDieOutside={4}, throw_type={5}",
                //                            oya, recvResult[0], recvResult[1], recvResult[2], recvDieOutside, throw_type));
                // GameLogDebug(string.Format("arg0={0:X}", arg0));
                for (int i = 0; i < oyaResult.Length; ++i) {
                    oyaResult[i] = recvResult[i];
                }
                oyaThrowType = throw_type;
                GameLogDebug(string.Format("Wrote oyaThrowType: {0}", oyaThrowType));
            } else {
                GameLogDebug("ProcessDiceResult, OTHER");
                if (state == STATE_P1THROW)
                    player = 1;
                else if (state == STATE_P2THROW)
                    player = 2;
                else if (state == STATE_P3THROW)
                    player = 3;
                else if (state == STATE_P4THROW)
                    player = 4;
                mkop_throwresult(player, recvResult, throw_type);
            }
            Broadcast();

            // Delay here before we advance the state machine (also allows the
            // opcode to get transferred)
            // TODO: using OnPostSerialization to verify it's really gotten serialized?

            _ProcessDiceResult_throw_type = throw_type; // Save some vars for the continuation
            _ProcessDiceResult_player = player;
            SendCustomEventDelayedSeconds(nameof(_ProcessDiceResult_Continuation), 1.0f);
        }

        public void _ProcessDiceResult_Continuation()
        {
            GameLogDebug("_ProcessDiceResult_Continuation");

            if (!(state == STATE_OYATHROW ||
                  state == STATE_P1THROW || state == STATE_P2THROW ||
                  state == STATE_P3THROW || state == STATE_P4THROW))
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
                    oyaPayoutMultiplier = 1;
                    state = STATE_PREPARE_OYAPAYOUT;
                    _OyaStateMachine();
                    return;
                }

                if (throw_type == THROW_1 || throw_type == THROW_123) {
                    // Insta-fail and payout to everybody (different multiplier)
                    oyaPayoutMultiplier = (throw_type == THROW_123) ? 2 : 1;
                    state = STATE_PREPARE_OYAPAYOUT;
                    _OyaStateMachine();
                    return;
                }

                // 6 points, 456 or Triple: Oya wins instantly (different multipliers)
                if (throw_type == THROW_6 || throw_type == THROW_456 || throw_type == THROW_ZOROME) {
                    int mult = (throw_type == THROW_456)    ? -2 :
                               (throw_type == THROW_ZOROME) ? -3 : -1;
                    for (int i = 0; i < MAX_PLAYERS; ++i) {
                        betMultiplier[i] = mult;
                    }
                    state = STATE_PREPARE_BALANCE;
                    _OyaStateMachine();
                    return;
                }

                // Is a result with between 2-5 points. Continue to STATE_PREPARE_P1THROW
                state = STATE_PREPARE_P1THROW;
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
                    betMultiplier[player-1] = 1;
                } else if (throw_type == THROW_456) {
                    betMultiplier[player-1] = 2;
                } else if (throw_type == THROW_ZOROME) {
                    betMultiplier[player-1] = 3;
                } else if (throw_type == THROW_123) {
                    betMultiplier[player-1] = -2;
                } else if (throw_type == THROW_1) {
                    betMultiplier[player-1] = -1;
                } else if (throw_type > oyaThrowType) {
                    betMultiplier[player-1] = 1;
                } else if (throw_type < oyaThrowType) {
                    betMultiplier[player-1] = -1;
                } else {
                    // wakare
                    betMultiplier[player-1] = 0;
                }

                // Step state machine (depends on current state)
                if (state == STATE_P1THROW)
                    state = STATE_PREPARE_P2THROW;
                else if (state == STATE_P2THROW)
                    state = STATE_PREPARE_P3THROW;
                else if (state == STATE_P3THROW)
                    state = STATE_PREPARE_P4THROW;
                else if (state == STATE_P4THROW)
                    state = STATE_PREPARE_BALANCE;
                _OyaStateMachine();
            }
        }

        private void RecvEventDiceResult(int result)
        {
            GameLogDebug("RecvEventDiceResult");
            
            if (state == STATE_OYATHROW ||
                state == STATE_P1THROW || state == STATE_P2THROW ||
                state == STATE_P3THROW || state == STATE_P4THROW) {
                if (result < 0 || result > 6) { // 0 is "valid" in that it represents being outside
                    Debug.LogError("Invalid dice result: " + result.ToString());
                    GameLogError("Invalid dice result: " + result.ToString());
                    return;
                }

                if (!(recvResult_cntr < dice.Length))
                    return;

                recvResult[recvResult_cntr++] = result;

                if (recvResult_cntr == dice.Length) {
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
        // Opcodes for all broadcasts from Oya to everyone else. sent in arg0 and (sometimes) arg1
        private readonly uint OPCODE_ENABLE_BET = 1; // Enable bet panels everywhere
        private readonly uint OPCODE_BET = 3;
        private readonly uint OPCODE_BETUNDO = 4;
        private readonly uint OPCODE_BETDONE = 5; // Display that a particular player is done betting
        private readonly uint OPCODE_RESET_TABLE = 6;
        private readonly uint OPCODE_PLAYERJOIN = 10; // A player has joined: Switch their join button to leave, and make it so only that player can press it
        private readonly uint OPCODE_PLAYERLEAVE = 11;
        private readonly uint OPCODE_YOURTHROW = 20; // This enables the dice for one particular player, but disables them (pickup disabled) for everyone else (if player nbr is 0 just disable)
        private readonly uint OPCODE_THROWRESULT = 21;
        private readonly uint OPCODE_OYATHROWRESULT = 22;
        private readonly uint OPCODE_BALANCE = 30;  // This applies the change to a particular players udonChips balance (Oya applies changes to own balance on their own)
        private readonly uint OPCODE_OYAREPORT = 0xF0u; // simply update the oya variable
        private readonly uint OPCODE_OYACHANGE = 0xF1u; // Requests that another player take over as oya
        private readonly uint OPCODE_NOOYA     = 0x00u; // Sent by the oya when it is the last person leaving, resetting the game. Also the value in arg0 on start
        private readonly uint OPCODE_NOOP      = 0xFFu;

        uint op_getop()
        {
            return arg0 & 0xFFu;
        }

        private void mkop_enable_bet()
        {
            arg0 = OPCODE_ENABLE_BET;
        }

        private void mkop_bet(int player, float total)
        {
            uint playerpart = (uint)player & 0b111u;   // Player numbers are three bit
            uint totalpart = (uint)total & 0xFFFFu;  // 16 bit
            arg0 = OPCODE_BET | playerpart << 8 | totalpart << 16;
        }

        private void mkop_betundo(int player)
        {
            uint playerpart = (uint)player & 0b111u;   // Player numbers are three bit
            arg0 = OPCODE_BETUNDO | playerpart << 8;
        }

        private void mkop_betdone(int player, float total)
        {
            uint playerpart = (uint)player & 0b111u;
            uint totalpart = (uint)total & 0xFFFFu;
            arg0 = OPCODE_BETDONE | playerpart << 8 | totalpart << 16;
        }

        private void mkop_reset_table()
        {
            arg0 = OPCODE_RESET_TABLE;
        }

        int opbet_getplayer()
        {
            return (int)((arg0 >> 8) & 0b111u);
        }

        float opbet_gettotal()
        {
            return (float)((arg0 >> 16) & 0xFFFFu);
        }

        private void mkop_playerjoin(int player, int oya, bool[] playerActive)
        {
            uint playerpart = (uint)player & 0b111u;
            uint oyapart = (uint)oya & 0b111u;
            uint playerActivePart = _mk_playerActivePart(playerActive);
            arg0 = OPCODE_PLAYERJOIN | playerpart << 8 | oyapart << 11 | playerActivePart << 14;
        }

        private int opplayerjoin_oya()
        {
            return (int)((arg0 >> 11) & 0b111u);
        }

        private void mkop_playerleave(int player, bool[] playerActive)
        {
            uint playerpart = (uint)player & 0b111u;
            uint playerActivePart = _mk_playerActivePart(playerActive);
            arg0 = OPCODE_PLAYERLEAVE | playerpart << 8 | playerActivePart << 14;
        }

        private int opplayer_player()
        {
            return (int)((arg0 >> 8) & 0b111u);
        }

        private bool[] opplayer_playerActive()
        {
            return _get_playerActive(14);
        }

        private void mkop_yourthrow(int player, int rethrow)
        {
            uint playerpart = (uint)player & 0b111u;
            uint rethrowpart = (uint)rethrow & 0b111u;
            arg0 = OPCODE_YOURTHROW | playerpart << 8 | rethrowpart << 25;
        }

        private int opyourthrow_player()
        {
            return (int)((arg0 >> 8) & 0b111u);
        }

        private int opyourthrow_rethrow()
        {
            return (int)((arg0 >> 25) & 0b111u);
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

        private void mkop_throwresult(int player, int[] result, uint throw_type)
        {
            arg0 = _mkop_throw_helper(OPCODE_THROWRESULT, player, result, throw_type);
        }

        private void mkop_oyathrowresult(int player, int[] result, uint throw_type)
        {
            arg0 = _mkop_throw_helper(OPCODE_OYATHROWRESULT, player, result, throw_type);
        }

        private int opthrow_player()
        {
            return (int)((arg0 >> 8) & 0b111u);
        }

        private int[] opthrow_result()
        {
            int[] result = new int[3];
            uint resultpart = (arg0 >> 11);
            result[0] = (int)(resultpart & 0b111u);
            result[1] = (int)((resultpart >> 3) & 0b111u);
            result[2] = (int)((resultpart >> 6) & 0b111u);
            return result;
        }

        private uint opthrow_type()
        {
            uint throwpart = (arg0 >> 20) & 0b1111u;
            return throwpart;
        }

        private void mkop_balance(int player, float amount)
        {
            uint playerpart = (uint)player & 0b111u;
            uint signpart = (amount < 0.0f) ? 1u : 0u;
            uint amountpart = (uint)(Mathf.Abs(amount)) & 0xFFFFu;
            arg0 = OPCODE_BALANCE | playerpart << 8 | signpart << 15 | amountpart << 16;
        }

        private int opbalance_player()
        {
            return (int)((arg0 >> 8) & 0b111u);
        }

        private float opbalance_amount()
        {
            uint signpart = (arg0 >> 15) & 0b1u;
            uint amountpart = (arg0 >> 16) & 0xFFFFu;
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

        private void mkop_oyareport(int player, bool[] playerActive)
        {
            uint playerPart = (uint)player & 0b111u;
            uint playerActivePart = _mk_playerActivePart(playerActive);

            arg0 = OPCODE_OYAREPORT | playerPart << 8 | playerActivePart << 14;
        }

        private int opoyareport_oya()
        {
            return opoyachange_toplayer();
        }

        private bool[] opoyareport_playerActive()
        {
            return _get_playerActive(14);
        }

        private void mkop_oyachange(int fromPlayer, int toPlayer, bool[] playerActive)
        {
            uint toPlayerPart = (uint)toPlayer & 0b111u;
            uint fromPlayerPart = (uint)fromPlayer & 0b111u;
            uint playerActivePart = _mk_playerActivePart(playerActive);
            arg0 = OPCODE_OYACHANGE | toPlayerPart << 8 | fromPlayerPart << 11 | playerActivePart << 14;
        }

        private int opoyachange_toplayer() {
            return (int)((arg0 >> 8) & 0b111u);
        }

        private int opoyachange_fromplayer() {
            return (int)((arg0 >> 11) & 0b111u);
        }

        private bool[] _get_playerActive(int shift) {
            bool[] result = new bool[MAX_PLAYERS];
            uint playerActivePart = (arg0 >> shift) & 0b1111u;
            for (int i = 0; i < MAX_PLAYERS; ++i) {
                if (((playerActivePart >> i) & 1u) == 1)
                    result[i] = true;
            }

            return result;
        }

        private bool[] opoyachange_playerActive()
        {
            return _get_playerActive(14);
        }

        private void mkop_nooya() {
            arg0 = OPCODE_NOOYA;
        }
        #endregion
    }
}
