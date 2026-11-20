using System.Collections.Generic;
using ProbablyStolen.Multiplayer.Lobby;
using ProbablyStolen.Multiplayer.Network;
using ProbablyStolen.Multiplayer.Sync;
using Steamworks;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.UI
{
    /// <summary>
    /// Interactive IMGUI-based multiplayer menu supporting:
    /// Host Game, Join Friend, Lobby Browser, and Steam Friend Invites.
    /// Can be toggled using F1 or Esc.
    /// </summary>
    public class MultiplayerMenuUI : MonoBehaviour
    {
        public static MultiplayerMenuUI Instance { get; private set; }

        public bool IsMenuOpen { get; set; } = true;

        private enum MenuTab
        {
            Main,
            HostSettings,
            LobbyBrowser,
            FriendsList
        }

        private MenuTab _currentTab = MenuTab.Main;

        // Host settings state
        private string _hostLobbyName = "Heist Squad";
        private bool _hostFriendsOnly = true;
        private int _hostMaxPlayers = 4;

        // Lobby browser state
        private List<LobbyInfo> _cachedLobbies = new List<LobbyInfo>();
        private Vector2 _browserScrollPos = Vector2.zero;
        private bool _isRefreshingLobbies = false;

        // Friend list state
        private Vector2 _friendsScrollPos = Vector2.zero;

        private Rect _windowRect = new Rect(40, 40, 460, 480);
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private bool _stylesInitialized = false;

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
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnLobbyListReceived += HandleLobbyListReceived;
            }

            if (SteamManager.Initialized)
            {
                _hostLobbyName = $"{SteamFriends.GetPersonaName()}'s Heist";
            }
        }

        private void OnDestroy()
        {
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnLobbyListReceived -= HandleLobbyListReceived;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                IsMenuOpen = !IsMenuOpen;
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _headerStyle.normal.textColor = Color.cyan;

            _subHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            _subHeaderStyle.normal.textColor = Color.white;

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            // Toggle button in upper corner
            if (GUI.Button(new Rect(10, 10, 130, 25), IsMenuOpen ? "Hide MP Menu (F1)" : "Multiplayer (F1)"))
            {
                IsMenuOpen = !IsMenuOpen;
            }

            if (!IsMenuOpen) return;

            GUI.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 0.95f);
            _windowRect = GUI.Window(9901, _windowRect, DrawWindowContent, "PROBABLY STOLEN - MULTIPLAYER");
        }

        private void DrawWindowContent(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, 400, 25));

            GUILayout.Space(5);

            // Active Lobby Status Banner
            DrawLobbyStatusBar();

            GUILayout.Space(10);

            // Tab Navigation
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Main", GUILayout.Height(28))) _currentTab = MenuTab.Main;
            if (GUILayout.Button("Lobby Browser", GUILayout.Height(28)))
            {
                _currentTab = MenuTab.LobbyBrowser;
                RefreshLobbies();
            }
            if (GUILayout.Button("Friends", GUILayout.Height(28))) _currentTab = MenuTab.FriendsList;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            switch (_currentTab)
            {
                case MenuTab.Main:
                    DrawMainTab();
                    break;
                case MenuTab.HostSettings:
                    DrawHostSettingsTab();
                    break;
                case MenuTab.LobbyBrowser:
                    DrawLobbyBrowserTab();
                    break;
                case MenuTab.FriendsList:
                    DrawFriendsTab();
                    break;
            }
        }

        private void DrawLobbyStatusBar()
        {
            GUILayout.BeginVertical("box");
            if (LobbyManager.Instance.IsInLobby)
            {
                string role = LobbyManager.Instance.IsLobbyOwner ? "<color=green>HOST</color>" : "<color=yellow>CLIENT</color>";
                int memberCount = SteamMatchmaking.GetNumLobbyMembers(LobbyManager.Instance.CurrentLobbyID);
                int maxCount = SteamMatchmaking.GetLobbyMemberLimit(LobbyManager.Instance.CurrentLobbyID);

                GUILayout.Label($"<b>Status:</b> Connected [{role}] | <b>Players:</b> {memberCount}/{maxCount}", _subHeaderStyle);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Invite Friend (Steam Overlay)"))
                {
                    LobbyManager.Instance.OpenSteamInviteOverlay();
                }
                if (GUILayout.Button("Leave Lobby", GUILayout.Width(100)))
                {
                    LobbyManager.Instance.LeaveLobby();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("<b>Status:</b> Offline (Not in any lobby)", _subHeaderStyle);
            }
            GUILayout.EndVertical();
        }

        private void DrawMainTab()
        {
            GUILayout.Space(10);

            if (!LobbyManager.Instance.IsInLobby)
            {
                if (GUILayout.Button("Host Game", GUILayout.Height(40)))
                {
                    _currentTab = MenuTab.HostSettings;
                }

                GUILayout.Space(5);

                if (GUILayout.Button("Browse Public Lobbies", GUILayout.Height(40)))
                {
                    _currentTab = MenuTab.LobbyBrowser;
                    RefreshLobbies();
                }

                GUILayout.Space(5);

                if (GUILayout.Button("Join Steam Friend", GUILayout.Height(40)))
                {
                    _currentTab = MenuTab.FriendsList;
                }
            }
            else
            {
                GUILayout.Label("<b>Connected Players in Session:</b>", _subHeaderStyle);
                GUILayout.Space(5);

                int memberCount = SteamMatchmaking.GetNumLobbyMembers(LobbyManager.Instance.CurrentLobbyID);
                for (int i = 0; i < memberCount; i++)
                {
                    CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(LobbyManager.Instance.CurrentLobbyID, i);
                    string name = SteamFriends.GetFriendPersonaName(memberId);
                    bool isHost = SteamMatchmaking.GetLobbyOwner(LobbyManager.Instance.CurrentLobbyID) == memberId;

                    GUILayout.BeginHorizontal("box");
                    GUILayout.Label($"{(isHost ? "[HOST] " : "")}{name}");
                    if (isHost)
                    {
                        GUILayout.Label("<color=lime>Authority</color>", GUILayout.Width(70));
                    }
                    GUILayout.EndHorizontal();
                }
            }
        }

        private void DrawHostSettingsTab()
        {
            GUILayout.Label("Host Lobby Configuration", _subHeaderStyle);
            GUILayout.Space(5);

            GUILayout.Label("Lobby Name:");
            _hostLobbyName = GUILayout.TextField(_hostLobbyName, 32);

            GUILayout.Space(5);
            _hostFriendsOnly = GUILayout.Toggle(_hostFriendsOnly, " Friends Only (Uncheck for Public)");

            GUILayout.Space(5);
            GUILayout.Label($"Max Players: {_hostMaxPlayers}");
            _hostMaxPlayers = (int)GUILayout.HorizontalSlider(_hostMaxPlayers, 2, 4);

            GUILayout.Space(15);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Back", GUILayout.Height(35)))
            {
                _currentTab = MenuTab.Main;
            }
            if (GUILayout.Button("Create & Start Lobby", GUILayout.Height(35)))
            {
                LobbyManager.Instance.CreateLobby(_hostFriendsOnly, _hostMaxPlayers, _hostLobbyName);
                _currentTab = MenuTab.Main;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawLobbyBrowserTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Available Public Lobbies", _subHeaderStyle);
            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                RefreshLobbies();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            if (_isRefreshingLobbies)
            {
                GUILayout.Label("Searching Steam matchmaking...");
                return;
            }

            if (_cachedLobbies.Count == 0)
            {
                GUILayout.Label("No public lobbies found. Try hosting one or inviting friends directly.");
                return;
            }

            _browserScrollPos = GUILayout.BeginScrollView(_browserScrollPos, GUILayout.Height(280));
            foreach (var lobby in _cachedLobbies)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.BeginVertical();
                GUILayout.Label($"<b>{lobby.LobbyName}</b> (v{lobby.GameVersion})");
                GUILayout.Label($"Players: {lobby.CurrentMembers}/{lobby.MaxMembers}");
                GUILayout.EndVertical();

                if (GUILayout.Button("Join", GUILayout.Width(70), GUILayout.Height(35)))
                {
                    LobbyManager.Instance.JoinLobby(lobby.LobbyId);
                    _currentTab = MenuTab.Main;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
            GUILayout.EndScrollView();
        }

        private void DrawFriendsTab()
        {
            GUILayout.Label("Steam Friends Playing", _subHeaderStyle);
            GUILayout.Space(5);

            if (!SteamManager.Initialized)
            {
                GUILayout.Label("Steam not initialized.");
                return;
            }

            int friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            _friendsScrollPos = GUILayout.BeginScrollView(_friendsScrollPos, GUILayout.Height(300));

            int joinableCount = 0;
            for (int i = 0; i < friendCount; i++)
            {
                CSteamID friendId = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                string friendName = SteamFriends.GetFriendPersonaName(friendId);

                FriendGameInfo_t gameInfo;
                if (SteamFriends.GetFriendGamePlayed(friendId, out gameInfo))
                {
                    // Check if friend is in same AppID
                    if (gameInfo.m_gameID.AppID() == SteamUtils.GetAppID())
                    {
                        joinableCount++;
                        GUILayout.BeginHorizontal("box");
                        GUILayout.BeginVertical();
                        GUILayout.Label($"<b>{friendName}</b>");
                        GUILayout.Label($"Playing Probably Stolen");
                        GUILayout.EndVertical();

                        if (gameInfo.m_steamIDLobby != CSteamID.Nil)
                        {
                            if (GUILayout.Button("Join Game", GUILayout.Width(85), GUILayout.Height(30)))
                            {
                                LobbyManager.Instance.JoinLobby(gameInfo.m_steamIDLobby);
                                _currentTab = MenuTab.Main;
                            }
                        }
                        else
                        {
                            GUILayout.Label("(In Menu / Solo)", GUILayout.Width(100));
                        }
                        GUILayout.EndHorizontal();
                    }
                }
            }

            if (joinableCount == 0)
            {
                GUILayout.Label("None of your online friends are currently in a joinable session.");
            }

            GUILayout.EndScrollView();
        }

        private void RefreshLobbies()
        {
            _isRefreshingLobbies = true;
            LobbyManager.Instance.RequestLobbyList();
        }

        private void HandleLobbyListReceived(List<LobbyInfo> lobbies)
        {
            _cachedLobbies = lobbies;
            _isRefreshingLobbies = false;
        }
    }
}
