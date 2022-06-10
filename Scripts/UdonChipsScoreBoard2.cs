
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Enums;
using UnityEngine.UI;
using TMPro;
using UCS;
using System;

namespace XZDice
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class UdonChipsScoreBoard2 : UdonSharpBehaviour
    {
        // Maximum amount of entries in the scoreboard. Set to 80 since that is the largest hard-cap we can have,
        // although our prefab can actually display a lot less lines than this.
        private readonly int entrylist_length = 82;

        [SerializeField]
        [Tooltip("How often to check udonChips.money for changes (seconds)")]
        private int refresh_fast = 2; // Refresh locally and only send when changed
        [SerializeField]
        [Tooltip("How often we retransmit udonChips.money even if the amount hasn't changed")]
        private int refresh_slow = 60; // Send even if it hasn't changed

        [SerializeField]
        [Tooltip("When true the master will be indicated with a star in the list")]
        private bool markMaster = false;

        [SerializeField]
        private TextMeshProUGUI textMeshPro = null;

        private UdonChips udonChips = null;

        [UdonSynced] private uint newEntry = uint.MaxValue;

        private float old_amount = float.NaN;

        private uint[] entries;

        private void Start()
        {
            udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();

            if (textMeshPro == null) {
                textMeshPro = GetComponent<TextMeshProUGUI>();
            }

            entries = new uint[entrylist_length]; // Gets zero-initialized

            ClearOldAmount(); // Starts the thread that resets old_amount every refresh_slow seconds (causing things to get sent even if udonChips.money remains unchanged)
            SendAmount(); // Starts the thread that checks whether udonchips has changed every refresh_fast seconds
        }

        public override void OnDeserialization()
        {
            if (entry_filled(newEntry)) {
                AddEntry();
                UpdateText();
            }
        }

        // Top 24 bits encode amount, bottom 8 bits encode id. 0 is interpreted as being unused/unfilled 
        // (it is assumed id will never be larger than 254 nor smaller than 1)
        private uint make_entry(int id, float amount) {
            return (((uint)amount & 0x00ffffffu) << 8) | ((uint)id & 0xffu);
        }

        private int entry_id(uint entry) {
            return (int)(entry & 0xffu);
        }

        private uint entry_amount(uint entry) {
            return (entry >> 8) & 0x00ffffffu;
        }

        private bool entry_filled(uint entry) {
            return (entry != 0) && (entry != uint.MaxValue);
        }

        private string entry_name(uint entry) {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(entry_id(entry));
            if (player == null)
                return "";
            return player.displayName + ((markMaster && player.isMaster) ? "<color=\"yellow\"><sup>☆</sup></color>" : "");
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // Need to remove player from list, while compacting list
            uint[] newentries = new uint[entrylist_length];
            int idx = 0;
            for (int i = 0; i < entries.Length; ++i) {
                if (entry_id(entries[i]) != player.playerId) {
                    newentries[idx++] = entries[i];
                }
            }
            entries = newentries;

            UpdateText();

            // Forces everyone to resync soon
            old_amount = float.NaN;
        }

        public override void OnPlayerJoined(VRCPlayerApi player) {
            // Forces everyone to resync soon
            old_amount = float.NaN;
        }

        // Simple insertion sort with inverted comparison so we sort from largest to smallest
        public void reverseSort(uint[] array)
        {
            int i = 1;
            int length = array.Length;
            while (i < length) {
                uint x = array[i];
                int j = i - 1;
                while (j >= 0 && array[j] < x) {
                    array[j+1] = array[j];
                    j--;
                }
                array[j+1] = x;
                i++;
            }
        }

        private void AddEntry()
        {
            if (!entry_filled(newEntry))
                return;

            // Try to either find an existing entry and update it in place, or insert newly in the first non-filled entry.
            // The array is maintained such that filled entries will always come before non-filled entries,
            // thus we first check if we have gone past all filled entries first, since that means we can insert without
            // worrying about matching ids. If we have yet to get to the non-filled entries, we check that the id matches
            // and replace the existing entry in-place instead.
            bool found = false;
            for (int i = 0; i < entries.Length; ++i) {
                if (!entry_filled(entries[i]) || entry_id(newEntry) == entry_id(entries[i])) {
                    entries[i] = newEntry;
                    found = true;
                    break;
                }
            }
            if (!found)
                Debug.LogError("Could not add new entry: Entries array seems to have become full.");

            // We can simply sort as is, as the amount, which we want to sort by, is in the most significant bits and will thus dominate.
            // Since the array is by default filled with 0, the non-filled entries will still be kept towards the end of the array.
            reverseSort(entries);
        }

        private void UpdateText()
        {
            string contents = "";
            for (int i = 0; i < entries.Length && entry_filled(entries[i]); ++i) {
                //contents += string.Format("{0} {1,-18} {2,5}\n", entry_id(entries[i]), entry_name(entries[i]), entry_amount(entries[i]));
                contents += string.Format("{0,-18}<pos=70%>{1,5}\n", entry_name(entries[i]), entry_amount(entries[i]));
            }

            if (textMeshPro != null) {
                textMeshPro.text = contents;
            }
        }

        public void SendAmount()
        {
            // Do not update unless udonchips amount has changed
            // + a couple of safety checks to make Udon not whine at us when run in the editor 
            if (udonChips == null || Networking.LocalPlayer == null || old_amount == udonChips.money) {
                SendCustomEventDelayedSeconds(nameof(SendAmount), refresh_fast);
                return;
            }

            old_amount = udonChips.money;

            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            // Make a new entry for the local player, and first apply it locally, and then serialize it so it ends up on everyone elses list
            newEntry = make_entry(Networking.LocalPlayer.playerId, udonChips.money);
            AddEntry();
            UpdateText();
            RequestSerialization();

            // Schedule next refresh
            // We simply refresh every refresh_fast seconds, as there is no way to tell exactly when the udonchips
            // value has changed without modifying the basic UdonChips class itself
            SendCustomEventDelayedSeconds(nameof(SendAmount), refresh_fast);
        }

        // Clears old_amount every refresh_slow seconds. This makes sure that we always retransmit
        // somewhat infrequently in case a message got lost somewhere or so.
        public void ClearOldAmount()
        {
            old_amount = float.NaN;

            SendCustomEventDelayedSeconds(nameof(ClearOldAmount), refresh_slow);
        }
    }
}
