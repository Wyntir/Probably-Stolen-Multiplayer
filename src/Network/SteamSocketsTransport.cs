using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.Network
{
    /// <summary>
    /// Modern SteamNetworkingSockets P2P transport layer.
    /// Handles listening, connecting via SteamID, connection callbacks, message polling, and sending.
    /// </summary>
    public class SteamSocketsTransport : MonoBehaviour
    {
        public static SteamSocketsTransport Instance { get; private set; }

        public bool IsHost { get; private set; }
        public bool IsConnected { get; private set; }

        private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;
        private HSteamNetConnection _hostConnection = HSteamNetConnection.Invalid;

        // Active connections: Connection Handle -> SteamID
        private readonly Dictionary<HSteamNetConnection, CSteamID> _connections = new Dictionary<HSteamNetConnection, CSteamID>();
        private readonly Dictionary<CSteamID, HSteamNetConnection> _steamIdToConnection = new Dictionary<CSteamID, HSteamNetConnection>();

        private Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusCallback;

        // Events
        public event Action<CSteamID> OnPeerConnected;
        public event Action<CSteamID> OnPeerDisconnected;
        public event Action<CSteamID, byte[]> OnDataReceived;

        private const int MaxMessagesPerPoll = 64;
        private readonly IntPtr[] _messageBuffer = new IntPtr[MaxMessagesPerPoll];

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
                _connectionStatusCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
            }
        }

        private void OnDisable()
        {
            _connectionStatusCallback?.Dispose();
        }

        /// <summary>
        /// Start host listen socket using SteamNetworkingSockets P2P.
        /// </summary>
        public bool StartHost()
        {
            Shutdown();

            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamSocketsTransport] Steamworks not initialized!");
                return false;
            }

            // Virtual port 0 for standard game traffic
            _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
            if (_listenSocket == HSteamListenSocket.Invalid)
            {
                Debug.LogError("[SteamSocketsTransport] Failed to create listen socket!");
                return false;
            }

            IsHost = true;
            IsConnected = true;
            Debug.Log("[SteamSocketsTransport] Host listen socket started successfully.");
            return true;
        }

        /// <summary>
        /// Connect as a client to a host's SteamID.
        /// </summary>
        public bool ConnectToHost(CSteamID hostSteamID)
        {
            Shutdown();

            if (!SteamManager.Initialized)
            {
                Debug.LogError("[SteamSocketsTransport] Steamworks not initialized!");
                return false;
            }

            SteamNetworkingIdentity identity = new SteamNetworkingIdentity();
            identity.SetSteamID(hostSteamID);

            _hostConnection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
            if (_hostConnection == HSteamNetConnection.Invalid)
            {
                Debug.LogError($"[SteamSocketsTransport] Failed to initiate connection to host {hostSteamID}!");
                return false;
            }

            IsHost = false;
            _connections[_hostConnection] = hostSteamID;
            _steamIdToConnection[hostSteamID] = _hostConnection;

            Debug.Log($"[SteamSocketsTransport] Connecting to host: {hostSteamID}");
            return true;
        }

        private void Update()
        {
            if (!IsConnected) return;

            // Poll incoming network messages every frame
            PollMessages();
        }

        private void PollMessages()
        {
            if (IsHost)
            {
                // Host polls incoming messages across all active peer connections
                List<HSteamNetConnection> activeConns = new List<HSteamNetConnection>(_connections.Keys);
                foreach (HSteamNetConnection conn in activeConns)
                {
                    int msgCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(conn, _messageBuffer, MaxMessagesPerPoll);
                    for (int i = 0; i < msgCount; i++)
                    {
                        ProcessMessage(_messageBuffer[i]);
                    }
                }
            }
            else if (!IsHost && _hostConnection != HSteamNetConnection.Invalid)
            {
                // Client polls messages from the host connection
                int msgCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(_hostConnection, _messageBuffer, MaxMessagesPerPoll);
                for (int i = 0; i < msgCount; i++)
                {
                    ProcessMessage(_messageBuffer[i]);
                }
            }
        }

        private void ProcessMessage(IntPtr msgPtr)
        {
            if (msgPtr == IntPtr.Zero) return;

            try
            {
                SteamNetworkingMessage_t netMessage = SteamNetworkingMessage_t.FromIntPtr(msgPtr);
                byte[] data = new byte[netMessage.m_cbSize];

                unsafe
                {
                    byte* pSrc = (byte*)netMessage.m_pData;
                    fixed (byte* pDst = data)
                    {
                        Buffer.MemoryCopy(pSrc, pDst, data.Length, data.Length);
                    }
                }

                CSteamID senderSteamID = netMessage.m_identityPeer.GetSteamID();

                OnDataReceived?.Invoke(senderSteamID, data);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SteamSocketsTransport] Error processing message: {ex}");
            }
            finally
            {
                SteamNetworkingMessage_t.Release(msgPtr);
            }
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            HSteamNetConnection conn = param.m_hConn;
            ESteamNetworkingConnectionState state = param.m_info.m_eState;
            CSteamID remoteSteamID = param.m_info.m_identityRemote.GetSteamID();

            Debug.Log($"[SteamSocketsTransport] Connection {conn.m_HSteamNetConnection} to {remoteSteamID} changed state to: {state}");

            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    if (IsHost)
                    {
                        // Host accepts new incoming client connections
                        EResult acceptResult = SteamNetworkingSockets.AcceptConnection(conn);
                        if (acceptResult != EResult.k_EResultOK)
                        {
                            Debug.LogError($"[SteamSocketsTransport] Failed to accept connection from {remoteSteamID}: {acceptResult}");
                            SteamNetworkingSockets.CloseConnection(conn, (int)EResult.k_EResultFail, "Failed to accept", false);
                        }
                        else
                        {
                            Debug.Log($"[SteamSocketsTransport] Accepted connection from {remoteSteamID}");
                        }
                    }
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    IsConnected = true;
                    _connections[conn] = remoteSteamID;
                    _steamIdToConnection[remoteSteamID] = conn;
                    OnPeerConnected?.Invoke(remoteSteamID);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    Debug.Log($"[SteamSocketsTransport] Connection closed/dropped by {remoteSteamID}: {param.m_info.m_szEndDebug}");
                    HandleDisconnect(conn, remoteSteamID);
                    break;
            }
        }

        private void HandleDisconnect(HSteamNetConnection conn, CSteamID steamID)
        {
            SteamNetworkingSockets.CloseConnection(conn, 0, "Graceful Disconnect", false);

            _connections.Remove(conn);
            _steamIdToConnection.Remove(steamID);

            if (!IsHost && conn == _hostConnection)
            {
                _hostConnection = HSteamNetConnection.Invalid;
                IsConnected = false;
            }

            OnPeerDisconnected?.Invoke(steamID);
        }

        /// <summary>
        /// Send data to a specific peer.
        /// </summary>
        public bool SendData(CSteamID targetSteamID, byte[] data, bool reliable = true)
        {
            if (!_steamIdToConnection.TryGetValue(targetSteamID, out HSteamNetConnection conn))
            {
                Debug.LogWarning($"[SteamSocketsTransport] No active connection found for SteamID {targetSteamID}");
                return false;
            }

            return SendDataDirect(conn, data, reliable);
        }

        /// <summary>
        /// Broadcast data to all connected peers.
        /// </summary>
        public void Broadcast(byte[] data, bool reliable = true, CSteamID? excludeSteamID = null)
        {
            foreach (var kvp in _connections)
            {
                if (excludeSteamID.HasValue && kvp.Value == excludeSteamID.Value)
                    continue;

                SendDataDirect(kvp.Key, data, reliable);
            }
        }

        private bool SendDataDirect(HSteamNetConnection conn, byte[] data, bool reliable)
        {
            int sendFlags = reliable
                ? Constants.k_nSteamNetworkingSend_Reliable
                : Constants.k_nSteamNetworkingSend_UnreliableNoNagle;

            unsafe
            {
                fixed (byte* pData = data)
                {
                    EResult res = SteamNetworkingSockets.SendMessageToConnection(
                        conn,
                        (IntPtr)pData,
                        (uint)data.Length,
                        sendFlags,
                        out _
                    );
                    return res == EResult.k_EResultOK;
                }
            }
        }

        public void DisconnectPeer(CSteamID steamID, string reason = "Disconnected by host")
        {
            if (_steamIdToConnection.TryGetValue(steamID, out HSteamNetConnection conn))
            {
                SteamNetworkingSockets.CloseConnection(conn, (int)EResult.k_EResultOK, reason, false);
                HandleDisconnect(conn, steamID);
            }
        }

        public void Shutdown()
        {
            if (_listenSocket != HSteamListenSocket.Invalid)
            {
                SteamNetworkingSockets.CloseListenSocket(_listenSocket);
                _listenSocket = HSteamListenSocket.Invalid;
            }

            foreach (var kvp in _connections)
            {
                SteamNetworkingSockets.CloseConnection(kvp.Key, 0, "Transport Shutdown", false);
            }

            _connections.Clear();
            _steamIdToConnection.Clear();
            _hostConnection = HSteamNetConnection.Invalid;

            IsHost = false;
            IsConnected = false;
            Debug.Log("[SteamSocketsTransport] Shutdown complete.");
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}
