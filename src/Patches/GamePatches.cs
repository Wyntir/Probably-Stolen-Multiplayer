using HarmonyLib;
using ProbablyStolen.Multiplayer.Network;
using ProbablyStolen.Multiplayer.Sync;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.Patches
{
    /// <summary>
    /// Demonstrates minimal-invasion Harmony patches for 'Probably Stolen'.
    /// Intercepts local interactions and injects network synchronization.
    /// </summary>
    public static class GamePatches
    {
        // -------------------------------------------------------------
        // 1. LOCAL PLAYER SPAWN PATCH
        // When the game instantiates or initializes the local player,
        // attach the PlayerSync component.
        // -------------------------------------------------------------
        [HarmonyPatch(typeof(CharacterController), "Start")] // or PlayerController.Start
        public static class PlayerSpawnPatch
        {
            public static void Postfix(CharacterController __instance)
            {
                // Ensure this is the local controlled player and not an NPC
                if (__instance.GetComponent<PlayerSync>() == null && __instance.CompareTag("Player"))
                {
                    Debug.Log("[Multiplayer Mod] Attached PlayerSync to local player character.");
                    __instance.gameObject.AddComponent<PlayerSync>();
                }
            }
        }

        // -------------------------------------------------------------
        // 2. ITEM STEAL / PICKUP PATCH
        // Intercepts item stealing. If multiplayer is active, redirects
        // the action to the Host-Authoritative WorldSyncManager.
        // -------------------------------------------------------------
        // Target simulated game method: ItemPickup.TryStealItem(int itemId)
        /*
        [HarmonyPatch(typeof(ItemPickup), "TryStealItem")]
        public static class ItemPickupPatch
        {
            public static bool Prefix(int itemId, ref bool __result)
            {
                // If in single-player or not connected, allow native game code to run
                if (!SteamSocketsTransport.Instance.IsConnected)
                {
                    return true; // continue original method
                }

                // In multiplayer, route steal request to WorldSyncManager
                WorldSyncManager.Instance.RequestStealItem(itemId);

                // Prevent immediate local pickup until Host authorizes
                __result = false;
                return false; // skip original method
            }
        }
        */

        // -------------------------------------------------------------
        // 3. DOOR INTERACTION PATCH
        // Intercepts door open/close toggle to ensure synchrony.
        // -------------------------------------------------------------
        /*
        [HarmonyPatch(typeof(DoorInteractable), "ToggleDoor")]
        public static class DoorInteractablePatch
        {
            public static bool Prefix(int doorId)
            {
                if (!SteamSocketsTransport.Instance.IsConnected)
                {
                    return true;
                }

                WorldSyncManager.Instance.RequestToggleDoor(doorId);
                return false;
            }
        }
        */
    }
}
