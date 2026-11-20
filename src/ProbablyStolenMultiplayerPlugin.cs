using BepInEx;
using HarmonyLib;
using ProbablyStolen.Multiplayer.Lobby;
using ProbablyStolen.Multiplayer.Network;
using ProbablyStolen.Multiplayer.Sync;
using ProbablyStolen.Multiplayer.UI;
using UnityEngine;

namespace ProbablyStolen.Multiplayer
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class ProbablyStolenMultiplayerPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.probablystolen.multiplayer";
        public const string PLUGIN_NAME = "Probably Stolen Multiplayer P2P";
        public const string PLUGIN_VERSION = "1.0.0";

        private Harmony _harmony;

        private void Awake()
        {
            Logger.LogInfo($"[Multiplayer Mod] Initializing {PLUGIN_NAME} v{PLUGIN_VERSION}...");

            // Create network manager hierarchy
            GameObject netRoot = new GameObject("ProbablyStolen_Multiplayer_NetworkRoot");
            DontDestroyOnLoad(netRoot);

            netRoot.AddComponent<SteamSocketsTransport>();
            netRoot.AddComponent<LobbyManager>();
            netRoot.AddComponent<WorldSyncManager>();
            netRoot.AddComponent<NetworkManager>();
            netRoot.AddComponent<MultiplayerMenuUI>();

            // Apply Harmony patches
            _harmony = new Harmony(PLUGIN_GUID);
            _harmony.PatchAll();

            Logger.LogInfo("[Multiplayer Mod] Mod loaded and hooks initialized successfully.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
