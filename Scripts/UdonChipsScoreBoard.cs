using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Enums;
using UnityEngine.UI;
using TMPro;
using UCS;

namespace XZDice
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class UdonChipsScoreBoard : UdonSharpBehaviour
    {
        private readonly int entrylist_length = 80;

        [SerializeField]
        private int refresh_seconds = 10;

        [SerializeField]
        private TextMeshProUGUI textMeshPro = null;

        [SerializeField]
        private Text text = null;

        private UdonChips udonChips = null;

        [UdonSynced] private int newEntry_id = -1;
        [UdonSynced] private string newEntry_name = "";
        [UdonSynced] private float newEntry_amount = float.NaN;
        [UdonSynced] private bool entry_pending = false;  // True when sending a new entry to master

        private float old_amount = float.NaN;

        [UdonSynced]
        private string contents;

        // Udon sharp does not support structs (AAAAAAAAH!), so I'm forced to make several lists with correllated indexes instead.
        private int[] entries_id;
        private string[] entries_name;
        private float[] entries_amount;
        private bool[] entries_filled;

        /* What I actually wanted to do:
        private struct Entry
        {
            public int id;
            public string name;
            public float amount;
            public bool filled;
        };

        private Entry[] entries;
        */

        private void Start()
        {
            udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();

            if (textMeshPro == null)
            {
                textMeshPro = GetComponent<TextMeshProUGUI>();
            }

            if(text == null)
            {
                text = GetComponent<Text>();
            }
            //then = Time.time;

            SendAmount();
        }

        /*
        private float then = 0.0f;
        public void Update()
        {
            if (then + 10.0f > Time.time) {
                then = Time.time;
                SendAmount();
            }
        }
        */

        public override void OnDeserialization()
        {
            if (Networking.IsMaster && entry_pending) {
                // Master received a new entry request from non-master
                AddEntry();
            } else if (!Networking.IsMaster && !entry_pending) {
                // We received a new text string contents from master
                ApplyText();
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // Need to remove player from list, while compacting list
            if (Networking.IsMaster && entries_id != null) {
                int[] newids = new int[entrylist_length];
                string[] newnames = new string[entrylist_length];
                float[] newamounts = new float[entrylist_length];
                bool[] newfilled = new bool[entrylist_length];
                int idx = 0;
                for (int i = 0; i < entries_id.Length; ++i) {
                    if (!entries_filled[i]) {
                        break;
                    }
                    if (entries_id[i] != player.playerId) {
                        newids[idx] = entries_id[i];
                        newnames[idx] = entries_name[i];
                        newamounts[idx] = entries_amount[i];
                        newfilled[idx] = true;
                        idx++;
                    }
                }
                entries_id = newids;
                entries_name = newnames;
                entries_amount = newamounts;
                entries_filled = newfilled;

                UpdateText();
                ApplyText();
                BroadcastText();
            }

            // Forces everyone to resync after a while.
            // If we do not do this we risk that someone who just became master will not add themselves to their entries list.
            old_amount = float.NaN;
        }

        public override void OnPlayerJoined(VRCPlayerApi player) {
            // Forces everyone to resync after a while
            old_amount = float.NaN;
        }

        private void AddEntry()
        {
            if (!Networking.IsMaster) {
                Debug.LogError("AddEntry called for Non-master");
                return;
            }

            if (entries_id == null) {
                entries_id = new int[entrylist_length];
                entries_name = new string[entrylist_length];
                entries_amount = new float[entrylist_length];
                entries_filled = new bool[entrylist_length];
            }

            bool found = false;
            for (int i = 0; i < entries_id.Length && entries_filled[i]; ++i) {
                if (newEntry_id == entries_id[i]) {
                    // Update entry in place
                    found = true;
                    entries_name[i] = newEntry_name;
                    entries_amount[i] = newEntry_amount;
                }
            }

            if (!found) {
                // Didn't already exist in entries. Add in first empty slot (if available)
                // TODO: could be sped up if we keep the index from the previous loop
                for (int i = 0; i < entries_id.Length; ++i) {
                    if (!entries_filled[i]) {
                        entries_id[i] = newEntry_id;
                        entries_name[i] = newEntry_name;
                        entries_amount[i] = newEntry_amount;
                        entries_filled[i] = true;
                        break;
                    }
                }
            }

            entry_pending = false;

            UpdateText();
            ApplyText();
            BroadcastText();
        }

        private void UpdateText()
        {
            // TODO: sort by amount
            contents = "";
            for (int i = 0; i < entries_id.Length && entries_filled[i]; ++i) {
                contents += string.Format("{0} {1,-20} {2,6:F2}\n", entries_id[i], entries_name[i], entries_amount[i]);
            }
        }

        private void BroadcastText()
        {
            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            newEntry_id = -1;
            newEntry_name = "";
            newEntry_amount = 0.0f;
            entry_pending = false;
            RequestSerialization();
        }

        private void ApplyText()
        {
            if (text != null) {
                text.text = contents;
            }

            if (textMeshPro != null) {
                textMeshPro.text = contents;
            }
        }

        public void SendAmount()
        {
            // Do not update unless udonchips amount has changed
            if (old_amount == udonChips.money) {
                SendCustomEventDelayedSeconds(nameof(SendAmount), refresh_seconds);
                return;
            }

            old_amount = udonChips.money;

            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            entry_pending = true;
            newEntry_id = Networking.LocalPlayer.playerId;
            newEntry_name = Networking.LocalPlayer.displayName;
            newEntry_amount = udonChips.money;
            if (Networking.IsMaster) {
                newEntry_name += "\u2654";
                AddEntry();
            } else {
                // Empty text so we do not needlessly send that as well
                contents = "";
                RequestSerialization();
            }

            // Schedule next refresh
            // We simply refresh every refresh_secondsseconds, as there is no way to tell exactly when the udonchips
            // value has changed without modifying the basic UdonChips class itself
            SendCustomEventDelayedSeconds(nameof(SendAmount), refresh_seconds);
        }
    }
}
