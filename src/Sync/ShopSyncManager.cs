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
    /// Implements the 5 core co-op gameplay rules:
    /// 1. Customer first-come bargaining mutex & sync
    /// 2. Physical item dragging exclusive hold & drop recovery
    /// 3. Processing stations & machine sync
    /// 4. Shared shop treasury (Option A)
    /// 5. Unanimous day-end ready check
    /// </summary>
    public class ShopSyncManager : MonoBehaviour
    {
        public static ShopSyncManager Instance { get; private set; }

        // Rule 1: Customer Bargaining Mutex (CustomerId -> Owner SteamID)
        private readonly Dictionary<int, ulong> _lockedCustomers = new Dictionary<int, ulong>();
        public IReadOnlyDictionary<int, ulong> LockedCustomers => _lockedCustomers;

        // Rule 2: Item Grab Mutex (ItemId -> Holder SteamID)
        private readonly Dictionary<int, ulong> _heldItems = new Dictionary<int, ulong>();
        public IReadOnlyDictionary<int, ulong> HeldItems => _heldItems;

        // Rule 4: Shared Treasury
        public int SharedTreasury { get; private set; } = 0;
        private int _lastKnownLocalCash = 0;

        // Rule 5: Day End Votes
        private readonly HashSet<ulong> _dayEndVotes = new HashSet<ulong>();
        public bool IsLocalPlayerReadyForDayEnd { get; private set; } = false;
        public int DayEndVoteCount => _dayEndVotes.Count;

        // Events
        public event Action<int, ulong> OnCustomerLocked;
        public event Action<int> OnCustomerUnlocked;
        public event Action<int, ulong> OnItemGrabbed;
        public event Action<int> OnItemDropped;
        public event Action<int> OnTreasuryUpdated;
        public event Action<int, int> OnDayEndVoteUpdated;

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
            if (SteamSocketsTransport.Instance != null)
            {
                SteamSocketsTransport.Instance.OnPeerDisconnected += HandlePeerDisconnected;
            }
        }

        private void OnDestroy()
        {
            if (SteamSocketsTransport.Instance != null)
            {
                SteamSocketsTransport.Instance.OnPeerDisconnected -= HandlePeerDisconnected;
            }
        }

        private void Update()
        {
            if (!LobbyManager.Instance.IsInLobby) return;

            // Rule 4: Monitor and synchronize Shared Treasury
            SyncTreasuryWithGame();

            // Rule 5: Hotkey to toggle Day-End ready check (F2)
            if (Input.GetKeyDown(KeyCode.F2))
            {
                ToggleDayEndVote();
            }
        }

        #region Rule 1: Customer Mutex

        public bool IsCustomerLocked(int customerId, out ulong lockOwner)
        {
            return _lockedCustomers.TryGetValue(customerId, out lockOwner);
        }

        public void RequestLockCustomer(int customerId)
        {
            ulong myId = SteamUser.GetSteamID().m_SteamID;
            if (SteamSocketsTransport.Instance.IsHost)
            {
                AuthorizeCustomerLock(customerId, myId);
            }
            else
            {
                using (var writer = new PacketWriter(PacketType.CustomerLockRequest))
                {
                    writer.WriteInt(customerId);
                    writer.WriteULong(myId);
                    SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                }
            }
        }

        public void AuthorizeCustomerLock(int customerId, ulong claimantSteamId)
        {
            if (!SteamSocketsTransport.Instance.IsHost) return;

            if (_lockedCustomers.TryGetValue(customerId, out ulong currentOwner) && currentOwner != claimantSteamId)
            {
                Debug.LogWarning($"[ShopSync] Customer {customerId} already locked by {currentOwner}");
                return;
            }

            _lockedCustomers[customerId] = claimantSteamId;

            // Broadcast to all peers
            using (var writer = new PacketWriter(PacketType.CustomerLockBroadcast))
            {
                writer.WriteInt(customerId);
                writer.WriteULong(claimantSteamId);
                SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
            }

            OnCustomerLocked?.Invoke(customerId, claimantSteamId);
        }

        public void UnlockCustomer(int customerId)
        {
            ulong myId = SteamUser.GetSteamID().m_SteamID;
            if (_lockedCustomers.TryGetValue(customerId, out ulong owner) && owner == myId)
            {
                _lockedCustomers.Remove(customerId);
                using (var writer = new PacketWriter(PacketType.CustomerUnlockBroadcast))
                {
                    writer.WriteInt(customerId);
                    SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                }
                OnCustomerUnlocked?.Invoke(customerId);
            }
        }

        public void ApplyCustomerLockLocally(int customerId, ulong ownerSteamId)
        {
            _lockedCustomers[customerId] = ownerSteamId;
            OnCustomerLocked?.Invoke(customerId, ownerSteamId);
        }

        public void ApplyCustomerUnlockLocally(int customerId)
        {
            _lockedCustomers.Remove(customerId);
            OnCustomerUnlocked?.Invoke(customerId);
        }

        #endregion

        #region Rule 2: Item Grabbing & Physics Mutex

        public bool IsItemHeld(int itemId, out ulong holder)
        {
            return _heldItems.TryGetValue(itemId, out holder);
        }

        public void RequestGrabItem(int itemId)
        {
            ulong myId = SteamUser.GetSteamID().m_SteamID;
            if (SteamSocketsTransport.Instance.IsHost)
            {
                AuthorizeGrabItem(itemId, myId);
            }
            else
            {
                using (var writer = new PacketWriter(PacketType.ItemGrabRequest))
                {
                    writer.WriteInt(itemId);
                    writer.WriteULong(myId);
                    SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                }
            }
        }

        public void AuthorizeGrabItem(int itemId, ulong holderSteamId)
        {
            if (!SteamSocketsTransport.Instance.IsHost) return;

            if (_heldItems.TryGetValue(itemId, out ulong currentHolder) && currentHolder != holderSteamId)
            {
                Debug.LogWarning($"[ShopSync] Item {itemId} is already held by {currentHolder}");
                return;
            }

            _heldItems[itemId] = holderSteamId;

            using (var writer = new PacketWriter(PacketType.ItemGrabBroadcast))
            {
                writer.WriteInt(itemId);
                writer.WriteULong(holderSteamId);
                SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
            }

            OnItemGrabbed?.Invoke(itemId, holderSteamId);
        }

        public void ReleaseItem(int itemId, Vector3 dropPosition)
        {
            ulong myId = SteamUser.GetSteamID().m_SteamID;
            if (_heldItems.TryGetValue(itemId, out ulong holder) && holder == myId)
            {
                _heldItems.Remove(itemId);
                using (var writer = new PacketWriter(PacketType.ItemDropBroadcast))
                {
                    writer.WriteInt(itemId);
                    writer.WriteVector3(dropPosition);
                    SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                }
                OnItemDropped?.Invoke(itemId);
            }
        }

        public void ApplyItemGrabLocally(int itemId, ulong holderSteamId)
        {
            _heldItems[itemId] = holderSteamId;
            OnItemGrabbed?.Invoke(itemId, holderSteamId);
        }

        public void ApplyItemDropLocally(int itemId, Vector3 dropPosition)
        {
            _heldItems.Remove(itemId);
            OnItemDropped?.Invoke(itemId);
        }

        #endregion

        #region Rule 4: Shared Treasury (Option A)

        private void SyncTreasuryWithGame()
        {
            if (PlayerStore.Instance == null) return;

            int currentCash = PlayerStore.Instance.GetCash();

            if (SteamSocketsTransport.Instance.IsHost)
            {
                // Host detects native game money changes and broadcasts to clients
                if (currentCash != SharedTreasury)
                {
                    SharedTreasury = currentCash;
                    using (var writer = new PacketWriter(PacketType.TreasuryUpdate))
                    {
                        writer.WriteInt(SharedTreasury);
                        SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                    }
                    OnTreasuryUpdated?.Invoke(SharedTreasury);
                }
            }
            else
            {
                // Client tracks if they earned or spent locally, and requests host delta
                if (_lastKnownLocalCash != 0 && currentCash != _lastKnownLocalCash)
                {
                    int delta = currentCash - _lastKnownLocalCash;
                    using (var writer = new PacketWriter(PacketType.TreasuryDelta))
                    {
                        writer.WriteInt(delta);
                        SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                    }
                }
                _lastKnownLocalCash = currentCash;
            }
        }

        public void HandleTreasuryDeltaFromClient(int delta)
        {
            if (!SteamSocketsTransport.Instance.IsHost) return;

            if (PlayerStore.Instance != null)
            {
                PlayerStore.Instance.ModCash(delta);
                SharedTreasury = PlayerStore.Instance.GetCash();
            }
            else
            {
                SharedTreasury += delta;
            }

            // Broadcast new authoritative treasury to all squad members
            using (var writer = new PacketWriter(PacketType.TreasuryUpdate))
            {
                writer.WriteInt(SharedTreasury);
                SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
            }

            OnTreasuryUpdated?.Invoke(SharedTreasury);
        }

        public void ApplyTreasuryLocally(int totalTreasury)
        {
            SharedTreasury = totalTreasury;

            if (PlayerStore.Instance != null)
            {
                int current = PlayerStore.Instance.GetCash();
                int diff = totalTreasury - current;
                if (diff != 0)
                {
                    PlayerStore.Instance.ModCash(diff);
                }
                _lastKnownLocalCash = totalTreasury;
            }

            OnTreasuryUpdated?.Invoke(SharedTreasury);
        }

        #endregion

        #region Rule 5: Day End Unanimous Ready Check

        public void ToggleDayEndVote()
        {
            IsLocalPlayerReadyForDayEnd = !IsLocalPlayerReadyForDayEnd;
            ulong myId = SteamUser.GetSteamID().m_SteamID;

            if (SteamSocketsTransport.Instance.IsHost)
            {
                RegisterDayEndVote(myId, IsLocalPlayerReadyForDayEnd);
            }
            else
            {
                using (var writer = new PacketWriter(PacketType.DayEndVoteRequest))
                {
                    writer.WriteULong(myId);
                    writer.WriteBool(IsLocalPlayerReadyForDayEnd);
                    SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                }
            }
        }

        public void RegisterDayEndVote(ulong voterId, bool isReady)
        {
            if (!SteamSocketsTransport.Instance.IsHost) return;

            if (isReady)
            {
                _dayEndVotes.Add(voterId);
            }
            else
            {
                _dayEndVotes.Remove(voterId);
            }

            int totalMembers = LobbyManager.Instance.IsInLobby ? SteamMatchmaking.GetNumLobbyMembers(LobbyManager.Instance.CurrentLobbyID) : 1;

            // Broadcast tally to squad
            using (var writer = new PacketWriter(PacketType.DayEndVoteBroadcast))
            {
                writer.WriteInt(_dayEndVotes.Count);
                writer.WriteInt(totalMembers);
                SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
            }

            OnDayEndVoteUpdated?.Invoke(_dayEndVotes.Count, totalMembers);

            // If ALL squad members are ready, execute day end!
            if (_dayEndVotes.Count >= totalMembers && totalMembers > 0)
            {
                Debug.Log("[ShopSync] All players voted to end day! Executing Day End...");
                _dayEndVotes.Clear();
                IsLocalPlayerReadyForDayEnd = false;

                using (var writer = new PacketWriter(PacketType.DayEndExecute))
                {
                    SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
                }

                ExecuteDayEndLocally();
            }
        }

        public void ApplyVoteTallyLocally(int readyCount, int totalCount)
        {
            OnDayEndVoteUpdated?.Invoke(readyCount, totalCount);
        }

        public void ExecuteDayEndLocally()
        {
            IsLocalPlayerReadyForDayEnd = false;
            _dayEndVotes.Clear();

            if (PlayerStore.Instance != null && PlayerStore.Instance.CanEndDay())
            {
                PlayerStore.Instance.EndDay();
            }
            else if (EndOfDayUIManager.Instance != null)
            {
                EndOfDayUIManager.Instance.OnEndDayButtonClick();
            }
        }

        #endregion

        private void HandlePeerDisconnected(CSteamID peerSteamID)
        {
            ulong peerId = peerSteamID.m_SteamID;

            // Release any customer locks held by this peer
            List<int> customersToRelease = new List<int>();
            foreach (var kvp in _lockedCustomers)
            {
                if (kvp.Value == peerId) customersToRelease.Add(kvp.Key);
            }
            foreach (int cId in customersToRelease)
            {
                _lockedCustomers.Remove(cId);
                OnCustomerUnlocked?.Invoke(cId);
            }

            // Drop any items held by this peer to floor safely
            List<int> itemsToDrop = new List<int>();
            foreach (var kvp in _heldItems)
            {
                if (kvp.Value == peerId) itemsToDrop.Add(kvp.Key);
            }
            foreach (int itemId in itemsToDrop)
            {
                _heldItems.Remove(itemId);
                OnItemDropped?.Invoke(itemId);
            }

            // Remove day end vote if active
            _dayEndVotes.Remove(peerId);
        }
    }
}
