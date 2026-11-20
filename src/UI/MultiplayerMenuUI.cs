using System.Collections.Generic;
using ProbablyStolen.Multiplayer.Lobby;
using ProbablyStolen.Multiplayer.Network;
using ProbablyStolen.Multiplayer.Sync;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProbablyStolen.Multiplayer.UI
{
    /// <summary>
    /// Interactive IMGUI-based multiplayer menu rendered on the main screen and in-game.
    /// Provides New Game, Load Game, Join Friend, Lobby Browser, and Steam Invites.
    /// </summary>
    public class MultiplayerMenuUI : MonoBehaviour
    {
        public static MultiplayerMenuUI Instance { get; private set; }

        public bool IsMenuOpen { get; set; } = false;

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
        private bool _isHostPendingNewGame = false;
        private bool _isHostPendingLoadGame = false;

        // Lobby browser state
        private List<LobbyInfo> _cachedLobbies = new List<LobbyInfo>();
        private Vector2 _browserScrollPos = Vector2.zero;
        private bool _isRefreshingLobbies = false;

        // Friend list state
        private Vector2 _friendsScrollPos = Vector2.zero;

        private Rect _windowRect = new Rect(50, 50, 480, 520);
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _mainMenuBtnStyle;
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
                LobbyManager.Instance.OnLobbyCreationResult += HandleLobbyCreationResult;
            }

            if (SteamManager.Initialized)
            {
                _hostLobbyName = $"{SteamFriends.GetPersonaName()}'s Co-op Heist";
            }
        }

        private void OnDestroy()
        {
            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnLobbyListReceived -= HandleLobbyListReceived;
                LobbyManager.Instance.OnLobbyCreationResult -= HandleLobbyCreationResult;
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

            _mainMenuBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            bool isMainMenu = IsInMainMenu();

            // 1. Guaranteed visible header button in upper corner
            GUI.backgroundColor = new Color(0.1f, 0.7f, 1f, 0.95f);
            if (GUI.Button(new Rect(20, 20, 170, 38), IsMenuOpen ? "Close Multiplayer (F1)" : "MULTIPLAYER (F1)", _mainMenuBtnStyle))
            {
                IsMenuOpen = !IsMenuOpen;
            }
            GUI.backgroundColor = Color.white;

            // 2. Large prominent button on the Main Menu screen
            if (isMainMenu)
            {
                float btnWidth = 240f;
                float btnHeight = 48f;
                float btnX = 50f;
                float btnY = Screen.height - 220f;

                GUI.backgroundColor = new Color(0.15f, 0.85f, 0.35f, 0.95f);
                if (GUI.Button(new Rect(btnX, btnY, btnWidth, btnHeight), "MULTIPLAYER CO-OP", _mainMenuBtnStyle))
                {
                    IsMenuOpen = true;
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                // In-game sleek HUD bar
                DrawInGameMultiplayerBar();
            }

            // 2. The Interactive Multiplayer Window
            if (IsMenuOpen)
            {
                GUI.backgroundColor = new Color(0.10f, 0.11f, 0.15f, 0.98f);
                _windowRect = GUI.Window(9901, _windowRect, DrawWindowContent, "PROBABLY STOLEN - MULTIPLAYER CO-OP");
            }
        }

        private bool IsInMainMenu()
        {
            // Checks if MainMenuUIController is active or scene index is 0
            if (MainMenuUIController.Instance != null && MainMenuUIController.Instance.gameObject.activeInHierarchy)
            {
                return true;
            }
            string sceneName = SceneManager.GetActiveScene().name;
            return sceneName.ToLower().Contains("menu") || sceneName.ToLower().Contains("title") || SceneManager.GetActiveScene().buildIndex == 0;
        }

        private void DrawInGameMultiplayerBar()
        {
            float barWidth = 460f;
            float barHeight = 40f;
            float barX = (Screen.width - barWidth) / 2f;
            float barY = 10f;

            GUILayout.BeginArea(new Rect(barX, barY, barWidth, barHeight), "box");
            GUILayout.BeginHorizontal();

            if (LobbyManager.Instance.IsInLobby)
            {
                int count = SteamMatchmaking.GetNumLobbyMembers(LobbyManager.Instance.CurrentLobbyID);
                GUILayout.Label($"<b>Co-op ({count}/4)</b>", GUILayout.Width(75));

                if (ShopSyncManager.Instance != null)
                {
                    int treasury = ShopSyncManager.Instance.SharedTreasury;
                    GUILayout.Label($"<b>Bank:</b> <color=lime>${treasury}</color>", GUILayout.Width(95));

                    int votes = ShopSyncManager.Instance.DayEndVoteCount;
                    string readyTxt = ShopSyncManager.Instance.IsLocalPlayerReadyForDayEnd ? "<color=lime>Ready</color>" : "Vote (F2)";
                    if (GUILayout.Button($"Day: {votes}/{count} [{readyTxt}]", GUILayout.Width(115)))
                    {
                        ShopSyncManager.Instance.ToggleDayEndVote();
                    }
                }

                if (GUILayout.Button("Invite", GUILayout.Width(55)))
                {
                    LobbyManager.Instance.OpenSteamInviteOverlay();
                }
                if (GUILayout.Button("Menu (F1)", GUILayout.Width(70)))
                {
                    IsMenuOpen = !IsMenuOpen;
                }
            }
            else
            {
                if (GUILayout.Button("Host Co-op Session"))
                {
                    LobbyManager.Instance.CreateLobby(true, 4);
                }
                if (GUILayout.Button("Multiplayer Menu (F1)"))
                {
                    IsMenuOpen = !IsMenuOpen;
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawWindowContent(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, 420, 25));

            if (GUI.Button(new Rect(_windowRect.width - 32, 4, 26, 20), "X"))
            {
                IsMenuOpen = false;
            }

            GUILayout.Space(10);
            DrawLobbyStatusBar();
            GUILayout.Space(10);

            // Tab Navigation
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Multiplayer Home", GUILayout.Height(30))) _currentTab = MenuTab.Main;
            if (GUILayout.Button("Lobby Browser", GUILayout.Height(30)))
            {
                _currentTab = MenuTab.LobbyBrowser;
                RefreshLobbies();
            }
            if (GUILayout.Button("Steam Friends", GUILayout.Height(30))) _currentTab = MenuTab.FriendsList;
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
                string role = LobbyManager.Instance.IsLobbyOwner ? "<color=green>HOST (Authoritative)</color>" : "<color=yellow>CLIENT</color>";
                int memberCount = SteamMatchmaking.GetNumLobbyMembers(LobbyManager.Instance.CurrentLobbyID);
                int maxCount = SteamMatchmaking.GetLobbyMemberLimit(LobbyManager.Instance.CurrentLobbyID);

                GUILayout.Label($"<b>Active Session:</b> [{role}] | <b>Players:</b> {memberCount}/{maxCount}", _subHeaderStyle);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Invite Steam Friends"))
                {
                    LobbyManager.Instance.OpenSteamInviteOverlay();
                }
                if (GUILayout.Button("Leave Session", GUILayout.Width(110)))
                {
                    LobbyManager.Instance.LeaveLobby();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("<b>Status:</b> Offline (Solo Mode)", _subHeaderStyle);
            }
            GUILayout.EndVertical();
        }

        private void DrawMainTab()
        {
            GUILayout.Space(5);

            if (!LobbyManager.Instance.IsInLobby)
            {
                // HOST NEW GAME
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f, 1f);
                if (GUILayout.Button("HOST NEW CO-OP HEIST", GUILayout.Height(44)))
                {
                    _isHostPendingNewGame = true;
                    _isHostPendingLoadGame = false;
                    _currentTab = MenuTab.HostSettings;
                }

                GUILayout.Space(8);

                // HOST LOAD GAME
                GUI.backgroundColor = new Color(0.2f, 0.6f, 1f, 1f);
                if (GUILayout.Button("HOST SAVED GAME (LOAD)", GUILayout.Height(44)))
                {
                    _isHostPendingNewGame = false;
                    _isHostPendingLoadGame = true;
                    _currentTab = MenuTab.HostSettings;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.Space(12);

                if (GUILayout.Button("Join Steam Friend", GUILayout.Height(36)))
                {
                    _currentTab = MenuTab.FriendsList;
                }

                GUILayout.Space(5);

                if (GUILayout.Button("Browse Public Lobbies", GUILayout.Height(36)))
                {
                    _currentTab = MenuTab.LobbyBrowser;
                    RefreshLobbies();
                }
            }
            else
            {
                GUILayout.Label("<b>Connected Squad Members:</b>", _subHeaderStyle);
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
                        GUILayout.Label("<color=lime>Host Authority</color>", GUILayout.Width(100));
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(12);

                bool isMenu = IsInMainMenu();
                bool isHostUser = LobbyManager.Instance.IsLobbyOwner;

                if (isMenu)
                {
                    if (isHostUser)
                    {
                        GUI.backgroundColor = new Color(0.2f, 0.9f, 0.35f, 1f);
                        if (GUILayout.Button("LAUNCH SQUAD INTO STORE (InventoryScene)", GUILayout.Height(44)))
                        {
                            LobbyManager.Instance.TransitionSquadToScene("InventoryScene");
                            IsMenuOpen = false;
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    else
                    {
                        GUI.backgroundColor = new Color(0.15f, 0.85f, 1f, 1f);
                        if (GUILayout.Button("ENTER CO-OP STORE (Join Host)", GUILayout.Height(44)))
                        {
                            LobbyManager.Instance.TryJoinHostScene();
                            IsMenuOpen = false;
                        }
                        GUI.backgroundColor = Color.white;
                    }
                }

                GUILayout.Space(8);
                if (GUILayout.Button("Invite Another Friend via Steam", GUILayout.Height(36)))
                {
                    LobbyManager.Instance.OpenSteamInviteOverlay();
                }
            }
        }

        private void DrawHostSettingsTab()
        {
            string modeName = _isHostPendingLoadGame ? "Host Saved Game" : "Host New Game";
            GUILayout.Label($"Configuration: {modeName}", _subHeaderStyle);
            GUILayout.Space(8);

            GUILayout.Label("Session Name:");
            _hostLobbyName = GUILayout.TextField(_hostLobbyName, 32);

            GUILayout.Space(8);
            _hostFriendsOnly = GUILayout.Toggle(_hostFriendsOnly, " Friends Only (Recommended)");

            GUILayout.Space(8);
            GUILayout.Label($"Max Squad Size: {_hostMaxPlayers}");
            _hostMaxPlayers = (int)GUILayout.HorizontalSlider(_hostMaxPlayers, 2, 4);

            GUILayout.Space(20);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Back", GUILayout.Height(40)))
            {
                _currentTab = MenuTab.Main;
            }
            if (GUILayout.Button("Start Lobby & Open Game", GUILayout.Height(40)))
            {
                LobbyManager.Instance.CreateLobby(_hostFriendsOnly, _hostMaxPlayers, _hostLobbyName);
            }
            GUILayout.EndHorizontal();
        }

        private void HandleLobbyCreationResult(bool success, string msg)
        {
            if (!success)
            {
                Debug.LogError($"[MultiplayerUI] Failed to create lobby: {msg}");
                return;
            }

            _currentTab = MenuTab.Main;
            IsMenuOpen = false; // Hide overlay so player interacts with game screen

            // Trigger the native game action
            if (_isHostPendingNewGame)
            {
                _isHostPendingNewGame = false;
                if (MainMenuUIController.Instance != null)
                {
                    Debug.Log("[MultiplayerUI] Starting New Game flow for host...");
                    MainMenuUIController.Instance.OnNewGameClick();
                }
            }
            else if (_isHostPendingLoadGame)
            {
                _isHostPendingLoadGame = false;
                if (MainMenuUIController.Instance != null)
                {
                    Debug.Log("[MultiplayerUI] Opening Load Game menu for host...");
                    MainMenuUIController.Instance.OnLoadGameClick();
                }
            }

            // Pop up Steam friends overlay to invite friends immediately!
            LobbyManager.Instance.OpenSteamInviteOverlay();
        }

        private void DrawLobbyBrowserTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Available Public Heists", _subHeaderStyle);
            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                RefreshLobbies();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            if (_isRefreshingLobbies)
            {
                GUILayout.Label("Searching Steam matchmaking...");
                return;
            }

            if (_cachedLobbies.Count == 0)
            {
                GUILayout.Label("No public sessions found. Host one or invite friends directly!");
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

                if (GUILayout.Button("Join", GUILayout.Width(75), GUILayout.Height(36)))
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
            GUILayout.Label("Steam Friends Playing Probably Stolen", _subHeaderStyle);
            GUILayout.Space(8);

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
                    if (gameInfo.m_gameID.AppID() == SteamUtils.GetAppID())
                    {
                        joinableCount++;
                        GUILayout.BeginHorizontal("box");
                        GUILayout.BeginVertical();
                        GUILayout.Label($"<b>{friendName}</b>");
                        GUILayout.Label(gameInfo.m_steamIDLobby != CSteamID.Nil ? "In Co-op Session" : "In Menu / Solo");
                        GUILayout.EndVertical();

                        if (gameInfo.m_steamIDLobby != CSteamID.Nil)
                        {
                            if (GUILayout.Button("Join", GUILayout.Width(75), GUILayout.Height(32)))
                            {
                                LobbyManager.Instance.JoinLobby(gameInfo.m_steamIDLobby);
                                _currentTab = MenuTab.Main;
                            }
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
