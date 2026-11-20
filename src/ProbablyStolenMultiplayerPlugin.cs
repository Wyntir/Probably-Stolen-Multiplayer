using HarmonyLib;
using ProbablyStolen.Multiplayer.Lobby;
using ProbablyStolen.Multiplayer.Network;
using ProbablyStolen.Multiplayer.Sync;
using ProbablyStolen.Multiplayer.UI;
using UnityEngine;

namespace ProbablyStolen.Multiplayer
{
    /// <summary>
    /// Native mod entry point implementing Probably Stolen's IMod interface.
    /// </summary>
    public class MultiplayerMod : IMod
    {
        public const string MOD_ID = "probably_stolen_p2p_multiplayer";

        private Harmony _harmony;
        private GameObject _networkRoot;

        public void Init(ModManifest manifest)
        {
            Debug.Log("[ProbablyStolen P2P] Initializing P2P Multiplayer Mod...");
        }

        public void OnEnable()
        {
            Debug.Log("[ProbablyStolen P2P] Enabling P2P Multiplayer Mod...");

            if (_networkRoot == null)
            {
                _networkRoot = new GameObject("ProbablyStolen_Multiplayer_NetworkRoot");
                Object.DontDestroyOnLoad(_networkRoot);

                _networkRoot.AddComponent<SteamSocketsTransport>();
                _networkRoot.AddComponent<LobbyManager>();
                _networkRoot.AddComponent<WorldSyncManager>();
                _networkRoot.AddComponent<NetworkManager>();
                _networkRoot.AddComponent<MultiplayerMenuUI>();
            }

            try
            {
                _harmony = new Harmony(MOD_ID);
                _harmony.PatchAll();
                Debug.Log("[ProbablyStolen P2P] Harmony patches applied successfully.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ProbablyStolen P2P] Error applying Harmony patches: {ex}");
            }
        }

        public void OnDisable()
        {
            Debug.Log("[ProbablyStolen P2P] Disabling P2P Multiplayer Mod...");

            _harmony?.UnpatchAll(MOD_ID);
            _harmony = null;

            if (_networkRoot != null)
            {
                Object.Destroy(_networkRoot);
                _networkRoot = null;
            }
        }
    }
}
