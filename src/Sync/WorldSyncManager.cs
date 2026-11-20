using System;
using System.Collections.Generic;
using ProbablyStolen.Multiplayer.Network;
using ProbablyStolen.Multiplayer.Serialization;
using Steamworks;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.Sync
{
    public struct ItemState
    {
        public int ItemId;
        public bool IsStolen;
        public ulong StolenBySteamId;
    }

    public struct DoorState
    {
        public int DoorId;
        public bool IsOpen;
        public bool IsLocked;
    }

    /// <summary>
    /// Manages host-authoritative world synchronization, delta events,
    /// and full world snapshots for late-joining players.
    /// </summary>
    public class WorldSyncManager : MonoBehaviour
    {
        public static WorldSyncManager Instance { get; private set; }

        // Authoritative state tables
        private readonly Dictionary<int, ItemState> _items = new Dictionary<int, ItemState>();
        private readonly Dictionary<int, DoorState> _doors = new Dictionary<int, DoorState>();

        public int AlarmLevel { get; private set; } = 0; // 0 = Clear, 1 = Suspicion, 2 = Lockdown
        public float HeistTimeRemaining { get; private set; } = 300f; // 5 min default
        public int TotalLootValue { get; private set; } = 0;

        // Events
        public event Action<int, CSteamID> OnItemStolen;
        public event Action<int, bool> OnDoorToggled;
        public event Action<int> OnAlarmChanged;
        public event Action OnWorldSnapshotApplied;

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

        #region Registration & Initial Scene Setup

        public void RegisterItem(int itemId)
        {
            if (!_items.ContainsKey(itemId))
            {
                _items[itemId] = new ItemState { ItemId = itemId, IsStolen = false, StolenBySteamId = 0 };
            }
        }

        public void RegisterDoor(int doorId, bool initialOpen = false, bool initialLocked = false)
        {
            if (!_doors.ContainsKey(doorId))
            {
                _doors[doorId] = new DoorState { DoorId = doorId, IsOpen = initialOpen, IsLocked = initialLocked };
            }
        }

        #endregion

        #region Host Authority & Delta Events

        /// <summary>
        /// Request stealing an item. If caller is client, forwards request to Host.
        /// If caller is Host, validates and executes.
        /// </summary>
        public void RequestStealItem(int itemId)
        {
            if (SteamSocketsTransport.Instance.IsHost)
            {
                AuthorizeItemSteal(itemId, SteamUser.GetSteamID());
            }
            else
            {
                // Send request to Host
                using (PacketWriter writer = new PacketWriter(PacketType.ItemStolenEvent))
                {
                    writer.WriteInt(itemId);
                    writer.WriteSteamID(SteamUser.GetSteamID());
                    SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                }
            }
        }

        public void AuthorizeItemSteal(int itemId, CSteamID stealerSteamId)
        {
            if (!SteamSocketsTransport.Instance.IsHost) return;

            if (_items.TryGetValue(itemId, out ItemState state))
            {
                if (state.IsStolen)
                {
                    Debug.LogWarning($"[WorldSync] Item {itemId} was already stolen! Denying request.");
                    return;
                }

                // Update authoritative state
                state.IsStolen = true;
                state.StolenBySteamId = stealerSteamId.m_SteamID;
                _items[itemId] = state;

                // Broadcast confirmed delta event to all clients
                using (PacketWriter writer = new PacketWriter(PacketType.ItemStolenEvent))
                {
                    writer.WriteInt(itemId);
                    writer.WriteSteamID(stealerSteamId);
                    SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                }

                // Apply locally on host
                ApplyItemStolenLocally(itemId, stealerSteamId);
            }
        }

        public void ApplyItemStolenLocally(int itemId, CSteamID stealerSteamId)
        {
            _items[itemId] = new ItemState { ItemId = itemId, IsStolen = true, StolenBySteamId = stealerSteamId.m_SteamID };
            OnItemStolen?.Invoke(itemId, stealerSteamId);
            Debug.Log($"[WorldSync] Item {itemId} marked as stolen by {stealerSteamId}");
        }

        public void RequestToggleDoor(int doorId)
        {
            if (SteamSocketsTransport.Instance.IsHost)
            {
                AuthorizeToggleDoor(doorId);
            }
            else
            {
                using (PacketWriter writer = new PacketWriter(PacketType.DoorStateToggle))
                {
                    writer.WriteInt(doorId);
                    SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                }
            }
        }

        public void AuthorizeToggleDoor(int doorId)
        {
            if (!SteamSocketsTransport.Instance.IsHost) return;

            if (_doors.TryGetValue(doorId, out DoorState state))
            {
                if (state.IsLocked)
                {
                    Debug.LogWarning($"[WorldSync] Door {doorId} is locked!");
                    return;
                }

                state.IsOpen = !state.IsOpen;
                _doors[doorId] = state;

                // Broadcast toggle to clients
                using (PacketWriter writer = new PacketWriter(PacketType.DoorStateToggle))
                {
                    writer.WriteInt(doorId);
                    writer.WriteBool(state.IsOpen);
                    SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                }

                ApplyDoorToggleLocally(doorId, state.IsOpen);
            }
        }

        public void ApplyDoorToggleLocally(int doorId, bool isOpen)
        {
            if (_doors.TryGetValue(doorId, out DoorState state))
            {
                state.IsOpen = isOpen;
                _doors[doorId] = state;
            }
            OnDoorToggled?.Invoke(doorId, isOpen);
            Debug.Log($"[WorldSync] Door {doorId} isOpen = {isOpen}");
        }

        public void SetAlarmLevel(int level)
        {
            if (!SteamSocketsTransport.Instance.IsHost) return;

            AlarmLevel = level;
            using (PacketWriter writer = new PacketWriter(PacketType.AlarmStateChanged))
            {
                writer.WriteInt(level);
                SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
            }
            OnAlarmChanged?.Invoke(level);
        }

        public void ApplyAlarmLocally(int level)
        {
            AlarmLevel = level;
            OnAlarmChanged?.Invoke(level);
        }

        #endregion

        #region Late Join Snapshot Sync

        /// <summary>
        /// Host generates and sends a complete world snapshot to a newly joined client.
        /// </summary>
        public void SendWorldSnapshotToPeer(CSteamID targetPeer)
        {
            if (!SteamSocketsTransport.Instance.IsHost) return;

            Debug.Log($"[WorldSync] Generating world snapshot for late-joining peer {targetPeer}...");

            using (PacketWriter writer = new PacketWriter(PacketType.WorldSnapshotFull))
            {
                // 1. Alarm & Environment
                writer.WriteInt(AlarmLevel);
                writer.WriteFloat(HeistTimeRemaining);
                writer.WriteInt(TotalLootValue);

                // 2. Items
                writer.WriteInt(_items.Count);
                foreach (var kvp in _items)
                {
                    writer.WriteInt(kvp.Value.ItemId);
                    writer.WriteBool(kvp.Value.IsStolen);
                    writer.WriteULong(kvp.Value.StolenBySteamId);
                }

                // 3. Doors
                writer.WriteInt(_doors.Count);
                foreach (var kvp in _doors)
                {
                    writer.WriteInt(kvp.Value.DoorId);
                    writer.WriteBool(kvp.Value.IsOpen);
                    writer.WriteBool(kvp.Value.IsLocked);
                }

                // Send reliably to target peer
                SteamSocketsTransport.Instance.SendData(targetPeer, writer.ToArray(), reliable: true);
            }

            Debug.Log($"[WorldSync] Snapshot sent successfully to {targetPeer}.");
        }

        /// <summary>
        /// Client receives and unpacks full snapshot from Host.
        /// </summary>
        public void ApplyWorldSnapshot(PacketReader reader)
        {
            AlarmLevel = reader.ReadInt();
            HeistTimeRemaining = reader.ReadFloat();
            TotalLootValue = reader.ReadInt();

            // Unpack Items
            int itemCount = reader.ReadInt();
            for (int i = 0; i < itemCount; i++)
            {
                int id = reader.ReadInt();
                bool isStolen = reader.ReadBool();
                ulong stolenBy = reader.ReadULong();

                _items[id] = new ItemState
                {
                    ItemId = id,
                    IsStolen = isStolen,
                    StolenBySteamId = stolenBy
                };
            }

            // Unpack Doors
            int doorCount = reader.ReadInt();
            for (int i = 0; i < doorCount; i++)
            {
                int id = reader.ReadInt();
                bool isOpen = reader.ReadBool();
                bool isLocked = reader.ReadBool();

                _doors[id] = new DoorState
                {
                    DoorId = id,
                    IsOpen = isOpen,
                    IsLocked = isLocked
                };
            }

            OnWorldSnapshotApplied?.Invoke();
            Debug.Log($"[WorldSync] Full world snapshot successfully applied. {itemCount} items, {doorCount} doors synchronized.");
        }

        #endregion
    }
}
