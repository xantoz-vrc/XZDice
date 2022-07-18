// TODO: throw people out when they have too little money

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using UnityEngine.UI;
using TMPro;

#if VITDECK_HIDE_MENUITEM
namespace Vket2022Summer.Circle314
#else
namespace XZDice
#endif
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChinchirorinSinglePlayer : UdonSharpBehaviour
    {
        [SerializeField]
        private string npcName = "CPU";

        [SerializeField]
        private DieGrabSphere2 dieGrabSphere;

        [SerializeField]
        private GameObject joinButton;

        [SerializeField]
        private GameObject betScreen;

        [SerializeField]
        private TextMeshProUGUI betLabelPlayer;
        [SerializeField]
        private TextMeshProUGUI betLabelNPC;

        [SerializeField]
        private TextMeshProUGUI toBeatLabelPlayer;
        [SerializeField]
        private TextMeshProUGUI toBeatLabelNPC;

        [SerializeField]
        private Transform diceSpawnPlayer;
        [SerializeField]
        private Transform diceSpawnNPC;
        [SerializeField]
        private Transform overBowl;

        // [SerializeField]
        // private GameObject timeoutDisplay;

        [SerializeField]
        private TextMeshProUGUI kachingLabelPlayer;
        [SerializeField]
        private TextMeshProUGUI kachingLabelNPC;

        [SerializeField]
        private TextMeshProUGUI resultPopupLabelPlayer;
        [SerializeField]
        private TextMeshProUGUI resultPopupLabelNPC;

        [SerializeField]
        private GameObject oyaMarker;
        [SerializeField]
        private Transform NPCOyaMarkerPos;

        [SerializeField]
        private AudioSource[] diceSounds;

        [SerializeField]
        private AudioSource kachingSound;

        [SerializeField]
        private AudioSource errorSound;

        [SerializeField]
        private GameLog gameLog = null;

        [SerializeField]
        [Tooltip("GameLog which gets debug logs written to it as well")]
        private GameLog gameLogDebug = null;

        [SerializeField]
        [Tooltip("Enable debugging (also make sure to set gameLogDebug)")]
        private bool DEBUG = true;

        private readonly bool SPAM = true; // Enable spam level debug logs

#if VITDECK_HIDE_MENUITEM
        public UdonBehaviour VketUdonChips;
#else
        private UdonBehaviour udonChips = null;
#endif

        private bool langJp = false;

        private readonly float MAXBET = 20000.0f;
        private readonly int MAX_RETHROWS = 3;
        // private readonly int TIMEOUT_SECS = 60;

        private float playerBet = 0.0f;
        private float npcBet= 0.0f;

        private int betMultiplier = 0;

        private bool showOyaMarker = false;

        private bool oyaLost = false;
        private uint oyaThrowType;
        private int[] recvResult = new int[3];
        int recvResult_cntr = 0;

        private readonly int PLAYER_HUMAN = 1;
        private readonly int PLAYER_NPC = 2;

        private readonly int OYA_PLAYER = 1;
        private readonly int OYA_NPC = 2;
        private int oya = -1;

        private readonly float delayPostThrowRethrow = 1.0f;
        private readonly float delayPostThrow = 2.0f;

        private bool active = false;

        private int rethrowCount = 0;

#if VITDECK_HIDE_MENUITEM
        private bool inBooth = false;
#endif

        private void GameLog(string message)
        {
            if (gameLog != null) {
                gameLog._Log(message);
            }
            if (gameLogDebug != null) {
                gameLogDebug._Log(message);
            }
        }

        private void GameLog2(string message1, string message2)
        {
            if (gameLog != null) {
                gameLog._Log(message1);
            }
            if (gameLogDebug != null) {
                gameLogDebug._Log((DEBUG) ? message2 : message1);
            }
        }

        private void GameLogDebug(string message)
        {
            if (DEBUG && gameLogDebug != null) {
                gameLogDebug._Log("<color=\"grey\">" + message + "</color>");
            }
        }

        private void GameLogSpam(string message)
        {
            if (DEBUG && SPAM && gameLogDebug != null) {
                gameLogDebug._Log("<color=#404040ff>" + message + "</color>");
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

#if VITDECK_HIDE_MENUITEM
        public void _VketStart()
#else
        private void Start()
#endif
        {
            GameLogDebug("Start");

            if (dieGrabSphere._GetLength() != 3)
                Debug.LogError("Must be three dice");

            dieGrabSphere._AddListener(this);
            dieGrabSphere.hideOnThrow = true; // Ensure hideOnThrow is set

#if VITDECK_HIDE_MENUITEM
#else
            udonChips = (UdonBehaviour)GameObject.Find("UdonChips").GetComponent(typeof(UdonBehaviour));
#endif

            ResetTable();

#if VITDECK_HIDE_MENUITEM
#else
            EnableAudioSources(true);
            joinButton.SetActive(true);
#endif
        }

#if VITDECK_HIDE_MENUITEM
        private string formatChips(float amount)
        {
            string formatString = (string)VketUdonChips.GetProgramVariable("format");
            return string.Format(formatString, amount);
        }

        private float getUdonChipsMoney() { return (float)VketUdonChips.GetProgramVariable("money"); }
        private void setUdonChipsMoney(float amount) { VketUdonChips.SetProgramVariable("money", amount); }
        private void incUdonChipsMoney(float amount) { setUdonChipsMoney(getUdonChipsMoney() + amount); }
        private void decUdonChipsMoney(float amount) { incUdonChipsMoney(-amount); }
#else
        private string formatChips(float amount)
        {
            string formatString = (string)udonChips.GetProgramVariable("format");
            return string.Format(formatString, amount);
        }

        private float getUdonChipsMoney()             { return (float)udonChips.GetProgramVariable("money"); }
        private void  setUdonChipsMoney(float amount) { udonChips.SetProgramVariable("money", amount); }
        private void  incUdonChipsMoney(float amount) { setUdonChipsMoney(getUdonChipsMoney() + amount); }
        private void  decUdonChipsMoney(float amount) { incUdonChipsMoney(-amount); }
#endif

        private readonly int STATE_BEGIN = 1;
        private readonly int STATE_BET = 2;
        private readonly int STATE_PREPARE_OYATHROW = 3;
        private readonly int STATE_OYATHROW = 4;
        private readonly int STATE_PREPARE_THROW = 5;
        private readonly int STATE_THROW = 6;
        private readonly int STATE_BALANCE = 7;
        private int state = -1;

        private readonly int EVENT_NONE = 0;
        private readonly int EVENT_LEAVE = 2;
        private readonly int EVENT_BET = 0x10;
        private readonly int EVENT_BETUNDO = 0x11;
        private readonly int EVENT_BETDONE = 0x12;
        private readonly int EVENT_THROWRESULT = 0x20;

        private void SetButtonText(GameObject btn, string str)
        {
            // TODO: switch to GetComponentInChildren ?
            GameObject txtObj = btn.transform.GetChild(0).gameObject;
            TextMeshProUGUI text = txtObj.GetComponent<TextMeshProUGUI>();
            text.text = str;
        }

        public void _BtnJoin()
        {
            GameLogDebug("_BtnJoin");

            if (active) {
                LeaveGame();
            } else {
                JoinGame();
            }
        }

        private void JoinGame()
        {
            joinButton.SetActive(false);

            active = true;

            oya = OYA_NPC;
            state = STATE_BEGIN;
            _StateMachine(EVENT_NONE, 0);

            SetButtonText(joinButton, "Leave");
            joinButton.SetActive(true);
        }

        private void LeaveGame()
        {
            joinButton.SetActive(false);

            _StateMachine(EVENT_LEAVE, 0);

            active = false;

            SetButtonText(joinButton, "Join");
            joinButton.SetActive(true);
        }

        private string getPlayerName(int who)
        {
            if (who == PLAYER_HUMAN) {
                if (active && Utilities.IsValid(Networking.LocalPlayer))
                    return Networking.LocalPlayer.displayName;

                return "Player";
            } else if (who == PLAYER_NPC) {
                return npcName;
            } else {
                return "???";
            }
        }

        private readonly string oyaString = "<sup><color=\"yellow\">親</color></sup>";

        private void SetBetLabelPlayer(float amount)
        {
            if (!active) {
                betLabelPlayer.text = "Player";
            } else if (oya == OYA_PLAYER) {
                betLabelPlayer.text =
                    string.Format("{0}{1}\nMoney: {2}\n",
                                  getPlayerName(PLAYER_HUMAN), oyaString, formatChips(getUdonChipsMoney()));
            } else {
                betLabelPlayer.text =
                    string.Format("{0}\nMoney: {1}\nBet: {2}",
                                  getPlayerName(PLAYER_HUMAN), formatChips(getUdonChipsMoney()), formatChips(amount));
            }
        }

        private void SetBetLabelNPC(float amount)
        {
            string oyaPart = (oya == OYA_PLAYER) ? "<sup><color=\"yellow\">親</color></sup>" : "";

            if (oya == OYA_NPC) {
                betLabelNPC.text =
                    string.Format("{0}{1}",
                                  getPlayerName(PLAYER_NPC), oyaString);
            } else {
                betLabelNPC.text =
                    string.Format("{0}\nBet: {1}",
                                  getPlayerName(PLAYER_NPC), formatChips(amount));
            }
        }


        private void ResetTable()
        {
            kachingLabelPlayer.gameObject.SetActive(false);
            kachingLabelNPC.gameObject.SetActive(false);

            resultPopupLabelPlayer.gameObject.SetActive(false);
            resultPopupLabelNPC.gameObject.SetActive(false);

            toBeatLabelPlayer.gameObject.SetActive(false);
            toBeatLabelNPC.gameObject.SetActive(false);

            betScreen.SetActive(false);
            // timeoutDisplay.SetActive(false);

            SetBetLabelPlayer(0.0f);
            SetBetLabelNPC(0.0f);

            HideOyaMarker();
            dieGrabSphere._HideWithDice();
        }

        public void _BtnPlayerBet10()   { _StateMachine(EVENT_BET, 10); }
        public void _BtnPlayerBet50()   { _StateMachine(EVENT_BET, 50); }
        public void _BtnPlayerBet100()  { _StateMachine(EVENT_BET, 100); }
        public void _BtnPlayerBet500()  { _StateMachine(EVENT_BET, 500); }
        public void _BtnPlayerBetUndo() { _StateMachine(EVENT_BETUNDO, 0); }
        public void _BtnPlayerBetDone() { _StateMachine(EVENT_BETDONE, 0); }

        private void SetBetScreenButtons(bool val, bool enableDone)
        {
            Button[] buttons = betScreen.GetComponentsInChildren<Button>();

            foreach (Button btn in buttons) {
                if (btn.name == "DoneButton") { // Simply match by name
                    btn.interactable = val && enableDone;
                } else {
                    btn.interactable = val;
                }
            }
        }

        private float getPlayerMaxBet()
        {
            return Mathf.Clamp(getUdonChipsMoney()/3.0f, 0.0f, MAXBET);
        }

        private void UpdateBetScreen()
        {
            TextMeshProUGUI text = betScreen.GetComponentInChildren<TextMeshProUGUI>();

            text.text = string.Format("{0}\nMax Bet: {1}\nBet:{2}",
                                      getPlayerName(PLAYER_HUMAN), getPlayerMaxBet(), playerBet);
        }

        private void KachingLabel(int player, float amount, bool isOya)
        {
#if VITDECK_HIDE_MENUITEM
            if (!inBooth) return;
#endif

            string color =
                (amount < 0.0f) ? "#ff0000" :
                (amount > 0.0f) ? "#00ff00" : "#ffff00";

            var label = (player == PLAYER_HUMAN) ? kachingLabelPlayer : kachingLabelNPC;

            // Toggling the gameobject restarts the animation
            label.gameObject.SetActive(false);
            label.text = string.Format("<color={0}>{1}</color>", color, formatChips(amount));
            label.gameObject.SetActive(true);

            string oyaPart = (isOya) ? "(dealer) " : "";
            if (amount > 0.0f)
                GameLog(string.Format("{0} {1}won <color=\"lime\">{2}</color>",
                                      getPlayerName(player), oyaPart, formatChips(Mathf.Abs(amount))));
            else if (amount < 0.0f)
                GameLog(string.Format("{0} {1}lost <color=\"red\">{2}</color>",
                                      getPlayerName(player), oyaPart, formatChips(Mathf.Abs(amount))));
            else
                GameLog(string.Format("{0} {1}<color=\"yellow\">no change</color>",
                                      getPlayerName(player), oyaPart));
        }

        private string getThrowTypeColor(uint throw_type)
        {
            return
                (throw_type == THROW_INVALID)                      ? "#ff00ff" :
                (throw_type == THROW_1 || throw_type == THROW_123) ? "#ff0000" :
                (throw_type == THROW_SHONBEN)                      ? "#a52a2a" :
                (throw_type == THROW_MENASHI)                      ? "#c0c0c0" : "#00ff00";
        }

        private string getRelativeThrowTypeColor(uint tt, uint oya_tt)
        {
            return
                (tt == THROW_123)                       ? "#ff0000" :
                (tt == THROW_SHONBEN)                   ? "#a52a2a" :
                (tt == THROW_MENASHI)                   ? "#c0c0c0" :
                (tt == THROW_ZOROME || tt == THROW_456) ? "#00ff00" :
                (tt > 0 && tt <= 6 && tt > oya_tt)      ? "#00ff00" :
                (tt > 0 && tt <= 6 && tt == oya_tt)     ? "#ffff00" :
                (tt > 0 && tt <= 6 && tt < oya_tt)      ? "#ff0000" : "#ff00ff";
        }

        private void SetToBeatLabel(int[] result, uint throw_type, int player)
        {
            // Only time something will go on is if the oya throws between 2 and 5 points, anything
            // else immediately ends the round.
            if (!(THROW_2 <= throw_type && throw_type <= THROW_5))
                return;

            string text = (langJp) ? "親の出目: " : "To beat:\n";
            text += string.Format("{0} {1} {2} = <color={3}>{4}</color>",
                                  result[0], result[1], result[2],
                                  getThrowTypeColor(throw_type), formatThrowType(throw_type));

            if (player == PLAYER_HUMAN) {
                toBeatLabelPlayer.text = text;
                toBeatLabelPlayer.gameObject.SetActive(true);
            } else {
                toBeatLabelNPC.text = text;
                toBeatLabelNPC.gameObject.SetActive(true);
            }
        }

        private void ShowThrowResult(int player, int[] result, uint throw_type, bool oyaThrow)
        {
#if VITDECK_HIDE_MENUITEM
            if (!inBooth) return;
#endif

            if (result.Length != 3) {
                Debug.LogError("ShowThrowResult called with bad result array");
                result = new int[3];
            }

            string color =
                (oyaThrow) ? getThrowTypeColor(throw_type) : getRelativeThrowTypeColor(throw_type, oyaThrowType);

            var label = (player == PLAYER_HUMAN) ? resultPopupLabelPlayer : resultPopupLabelNPC;

            // Toggling the gameobject restarts the animation
            label.gameObject.SetActive(false);

            string resultStr = (throw_type == THROW_SHONBEN) ? "" : string.Format("{0} {1} {2}", result[0], result[1], result[2]);
            label.text = string.Format("<size=40%>{0}</size>\n<color={1}>{2}</color>",
                                       resultStr, color, formatThrowType(throw_type));
            label.gameObject.SetActive(true);

            // Show oyas result to everyone
            if (oyaThrow) {
                SetToBeatLabel(result, throw_type, (player == PLAYER_NPC) ? PLAYER_HUMAN : PLAYER_NPC);
                var lbl = (player == PLAYER_HUMAN) ? resultPopupLabelNPC : resultPopupLabelPlayer;

                // Toggling the gameobject restarts the animation
                lbl.gameObject.SetActive(false);
                lbl.text = string.Format("<size=40%>{0}</size>\n<color={1}>{2}</color>",
                                         (langJp) ? "親の出目" : "Dealers Result", color, formatThrowType(throw_type));
                lbl.gameObject.SetActive(true);
            }
        }


        public void _StateMachine(int ev, int arg)
        {
            GameLogDebug(string.Format("_StateMachine({0}, {1}), state={2}",
                                       ev, arg, state));

            if (!active) {
                GameLogWarn("_StateMachine called when not active");
                return;
            }

            while (true) {
                if (ev == EVENT_LEAVE) {
                    GameLogDebug("ev == EVENT_LEAVE");
                    oya = -1;
                    active = false;
                    ResetTable();
                    return;
                }

                if (state == STATE_BEGIN) {
                    GameLogDebug("state == STATE_BEGIN");

                    ResetTable();
                    oyaLost = false;
                    oyaThrowType = THROW_INVALID;

                    ev = EVENT_NONE;
                    state = STATE_BET;
                    ShowOyaMarker();
                    continue;
                } else if (state == STATE_BET) {
                    GameLogDebug("state == STATE_BET");

                    int currentPlayer = (oya == OYA_NPC) ? PLAYER_HUMAN : PLAYER_NPC;
                    if (ev == EVENT_BETDONE) {
                        GameLog(string.Format("{0} bet {1}",
                                              getPlayerName(currentPlayer),
                                              formatChips((currentPlayer == PLAYER_HUMAN) ? playerBet : (float)arg)));
                    }

                    if (oya == OYA_NPC) {
                        if (ev == EVENT_BETDONE) {
                            SetBetLabelPlayer(playerBet);
                            UpdateBetScreen();
                            SetBetScreenButtons(false, false);
                            betScreen.SetActive(false);

                            ev = EVENT_NONE;
                            state = STATE_PREPARE_OYATHROW;
                            continue;
                        } else if (ev == EVENT_BETUNDO) {
                            playerBet = 0.0f;
                        } else if (ev == EVENT_BET) {
                            if (playerBet + arg > getPlayerMaxBet()) {
                                PlayErrorSound();
                                GameLog(string.Format("<color=\"red\">You can at most bet a third of your total ({0})</color>",
                                                      formatChips(getPlayerMaxBet())));
                            } else {
                                playerBet += (float)arg;
                            }
                        } else if (ev == EVENT_NONE) {
                            playerBet = 0.0f;
                            betScreen.SetActive(true);
                            GameLog("Waiting for bet");
                        }
                        SetBetScreenButtons(true, playerBet > 0.0f);
                        SetBetLabelPlayer(playerBet);
                        UpdateBetScreen();
                    } else {
                        if (ev == EVENT_BETDONE) {
                            npcBet = (float)arg;

                            SetBetLabelNPC(npcBet);

                            ev = EVENT_NONE;
                            state = STATE_PREPARE_OYATHROW;
                            continue;
                        } else if (ev == EVENT_NONE) {
                            npcBet = 0;
                            GameLog("Waiting for bet");
                            NPCDoBet();
                        }
                        SetBetLabelNPC(npcBet);
                    }
                    return;
                } else if (state == STATE_PREPARE_OYATHROW) {
                    GameLogDebug("state == STATE_PREPARE_OYATHROW");

                    rethrowCount = 0;
                    ev = EVENT_NONE;
                    state = STATE_OYATHROW;
                    continue;
                } else if (state == STATE_OYATHROW) {
                    GameLogDebug("state == STATE_OYATHROW");

                    int currentPlayer = (oya == OYA_NPC) ? PLAYER_NPC : PLAYER_HUMAN;

                    if (rethrowCount >= MAX_RETHROWS) {
                        GameLog("Out of rethrows");
                        oyaLost = true;
                        betMultiplier = 1;

                        state = STATE_BALANCE; ev = EVENT_NONE;
                        SendCustomEventDelayedSeconds(nameof(_StateMachine), delayPostThrow);
                        return;
                    } else if (ev == EVENT_THROWRESULT) {
                        uint throw_type = (uint)arg;
                        oyaThrowType = throw_type;
                        ShowThrowResult(currentPlayer, recvResult, throw_type, true);

                        if (arg == THROW_SHONBEN)
                            GameLog(string.Format("{0} (dealer) threw <color={1}>outside</color>",
                                                  getPlayerName(currentPlayer), getThrowTypeColor(throw_type)));
                        else
                            GameLog(string.Format("{0} (dealer) threw {1} {2} {3}: <color={4}>{5}</color>",
                                                  getPlayerName(currentPlayer), recvResult[0], recvResult[1], recvResult[2],
                                                  getThrowTypeColor(throw_type), formatThrowType(throw_type)));

                        if (throw_type == THROW_MENASHI) {
                            rethrowCount++;
                            ev = EVENT_NONE;
                            SendCustomEventDelayedSeconds(nameof(_StateMachine), delayPostThrowRethrow);
                            return;
                        }

                        if (throw_type == THROW_SHONBEN) {
                            oyaLost = true;
                            betMultiplier = 1;

                            state = STATE_BALANCE; ev = EVENT_NONE;
                            SendCustomEventDelayedSeconds(nameof(_StateMachine), delayPostThrow);
                            return;
                        }

                        if (throw_type == THROW_1 || throw_type == THROW_123) {
                            // Insta-fail and payout to everybody (different multiplier)
                            oyaLost = true;
                            betMultiplier = (throw_type == THROW_123) ? 2 : 1;

                            state = STATE_BALANCE; ev = EVENT_NONE;
                            SendCustomEventDelayedSeconds(nameof(_StateMachine), delayPostThrow);
                            return;
                        }

                        // 6 points, 456 or Triple: Oya wins instantly (different multipliers)
                        if (throw_type == THROW_6 || throw_type == THROW_456 || throw_type == THROW_ZOROME) {
                            betMultiplier = (throw_type == THROW_456)    ? -2 :
                                            (throw_type == THROW_ZOROME) ? -3 : -1;

                            state = STATE_BALANCE; ev = EVENT_NONE;
                            SendCustomEventDelayedSeconds(nameof(_StateMachine), delayPostThrow);
                            return;
                        }

                        state = STATE_PREPARE_THROW; ev = EVENT_NONE;
                        SendCustomEventDelayedSeconds(nameof(_StateMachine), delayPostThrow);
                        return;
                    } else {
                        YourThrow(currentPlayer);
                        return;
                    }
                } else if (state == STATE_PREPARE_THROW) {
                    GameLogDebug("state == STATE_PREPARE_THROW");

                    rethrowCount = 0;
                    ev = EVENT_NONE;
                    state = STATE_THROW;
                    continue;
                } else if (state == STATE_THROW) {
                    GameLogDebug("state == STATE_THROW");

                    int currentPlayer = (oya == OYA_NPC) ? PLAYER_HUMAN : PLAYER_NPC;

                    if (rethrowCount >= MAX_RETHROWS) {
                        GameLog("Out of rethrows");
                        betMultiplier = -1;

                        state = STATE_BALANCE; ev = EVENT_NONE;
                        SendCustomEventDelayedSeconds(nameof(_StateMachine), delayPostThrow);
                        return;
                    } else if (ev == EVENT_THROWRESULT) {
                        uint throw_type = (uint)arg;
                        ShowThrowResult(currentPlayer, recvResult, throw_type, false);

                        if (arg == THROW_SHONBEN)
                            GameLog(string.Format("{0} threw <color={1}>outside</color>",
                                                  getPlayerName(currentPlayer), getThrowTypeColor(throw_type)));
                        else
                            GameLog(string.Format("{0} threw {1} {2} {3}: <color={4}>{5}</color>",
                                                  getPlayerName(currentPlayer), recvResult[0], recvResult[1], recvResult[2],
                                                  getThrowTypeColor(throw_type), formatThrowType(throw_type)));

                        if (throw_type == THROW_MENASHI) {
                            rethrowCount++;
                            ev = EVENT_NONE;
                            SendCustomEventDelayedSeconds(nameof(_StateMachine), delayPostThrowRethrow);
                            return;
                        }

                        if (throw_type == THROW_SHONBEN) {
                            betMultiplier = -1;
                        } else if (throw_type == THROW_6) {
                            oyaLost = true;
                            betMultiplier = 1;
                        } else if (throw_type == THROW_456) {
                            oyaLost = true;
                            betMultiplier = 2;
                        } else if (throw_type == THROW_ZOROME) {
                            oyaLost = true;
                            betMultiplier = 3;
                        } else if (throw_type == THROW_123) {
                            betMultiplier = -2;
                        } else if (throw_type == THROW_1) {
                            betMultiplier = -1;
                        } else if (throw_type > oyaThrowType) {
                            oyaLost = true;
                            betMultiplier = 1;
                        } else if (throw_type < oyaThrowType) {
                            betMultiplier = -1;
                        } else {
                            // wakare
                            betMultiplier = 0;
                        }

                        state = STATE_BALANCE; ev = EVENT_NONE;
                        SendCustomEventDelayedSeconds(nameof(_StateMachine), delayPostThrow);
                        return;
                    } else {
                        YourThrow(currentPlayer);
                        return;
                    }
                } else if (state == STATE_BALANCE) {
                    GameLogDebug("state == STATE_BALANCE");

                    // TODO: only need one bet variable
                    float amount = ((oya == OYA_NPC) ? playerBet : npcBet) * betMultiplier;

                    PlayKachingSound();
                    if (oya == OYA_PLAYER) {
                        decUdonChipsMoney(amount);
                        KachingLabel(PLAYER_HUMAN, -amount, true);
                        KachingLabel(PLAYER_NPC, amount, false);
                    } else {
                        incUdonChipsMoney(amount);
                        KachingLabel(PLAYER_HUMAN, amount, false);
                        KachingLabel(PLAYER_NPC, -amount, true);
                    }

                    if (getUdonChipsMoney() < 0.0f)
                        setUdonChipsMoney(0.0f);

                    if (oyaLost) {
                        int currentOya = (oya == OYA_NPC) ? PLAYER_NPC : PLAYER_HUMAN;
                        int nextOya = (oya == OYA_NPC) ? PLAYER_HUMAN : PLAYER_NPC;

                        GameLog(string.Format("Dealer change {0} -> {1}",
                                              getPlayerName(currentOya), getPlayerName(nextOya)));
                        oya = (oya == OYA_NPC) ? OYA_PLAYER : OYA_NPC;
                    }

                    state = STATE_BEGIN; ev = EVENT_NONE;
                    SendCustomEventDelayedSeconds(nameof(_StateMachine), 5.0f);
                    return;
                }
            }
        }

        private void YourThrow(int player)
        {
            GameLog(string.Format("Waiting for {0} to throw ({1}/{2})",
                                  getPlayerName(player), rethrowCount+1, MAX_RETHROWS));

            PrepareRecvThrow();

            dieGrabSphere._BecomeOwner();
            dieGrabSphere._Show();
            if (player == PLAYER_NPC) {
                dieGrabSphere._SetPickupable(false);
                dieGrabSphere._TeleportTo(diceSpawnNPC);
                dieGrabSphere._ParkDice();
                NPCDoThrow();
            } else {
                dieGrabSphere._SetPickupable(true);
                dieGrabSphere._TeleportTo(diceSpawnPlayer);
                dieGrabSphere._ParkDice();
            }
        }

        private void NPCDoBet()
        {
            // TODO: speech bubble or something: "Hmm.. What should I bet?"

            SendCustomEventDelayedSeconds(nameof(_NPCDoBet_Continuation), 1.0f);
        }

        public void _NPCDoBet_Continuation()
        {
            int[] bets = new int[] { 10, 10, 10, 10, 50, 50, 50, 100, 100, 500 };
            Utilities.ShuffleArray(bets);
            _StateMachine(EVENT_BETDONE, bets[0]);
        }

        private void NPCDoThrow()
        {
            // TODO: animation and stuff ?
            dieGrabSphere.OnPickup();

            SendCustomEventDelayedSeconds(nameof(_NPCDoThrow_Continuation1), 1.0f);
        }

        public void _NPCDoThrow_Continuation1()
        {
            dieGrabSphere._TeleportTo(overBowl);

            SendCustomEventDelayedSeconds(nameof(_NPCDoThrow_Continuation2), 1.0f);
        }

        public void _NPCDoThrow_Continuation2()
        {
            dieGrabSphere.OnDrop();
        }


        private void ShowOyaMarker()
        {
#if VITDECK_HIDE_MENUITEM
                if (!inBooth) return;
#endif

                showOyaMarker = true;
                oyaMarker.SetActive(true);
            }

            private void HideOyaMarker()
            {
                showOyaMarker = false;
                oyaMarker.SetActive(false);
            }

            private void OyaMarkerUpdate()
            {
                if (oya == OYA_NPC)
                {
                    oyaMarker.transform.position = NPCOyaMarkerPos.position;
                }
                else
                {
                    VRCPlayerApi player = Networking.LocalPlayer;
                    if (!Utilities.IsValid(player))
                        return;

                    Vector3 pos = player.GetBonePosition(HumanBodyBones.Head);
                    pos.y += 0.4f;

                    oyaMarker.transform.position = pos;
                }
            }

#if VITDECK_HIDE_MENUITEM
            public void _VketUpdate()
#else
        private void Update()
#endif
            {
                if (showOyaMarker)
                {
                    OyaMarkerUpdate();
                }
            }

            #region throw
            // Helpers for a simple little format that we use to compare dice results
            private uint diceres(uint d1, uint d2, uint d3)
            {
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
                    return "Triple";

                if (throw_type == THROW_456)
                    return "4-5-6";

                if (throw_type == THROW_123)
                    return "1-2-3";

                if (throw_type == THROW_MENASHI)
                    return "No points";

                if (throw_type == THROW_SHONBEN)
                    return "Outside";
            }

            return "???";
        }
        #endregion


        #region DieGrabSphereListener
        // DiGrabSphereListener
        public void _SetThrown()
        {
            PlayDiceSound();
        }

        // DieGrabSphereListener
        public void _SetHeld()
        {
            // Do nothing
        }

        // DieGrabSphereListener
        public void _DiceResult0() { DiceResult(0); }
        public void _DiceResult1() { DiceResult(1); }
        public void _DiceResult2() { DiceResult(2); }
        public void _DiceResult3() { DiceResult(3); }
        public void _DiceResult4() { DiceResult(4); }
        public void _DiceResult5() { DiceResult(5); }
        public void _DiceResult6() { DiceResult(6); }

        private void PrepareRecvThrow()
        {
            for (int i = 0; i < recvResult.Length; ++i)
                recvResult[i] = -1;
            recvResult_cntr = 0;
        }

        private void DiceResult(int result)
        {
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
                uint throw_type = classify_throw(recvResult);
                _StateMachine(EVENT_THROWRESULT, (int)throw_type);
            }
        }
        #endregion

        #region sounds
        private void EnableAudioSources(bool val)
        {
            foreach (var sound in diceSounds) {
                sound.enabled = val;
            }
            kachingSound.enabled = val;
            errorSound.enabled = val;
        }

        private void PlayDiceSound()
        {
            GameLogDebug("PlayDiceSound");

            if (diceSounds != null && diceSounds.Length > 0) {
                int idx = Mathf.RoundToInt(Random.Range(0.0f, (float)(diceSounds.Length - 1)));
                diceSounds[idx].Play();
            }
        }

        private bool kachingSoundPlaying = false;
        private void PlayKachingSound()
        {
            GameLogDebug("PlayKachingSound");

            if (kachingSound != null && !kachingSound.isPlaying) {
                kachingSound.Play();
            }
        }

        private void PlayErrorSound()
        {
            GameLogDebug("PlayErrorSound");

            if (errorSound != null) {
                errorSound.Play();
            }
        }
        #endregion

#if VITDECK_HIDE_MENUITEM
        public void _VketOnBoothEnter()
        {
            GameLogDebug("_VketOnBoothEnter");
            joinButton.SetActive(true);
            inBooth = true;
            EnableAudioSources(true);
        }

        public void _VketOnBoothExit()
        {
            GameLogDebug("_VketOnBoothExit");
            if (active) {
                LeaveGame();
            }
            joinButton.SetActive(false);
            inBooth = false;
            EnableAudioSources(false);
        }
#endif
    }
}
