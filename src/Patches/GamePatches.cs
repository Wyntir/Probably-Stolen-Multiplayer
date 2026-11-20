using HarmonyLib;
using ProbablyStolen.Multiplayer.Network;
using ProbablyStolen.Multiplayer.Sync;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.Patches
{
    /// <summary>
    /// Harmony patches for Probably Stolen.
    /// Only active on real methods to ensure ModLoader.LoadModDll passes cleanly.
    /// </summary>
    public static class GamePatches
    {
        // SmoothMouseMovement is attached to the player in-game.
        // We use it to ensure PlayerSync is attached to the local player.
        [HarmonyPatch(typeof(SmoothMouseMovement), "Update")]
        public static class PlayerUpdatePatch
        {
            public static void Postfix(SmoothMouseMovement __instance)
            {
                if (__instance.GetComponent<PlayerSync>() == null)
                {
                    Debug.Log("[ProbablyStolen P2P] Attached PlayerSync to active player.");
                    __instance.gameObject.AddComponent<PlayerSync>();
                }
            }
        }
    }
}
