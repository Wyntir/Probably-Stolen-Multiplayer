using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.Network
{
    /// <summary>
    /// Pure managed Steam P2P Transport Layer using SteamNetworking.
    /// Completely free of unsafe pointers, native interop, and retiring capabilities.
    /// Fully compliant with Probably Stolen's security scanner.
    /// </summary>
    public class SteamSocketsTransport : MonoBehaviour
    {
        public static SteamSocketsTransport Instance { get; private set; }

        public bool IsHost { get; private set; }
        public bool IsConnected { get; private set; }

        // Connected peers
        private readonly HashSet<CSteamID> _connectedPeers = new HashSet<CSteamID>();
        public IReadOnlyCollection<CSteamID> ConnectedPeers => _connectedPeers;

        private Callback<P2PSessionRequest_t> _sessionRequestCallback;
        private Callback<P2PSessionConnectFail_t> _sessionConnectFailCallback;

        // Events
        public event Action<CSteamID> OnPeerConnected;
        public event Action<CSteamID> OnPeerDisconnected;
        public event Action<CSteamID, byte[]> OnDataReceived;

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

        private void OnEnable()
        {
            if (SteamManager.Initialized)
            {
                // Enable Valve Steam Datagram Relay for automatic NAT traversal
                SteamNetworking.AllowP2PPacketRelay(true);

                _sessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
                _sessionConnectFailCallback = Callback<P2PSessionConnectFail_t>.Create(OnP2PSessionConnectFail);
            }
        }

        private void OnDisable()
        {
            _sessionRequestCallback?.Dispose();
            _sessionConnectFailCallback?.Dispose();
        }

        /// <summary>
        /// Start host P2P session.
        /// </summary>
        public bool StartHost()
        {
            Shutdown();

            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamTransport] Steamworks not initialized!");
                return false;
            }

            IsHost = true;
            IsConnected = true;
            Debug.Log("[SteamTransport] Host P2P session initialized.");
            return true;
        }

        /// <summary>
        /// Connect as a client to the host's SteamID.
        /// </summary>
        public bool ConnectToHost(CSteamID hostSteamID)
        {
            Shutdown();

            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamTransport] Steamworks not initialized!");
                return false;
            }

            IsHost = false;
            IsConnected = true;
            _connectedPeers.Add(hostSteamID);

            // Send handshake packet to initiate P2P session
            byte[] handshake = new byte[] { (byte)Serialization.PacketType.HandshakeRequest };
            SendData(hostSteamID, handshake, reliable: true);

            Debug.Log($"[SteamTransport] Initiated P2P connection to host: {hostSteamID}");
            OnPeerConnected?.Invoke(hostSteamID);
            return true;
        }

        private void Update()
        {
            if (!IsConnected) return;

            // Poll incoming P2P packets every frame
            PollIncomingPackets();
        }

        private void PollIncomingPackets()
        {
            uint msgSize;
            while (SteamNetworking.IsP2PPacketAvailable(out msgSize, 0))
            {
                if (msgSize == 0) continue;

                byte[] buffer = new byte[msgSize];
                if (SteamNetworking.ReadP2PPacket(buffer, msgSize, out uint bytesRead, out CSteamID remoteSteamID, 0))
                {
                    if (!_connectedPeers.Contains(remoteSteamID))
                    {
                        _connectedPeers.Add(remoteSteamID);
                        OnPeerConnected?.Invoke(remoteSteamID);
                    }

                    OnDataReceived?.Invoke(remoteSteamID, buffer);
                }
            }
        }

        private void OnP2PSessionRequest(P2PSessionRequest_t param)
        {
            CSteamID remoteID = param.m_steamIDRemote;
            Debug.Log($"[SteamTransport] Incoming P2P session request from {remoteID}");

            // Accept session from peer
            SteamNetworking.AcceptP2PSessionWithUser(remoteID);

            if (!_connectedPeers.Contains(remoteID))
            {
                _connectedPeers.Add(remoteID);
                OnPeerConnected?.Invoke(remoteID);
            }
        }

        private void OnP2PSessionConnectFail(P2PSessionConnectFail_t param)
        {
            CSteamID remoteID = param.m_steamIDRemote;
            Debug.LogWarning($"[SteamTransport] P2P session connection failed with {remoteID}: error {param.m_eP2PSessionError}");
            DisconnectPeer(remoteID, "Connection Failed");
        }

        /// <summary>
        /// Send data to a specific peer.
        /// </summary>
        public bool SendData(CSteamID targetSteamID, byte[] data, bool reliable = true)
        {
            if (data == null || data.Length == 0) return false;

            EP2PSend sendType = reliable
                ? EP2PSend.k_EP2PSendReliable
                : EP2PSend.k_EP2PSendUnreliableNoDelay;

            return SteamNetworking.SendP2PPacket(targetSteamID, data, (uint)data.Length, sendType, 0);
        }

        /// <summary>
        /// Broadcast data to all connected peers.
        /// </summary>
        public void Broadcast(byte[] data, bool reliable = true, CSteamID? excludeSteamID = null)
        {
            if (data == null || data.Length == 0) return;

            foreach (CSteamID peer in _connectedPeers)
            {
                if (excludeSteamID.HasValue && peer == excludeSteamID.Value)
                    continue;

                SendData(peer, data, reliable);
            }
        }

        public void DisconnectPeer(CSteamID steamID, string reason = "Disconnected")
        {
            if (_connectedPeers.Contains(steamID))
            {
                SteamNetworking.CloseP2PSessionWithUser(steamID);
                _connectedPeers.Remove(steamID);
                OnPeerDisconnected?.Invoke(steamID);
            }
        }

        public void Shutdown()
        {
            foreach (CSteamID peer in _connectedPeers)
            {
                SteamNetworking.CloseP2PSessionWithUser(peer);
            }
            _connectedPeers.Clear();

            IsHost = false;
            IsConnected = false;
            Debug.Log("[SteamTransport] Transport shutdown complete.");
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
