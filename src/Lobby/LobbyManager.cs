using System;
using System.Collections.Generic;
using ProbablyStolen.Multiplayer.Network;
using Steamworks;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.Lobby
{
    public struct LobbyInfo
    {
        public CSteamID LobbyId;
        public string LobbyName;
        public int CurrentMembers;
        public int MaxMembers;
        public string GameVersion;
        public CSteamID HostSteamId;
    }

    /// <summary>
    /// Manages Steam Lobbies, friend invites, lobby searches, and coordinates with SteamSocketsTransport.
    /// </summary>
    public class LobbyManager : MonoBehaviour
    {
        public static LobbyManager Instance { get; private set; }

        public const string LOBBY_KEY_HOST_STEAMID = "HostSteamID";
        public const string LOBBY_KEY_NAME = "Name";
        public const string LOBBY_KEY_VERSION = "Version";
        public const string LOBBY_KEY_MAP = "Map";
        public const string CURRENT_MOD_VERSION = "1.0.0";

        public CSteamID CurrentLobbyID { get; private set; } = CSteamID.Nil;
        public bool IsInLobby => CurrentLobbyID != CSteamID.Nil;
        public bool IsLobbyOwner => IsInLobby && SteamMatchmaking.GetLobbyOwner(CurrentLobbyID) == SteamUser.GetSteamID();

        // Steam Callbacks
        private CallResult<LobbyCreated_t> _lobbyCreatedCallResult;
        private CallResult<LobbyEnter_t> _lobbyEnterCallResult;
        private CallResult<LobbyMatchList_t> _lobbyListCallResult;
        private Callback<GameLobbyJoinRequested_t> _lobbyJoinRequestedCallback;
        private Callback<LobbyChatUpdate_t> _lobbyChatUpdateCallback;

        // Events
        public event Action<bool, string> OnLobbyCreationResult;
        public event Action<bool, string> OnLobbyJoinResult;
        public event Action<List<LobbyInfo>> OnLobbyListReceived;
        public event Action OnLobbyLeft;
        public event Action<CSteamID> OnPlayerJoinedLobby;
        public event Action<CSteamID> OnPlayerLeftLobby;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogWarning("[LobbyManager] Steamworks is not initialized!");
                return;
            }

            _lobbyCreatedCallResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEnterCallResult = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            _lobbyListCallResult = CallResult<LobbyMatchList_t>.Create(OnLobbyListReceivedCallback);
            _lobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            _lobbyChatUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);

            // Check if game was launched via Steam friend invite (+connect_lobby <id>)
            CheckCommandLineLaunchInvite();
        }

        /// <summary>
        /// Host a new Steam Lobby.
        /// </summary>
        public void CreateLobby(bool friendsOnly = true, int maxMembers = 4, string lobbyName = null)
        {
            if (IsInLobby)
            {
                LeaveLobby();
            }

            ELobbyType lobbyType = friendsOnly ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePublic;
            SteamAPICall_t handle = SteamMatchmaking.CreateLobby(lobbyType, maxMembers);
            _lobbyCreatedCallResult.Set(handle);

            if (string.IsNullOrEmpty(lobbyName))
            {
                lobbyName = $"{SteamFriends.GetPersonaName()}'s Heist";
            }
            PlayerPrefs.SetString("LastLobbyName", lobbyName);

            Debug.Log($"[LobbyManager] Requesting lobby creation (type: {lobbyType}, max: {maxMembers})...");
        }

        private void OnLobbyCreated(LobbyCreated_t param, bool bioFailure)
        {
            if (bioFailure || param.m_eResult != EResult.k_EResultOK)
            {
                Debug.LogError($"[LobbyManager] Lobby creation failed: {param.m_eResult}");
                OnLobbyCreationResult?.Invoke(false, param.m_eResult.ToString());
                return;
            }

            CurrentLobbyID = new CSteamID(param.m_ulSteamIDLobby);
            Debug.Log($"[LobbyManager] Lobby created successfully! ID: {CurrentLobbyID}");

            // Set metadata on the lobby
            string lobbyName = PlayerPrefs.GetString("LastLobbyName", $"{SteamFriends.GetPersonaName()}'s Game");
            CSteamID mySteamID = SteamUser.GetSteamID();

            SteamMatchmaking.SetLobbyData(CurrentLobbyID, LOBBY_KEY_HOST_STEAMID, mySteamID.m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(CurrentLobbyID, LOBBY_KEY_NAME, lobbyName);
            SteamMatchmaking.SetLobbyData(CurrentLobbyID, LOBBY_KEY_VERSION, CURRENT_MOD_VERSION);

            // Set Steam Rich Presence
            SteamFriends.SetRichPresence("status", "Hosting Heist");
            SteamFriends.SetRichPresence("connect", $"+connect_lobby {CurrentLobbyID.m_SteamID}");

            // Initialize host transport
            SteamSocketsTransport.Instance.StartHost();

            OnLobbyCreationResult?.Invoke(true, "Lobby created.");
        }

        /// <summary>
        /// Join a lobby by Steam ID.
        /// </summary>
        public void JoinLobby(CSteamID lobbyID)
        {
            if (IsInLobby)
            {
                LeaveLobby();
            }

            Debug.Log($"[LobbyManager] Joining lobby {lobbyID}...");
            SteamAPICall_t handle = SteamMatchmaking.JoinLobby(lobbyID);
            _lobbyEnterCallResult.Set(handle);
        }

        private void OnLobbyEntered(LobbyEnter_t param, bool bioFailure)
        {
            if (bioFailure || param.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Debug.LogError($"[LobbyManager] Failed to enter lobby: {param.m_EChatRoomEnterResponse}");
                OnLobbyJoinResult?.Invoke(false, $"Error code: {param.m_EChatRoomEnterResponse}");
                return;
            }

            CurrentLobbyID = new CSteamID(param.m_ulSteamIDLobby);
            Debug.Log($"[LobbyManager] Entered lobby {CurrentLobbyID}");

            // Check if we are host or client
            CSteamID ownerID = SteamMatchmaking.GetLobbyOwner(CurrentLobbyID);
            CSteamID myID = SteamUser.GetSteamID();

            if (ownerID != myID)
            {
                // We are client, connect to host via SteamSocketsTransport
                string hostIdStr = SteamMatchmaking.GetLobbyData(CurrentLobbyID, LOBBY_KEY_HOST_STEAMID);
                if (ulong.TryParse(hostIdStr, out ulong hostSteamIdUlong) && hostSteamIdUlong != 0)
                {
                    CSteamID hostSteamID = new CSteamID(hostSteamIdUlong);
                    SteamSocketsTransport.Instance.ConnectToHost(hostSteamID);
                }
                else
                {
                    Debug.LogError("[LobbyManager] Host SteamID not found in lobby metadata!");
                }
            }

            SteamFriends.SetRichPresence("status", "Playing Co-op Heist");
            OnLobbyJoinResult?.Invoke(true, "Joined lobby successfully.");
        }

        /// <summary>
        /// Refresh the list of public lobbies matching game version.
        /// </summary>
        public void RequestLobbyList()
        {
            SteamMatchmaking.AddRequestLobbyListStringFilter(LOBBY_KEY_VERSION, CURRENT_MOD_VERSION, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(20);
            SteamAPICall_t handle = SteamMatchmaking.RequestLobbyList();
            _lobbyListCallResult.Set(handle);
        }

        private void OnLobbyListReceivedCallback(LobbyMatchList_t param, bool bioFailure)
        {
            List<LobbyInfo> results = new List<LobbyInfo>();
            if (bioFailure)
            {
                Debug.LogError("[LobbyManager] Failed to fetch lobby list.");
                OnLobbyListReceived?.Invoke(results);
                return;
            }

            for (int i = 0; i < param.m_nLobbiesMatching; i++)
            {
                CSteamID lobbyID = SteamMatchmaking.GetLobbyByIndex(i);
                string name = SteamMatchmaking.GetLobbyData(lobbyID, LOBBY_KEY_NAME);
                string version = SteamMatchmaking.GetLobbyData(lobbyID, LOBBY_KEY_VERSION);
                string hostStr = SteamMatchmaking.GetLobbyData(lobbyID, LOBBY_KEY_HOST_STEAMID);
                ulong.TryParse(hostStr, out ulong hostUlong);

                results.Add(new LobbyInfo
                {
                    LobbyId = lobbyID,
                    LobbyName = string.IsNullOrEmpty(name) ? "Unnamed Lobby" : name,
                    CurrentMembers = SteamMatchmaking.GetNumLobbyMembers(lobbyID),
                    MaxMembers = SteamMatchmaking.GetLobbyMemberLimit(lobbyID),
                    GameVersion = version,
                    HostSteamId = new CSteamID(hostUlong)
                });
            }

            OnLobbyListReceived?.Invoke(results);
        }

        /// <summary>
        /// Invoked when a player accepts an invite through the Steam chat or overlay.
        /// </summary>
        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t param)
        {
            Debug.Log($"[LobbyManager] Join requested from friend: {param.m_steamIDFriend}, Lobby: {param.m_steamIDLobby}");
            JoinLobby(param.m_steamIDLobby);
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t param)
        {
            CSteamID changerID = new CSteamID(param.m_ulSteamIDUserChanged);
            EChatMemberStateChange stateChange = (EChatMemberStateChange)param.m_rgfChatMemberStateChange;

            if (stateChange.HasFlag(EChatMemberStateChange.k_EChatMemberStateChangeEntered))
            {
                Debug.Log($"[LobbyManager] Member entered: {changerID}");
                OnPlayerJoinedLobby?.Invoke(changerID);
            }
            else if (stateChange.HasFlag(EChatMemberStateChange.k_EChatMemberStateChangeLeft) ||
                     stateChange.HasFlag(EChatMemberStateChange.k_EChatMemberStateChangeDisconnected) ||
                     stateChange.HasFlag(EChatMemberStateChange.k_EChatMemberStateChangeKicked))
            {
                Debug.Log($"[LobbyManager] Member left: {changerID}");
                OnPlayerLeftLobby?.Invoke(changerID);
            }
        }

        /// <summary>
        /// Open native Steam friend invite modal dialog.
        /// </summary>
        public void OpenSteamInviteOverlay()
        {
            if (!IsInLobby)
            {
                Debug.LogWarning("[LobbyManager] Cannot invite: not in a lobby.");
                return;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobbyID);
        }

        public void LeaveLobby()
        {
            if (!IsInLobby) return;

            SteamMatchmaking.LeaveLobby(CurrentLobbyID);
            CurrentLobbyID = CSteamID.Nil;
            SteamSocketsTransport.Instance.Shutdown();
            SteamFriends.ClearRichPresence();

            OnLobbyLeft?.Invoke();
            Debug.Log("[LobbyManager] Left lobby.");
        }

        private void CheckCommandLineLaunchInvite()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "+connect_lobby" && i + 1 < args.Length)
                {
                    if (ulong.TryParse(args[i + 1], out ulong lobbyId))
                    {
                        Debug.Log($"[LobbyManager] Detected launch argument +connect_lobby {lobbyId}");
                        JoinLobby(new CSteamID(lobbyId));
                    }
                    break;
                }
            }
        }

        private void OnDestroy()
        {
            LeaveLobby();
        }
    }
}
