﻿
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Enums;
using UnityEngine.UI;
using TMPro;
using UCS;
using System;

namespace XZDice
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class UdonChipsScoreBoard3 : UdonSharpBehaviour
    {
        // Maximum amount of entries in the scoreboard. Set to 80 since that is the largest hard-cap we can have,
        // although our prefab can actually display a lot less lines than this.
        private readonly int entrylist_length = 82;

        [SerializeField]
        [Tooltip("How often to check udonChips.money for changes (seconds)")]
        private int refresh_fast = 3; // Refresh locally and only send when changed
        [SerializeField]
        [Tooltip("How often we retransmit udonChips.money even if the amount hasn't changed")]
        private int refresh_slow = 60; // Send even if it hasn't changed

        [SerializeField]
        [Tooltip("When true the master will be indicated with a star in the list")]
        private bool markMaster = false;

        [SerializeField]
        [Tooltip("When true we also show the player ID for debugging purposes")]
        private bool showPlayerID = false;

        [SerializeField]
        private TextMeshProUGUI textMeshPro = null;

        private UdonChips udonChips = null;

        [UdonSynced] private float newEntry_amount = float.NaN;
        [UdonSynced] private int newEntry_id = -1;

        private float old_amount = float.NaN;

        private int[] entries_id;
        private float[] entries_amount;

        private void Start()
        {
            udonChips = GameObject.Find("UdonChips").GetComponent<UdonChips>();

            if (textMeshPro == null) {
                textMeshPro = GetComponent<TextMeshProUGUI>();
            }

            entries_id = new int[entrylist_length];
            entries_amount = new float[entrylist_length];
            for (int i = 0; i < entrylist_length; ++i) {
                entries_id[i] = -1;
                entries_amount[i] = float.NaN;
            }

            _ClearOldAmount(); // Starts the thread that resets old_amount every refresh_slow seconds (causing things to get sent even if udonChips.money remains unchanged)
            _SendAmount(); // Starts the thread that checks whether udonchips has changed every refresh_fast seconds
        }

        public override void OnDeserialization()
        {
            if (newEntry_filled()) {
                AddEntry();
                UpdateText();
            }
        }

        private bool newEntry_filled()
        {
            return newEntry_id > 0 && !float.IsNaN(newEntry_amount);
        }

        private bool entry_filled(int idx)
        {
            return entries_id[idx] > 0 && !float.IsNaN(entries_amount[idx]);
        }

        private string entry_name(int idx)
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(entries_id[idx]);
            if (player == null)
                return "";
            return player.displayName + ((markMaster && player.isMaster) ? "<color=\"yellow\"><sup>☆</sup></color>" : "");
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (player == null)
                return;

            // Need to remove player from list, while compacting list
            int[] newentries_id = new int[entrylist_length];
            float[] newentries_amount = new float[entrylist_length];
            for (int i = 0; i < entrylist_length; ++i) {
                newentries_id[i] = -1;
                newentries_amount[i] = float.NaN;
            }
            
            int idx = 0;
            for (int i = 0; i < entrylist_length; ++i) {
                if (entries_id[i] != player.playerId) {
                    newentries_id[idx] = entries_id[i];
                    newentries_amount[idx] = entries_amount[i];
                    idx++;
                }
            }
            entries_id = newentries_id;
            entries_amount = newentries_amount;

            UpdateText();

            // Forces everyone to resync soon
            old_amount = float.NaN;
        }

        public override void OnPlayerJoined(VRCPlayerApi player) {
            // Forces everyone to resync soon
            old_amount = float.NaN;
        }

        // less-than comparison so that NaN is always considered smaller and thus always
        // gets sorted towards the end with our reverse sort
        private bool entries_lt(float a, float b)
        {
            if (float.IsNaN(a))
                return true;

            if (float.IsNaN(b))
                return false;

            return a < b;
        }

        // Simple insertion sort with inverted comparison so we sort from largest to smallest
        private void entries_reverseSort()
        {
            int i = 1;
            int length = entrylist_length;
            while (i < length) {
                int x_id = entries_id[i];
                float x_amount = entries_amount[i];
                int j = i - 1;
                while (j >= 0 && entries_lt(entries_amount[j], x_amount)) {
                    entries_id[j+1] = entries_id[j];
                    entries_amount[j+1] = entries_amount[j];
                    j--;
                }
                entries_id[j+1] = x_id;
                entries_amount[j+1] = x_amount;
                i++;
            }
        }

        private void AddEntry()
        {
            if (!newEntry_filled())
                return;

            // Try to either find an existing entry and update it in place, or insert newly in the first non-filled entry.
            // The array is maintained such that filled entries will always come before non-filled entries,
            // thus we first check if we have gone past all filled entries first, since that means we can insert without
            // worrying about matching ids. If we have yet to get to the non-filled entries, we check that the id matches
            // and replace the existing entry in-place instead.
            bool found = false;
            for (int i = 0; i < entrylist_length; ++i) {
                if (!entry_filled(i) || newEntry_id == entries_id[i]) { 
                    entries_id[i] = newEntry_id;
                    entries_amount[i] = newEntry_amount;
                    found = true;
                    break;
                }
            }
            if (!found)
                Debug.LogError("Could not add new entry: Entries array seems to have become full.");

            entries_reverseSort();
        }

        private string formatChips(float amount)
        {
            return string.Format(udonChips.format, amount);
        }

        private void UpdateText()
        {
            string contents = "";
            for (int i = 0; i < entrylist_length && entry_filled(i); ++i) {
                if (showPlayerID)
                    contents += string.Format("{0,2} ", entries_id[i]);

                contents += string.Format("{0,-18}<pos=70%>{1}\n",
                                          entry_name(i), formatChips(entries_amount[i]));
            }

            if (textMeshPro != null) {
                textMeshPro.text = contents;
            }
        }

        public void _SendAmount()
        {
            // Do not update unless udonchips amount has changed
            // + a couple of safety checks to make Udon not whine at us when run in the editor 
            if (udonChips == null || Networking.LocalPlayer == null || old_amount == udonChips.money) {
                SendCustomEventDelayedSeconds(nameof(_SendAmount), refresh_fast);
                return;
            }

            old_amount = udonChips.money;

            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            // Make a new entry for the local player, and first apply it locally, and then serialize it so it ends up on everyone elses list
            newEntry_id = Networking.LocalPlayer.playerId;
            newEntry_amount = udonChips.money;
            AddEntry();
            UpdateText();
            RequestSerialization();

            // Schedule next refresh
            // We simply refresh every refresh_fast seconds, as there is no way to tell exactly when the udonchips
            // value has changed without modifying the basic UdonChips class itself
            SendCustomEventDelayedSeconds(nameof(_SendAmount), refresh_fast);
        }

        // Clears old_amount every refresh_slow seconds. This makes sure that we always retransmit
        // somewhat infrequently in case a message got lost somewhere or so.
        public void _ClearOldAmount()
        {
            old_amount = float.NaN;

            SendCustomEventDelayedSeconds(nameof(_ClearOldAmount), refresh_slow);
        }
    }
}
