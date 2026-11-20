using System;
using System.Collections.Generic;
using ProbablyStolen.Multiplayer.Lobby;
using ProbablyStolen.Multiplayer.Network;
using ProbablyStolen.Multiplayer.Serialization;
using Steamworks;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.Sync
{
    /// <summary>
    /// Central Network Orchestrator. Dispatches packets, manages player spawning,
    /// connects transport callbacks to game state.
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        [Header("Prefabs")]
        [SerializeField] private GameObject remotePlayerPrefab;

        // Active remote players: SteamID -> NetworkPlayer instance
        private readonly Dictionary<CSteamID, NetworkPlayer> _remotePlayers = new Dictionary<CSteamID, NetworkPlayer>();

        public IReadOnlyDictionary<CSteamID, NetworkPlayer> RemotePlayers => _remotePlayers;

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
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

            if (SteamSocketsTransport.Instance != null)
            {
                SteamSocketsTransport.Instance.OnPeerConnected += HandlePeerConnected;
                SteamSocketsTransport.Instance.OnPeerDisconnected += HandlePeerDisconnected;
                SteamSocketsTransport.Instance.OnDataReceived += HandleDataReceived;
            }

            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnLobbyLeft += HandleLobbyLeft;
            }
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

            if (SteamSocketsTransport.Instance != null)
            {
                SteamSocketsTransport.Instance.OnPeerConnected -= HandlePeerConnected;
                SteamSocketsTransport.Instance.OnPeerDisconnected -= HandlePeerDisconnected;
                SteamSocketsTransport.Instance.OnDataReceived -= HandleDataReceived;
            }

            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnLobbyLeft -= HandleLobbyLeft;
            }
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Debug.Log($"[NetworkManager] Scene loaded: {scene.name}");
            if (LobbyManager.Instance != null && LobbyManager.Instance.IsLobbyOwner)
            {
                LobbyManager.Instance.UpdateHostScene(scene.name);
            }
        }

        private void HandlePeerConnected(CSteamID peerSteamID)
        {
            Debug.Log($"[NetworkManager] Peer connected: {peerSteamID}");

            // Spawn remote player representation
            SpawnRemotePlayer(peerSteamID);

            if (SteamSocketsTransport.Instance.IsHost)
            {
                // Host sends the full current world snapshot to late joiner
                WorldSyncManager.Instance.SendWorldSnapshotToPeer(peerSteamID);
            }
            else
            {
                // Client requests snapshot from Host
                using (PacketWriter writer = new PacketWriter(PacketType.WorldSnapshotRequest))
                {
                    SteamSocketsTransport.Instance.SendData(peerSteamID, writer.ToArray(), reliable: true);
                }
            }
        }

        private void HandlePeerDisconnected(CSteamID peerSteamID)
        {
            Debug.Log($"[NetworkManager] Peer disconnected: {peerSteamID}");
            DespawnRemotePlayer(peerSteamID);
        }

        private void HandleLobbyLeft()
        {
            // Destroy all remote players
            foreach (var kvp in _remotePlayers)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value.gameObject);
                }
            }
            _remotePlayers.Clear();
        }

        private void SpawnRemotePlayer(CSteamID steamID)
        {
            if (_remotePlayers.ContainsKey(steamID)) return;

            GameObject playerObj;
            if (remotePlayerPrefab != null)
            {
                playerObj = Instantiate(remotePlayerPrefab);
            }
            else
            {
                // Fallback default avatar if prefab not bound
                playerObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Collider col = playerObj.GetComponent<Collider>();
                if (col != null) col.isTrigger = true; // Don't block local player physics
            }

            NetworkPlayer netPlayer = playerObj.GetComponent<NetworkPlayer>();
            if (netPlayer == null)
            {
                netPlayer = playerObj.AddComponent<NetworkPlayer>();
            }

            netPlayer.Initialize(steamID);
            _remotePlayers[steamID] = netPlayer;
        }

        private void DespawnRemotePlayer(CSteamID steamID)
        {
            if (_remotePlayers.TryGetValue(steamID, out NetworkPlayer netPlayer))
            {
                if (netPlayer != null)
                {
                    Destroy(netPlayer.gameObject);
                }
                _remotePlayers.Remove(steamID);
            }
        }

        private void HandleDataReceived(CSteamID senderSteamID, byte[] data)
        {
            if (data == null || data.Length == 0) return;

            using (PacketReader reader = new PacketReader(data, 0, data.Length))
            {
                switch (reader.Type)
                {
                    case PacketType.PlayerStateUpdate:
                        HandlePlayerStateUpdate(senderSteamID, reader);
                        break;

                    case PacketType.PlayerAnimationTrigger:
                        HandlePlayerAnimationTrigger(senderSteamID, reader);
                        break;

                    case PacketType.WorldSnapshotRequest:
                        if (SteamSocketsTransport.Instance.IsHost)
                        {
                            WorldSyncManager.Instance.SendWorldSnapshotToPeer(senderSteamID);
                        }
                        break;

                    case PacketType.WorldSnapshotFull:
                        if (!SteamSocketsTransport.Instance.IsHost)
                        {
                            WorldSyncManager.Instance.ApplyWorldSnapshot(reader);
                        }
                        break;

                    case PacketType.ItemStolenEvent:
                        HandleItemStolenPacket(senderSteamID, reader);
                        break;

                    case PacketType.DoorStateToggle:
                        HandleDoorTogglePacket(senderSteamID, reader);
                        break;

                    case PacketType.AlarmStateChanged:
                        int alarmLevel = reader.ReadInt();
                        WorldSyncManager.Instance.ApplyAlarmLocally(alarmLevel);
                        break;

                    case PacketType.SceneLoadBroadcast:
                        string targetScene = reader.ReadString();
                        Debug.Log($"[NetworkManager] Received SceneLoadBroadcast: {targetScene}");
                        if (!SteamSocketsTransport.Instance.IsHost)
                        {
                            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != targetScene)
                            {
                                if (NewGameData.Instance != null) NewGameData.Instance.skipIntro = true;
                                UnityEngine.SceneManagement.SceneManager.LoadScene(targetScene);
                            }
                        }
                        break;

                    case PacketType.CustomerLockRequest:
                        if (SteamSocketsTransport.Instance.IsHost && ShopSyncManager.Instance != null)
                        {
                            int cId = reader.ReadInt();
                            ulong claimant = reader.ReadULong();
                            ShopSyncManager.Instance.AuthorizeCustomerLock(cId, claimant);
                        }
                        break;

                    case PacketType.CustomerLockBroadcast:
                        if (ShopSyncManager.Instance != null)
                        {
                            int lockedCId = reader.ReadInt();
                            ulong lockOwner = reader.ReadULong();
                            ShopSyncManager.Instance.ApplyCustomerLockLocally(lockedCId, lockOwner);
                        }
                        break;

                    case PacketType.CustomerUnlockBroadcast:
                        if (ShopSyncManager.Instance != null)
                        {
                            int unlockedCId = reader.ReadInt();
                            ShopSyncManager.Instance.ApplyCustomerUnlockLocally(unlockedCId);
                        }
                        break;

                    case PacketType.ItemGrabRequest:
                        if (SteamSocketsTransport.Instance.IsHost && ShopSyncManager.Instance != null)
                        {
                            int grabItemId = reader.ReadInt();
                            ulong grabber = reader.ReadULong();
                            ShopSyncManager.Instance.AuthorizeGrabItem(grabItemId, grabber);
                        }
                        break;

                    case PacketType.ItemGrabBroadcast:
                        if (ShopSyncManager.Instance != null)
                        {
                            int gItemId = reader.ReadInt();
                            ulong holderId = reader.ReadULong();
                            ShopSyncManager.Instance.ApplyItemGrabLocally(gItemId, holderId);
                        }
                        break;

                    case PacketType.ItemDropBroadcast:
                        if (ShopSyncManager.Instance != null)
                        {
                            int dropItemId = reader.ReadInt();
                            Vector3 dropPos = reader.ReadVector3();
                            ShopSyncManager.Instance.ApplyItemDropLocally(dropItemId, dropPos);
                        }
                        break;

                    case PacketType.TreasuryUpdate:
                        if (ShopSyncManager.Instance != null)
                        {
                            int totalTreasury = reader.ReadInt();
                            ShopSyncManager.Instance.ApplyTreasuryLocally(totalTreasury);
                        }
                        break;

                    case PacketType.TreasuryDelta:
                        if (SteamSocketsTransport.Instance.IsHost && ShopSyncManager.Instance != null)
                        {
                            int delta = reader.ReadInt();
                            ShopSyncManager.Instance.HandleTreasuryDeltaFromClient(delta);
                        }
                        break;

                    case PacketType.DayEndVoteRequest:
                        if (SteamSocketsTransport.Instance.IsHost && ShopSyncManager.Instance != null)
                        {
                            ulong voter = reader.ReadULong();
                            bool isReady = reader.ReadBool();
                            ShopSyncManager.Instance.RegisterDayEndVote(voter, isReady);
                        }
                        break;

                    case PacketType.DayEndVoteBroadcast:
                        if (ShopSyncManager.Instance != null)
                        {
                            int readyCount = reader.ReadInt();
                            int totalCount = reader.ReadInt();
                            ShopSyncManager.Instance.ApplyVoteTallyLocally(readyCount, totalCount);
                        }
                        break;

                    case PacketType.DayEndExecute:
                        if (ShopSyncManager.Instance != null)
                        {
                            ShopSyncManager.Instance.ExecuteDayEndLocally();
                        }
                        break;

                    default:
                        Debug.LogWarning($"[NetworkManager] Unhandled packet type: {reader.Type} from {senderSteamID}");
                        break;
                }
            }
        }

        private void HandlePlayerStateUpdate(CSteamID senderSteamID, PacketReader reader)
        {
            Vector3 pos = reader.ReadVector3();
            float yaw = reader.ReadByteYaw();
            PlayerSyncFlags flags = (PlayerSyncFlags)reader.ReadByte();

            if (_remotePlayers.TryGetValue(senderSteamID, out NetworkPlayer netPlayer))
            {
                netPlayer.ApplyStateUpdate(pos, yaw, flags);
            }

            // If we are Host, relay movement to other clients so everyone sees each other (Star topology)
            if (SteamSocketsTransport.Instance.IsHost)
            {
                using (PacketWriter relayWriter = new PacketWriter(PacketType.PlayerStateUpdate))
                {
                    relayWriter.WriteVector3(pos);
                    relayWriter.WriteByteYaw(yaw);
                    relayWriter.WriteByte((byte)flags);
                    // Broadcast to everyone except the original sender
                    SteamSocketsTransport.Instance.Broadcast(relayWriter.ToArray(), reliable: false, excludeSteamID: senderSteamID);
                }
            }
        }

        private void HandlePlayerAnimationTrigger(CSteamID senderSteamID, PacketReader reader)
        {
            string trigger = reader.ReadString();
            if (_remotePlayers.TryGetValue(senderSteamID, out NetworkPlayer netPlayer))
            {
                netPlayer.TriggerAnimation(trigger);
            }

            if (SteamSocketsTransport.Instance.IsHost)
            {
                using (PacketWriter relayWriter = new PacketWriter(PacketType.PlayerAnimationTrigger))
                {
                    relayWriter.WriteString(trigger);
                    SteamSocketsTransport.Instance.Broadcast(relayWriter.ToArray(), reliable: true, excludeSteamID: senderSteamID);
                }
            }
        }

        private void HandleItemStolenPacket(CSteamID senderSteamID, PacketReader reader)
        {
            int itemId = reader.ReadInt();
            CSteamID stealerSteamId = reader.ReadSteamID();

            if (SteamSocketsTransport.Instance.IsHost)
            {
                WorldSyncManager.Instance.AuthorizeItemSteal(itemId, stealerSteamId);
            }
            else
            {
                WorldSyncManager.Instance.ApplyItemStolenLocally(itemId, stealerSteamId);
            }
        }

        private void HandleDoorTogglePacket(CSteamID senderSteamID, PacketReader reader)
        {
            int doorId = reader.ReadInt();

            if (SteamSocketsTransport.Instance.IsHost)
            {
                WorldSyncManager.Instance.AuthorizeToggleDoor(doorId);
            }
            else
            {
                bool isOpen = reader.ReadBool();
                WorldSyncManager.Instance.ApplyDoorToggleLocally(doorId, isOpen);
            }
        }
    }
}
