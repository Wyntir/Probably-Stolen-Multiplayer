using System;
using ProbablyStolen.Multiplayer.Network;
using ProbablyStolen.Multiplayer.Serialization;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.Sync
{
    /// <summary>
    /// Attached to the local player to sample motion, detect state deltas,
    /// and transmit high-frequency updates via SteamNetworkingSockets.
    /// </summary>
    public class PlayerSync : MonoBehaviour
    {
        [Header("Tick Rate Settings")]
        [SerializeField] private float sendRateHz = 20f; // 20 packets per second
        private float _timeSinceLastSend = 0f;

        [Header("Delta Thresholds")]
        [SerializeField] private float positionThreshold = 0.01f; // 1cm minimum movement to trigger delta
        [SerializeField] private float angleThreshold = 1.0f; // 1 degree rotation change

        private Vector3 _lastSentPosition;
        private float _lastSentYaw;
        private PlayerSyncFlags _lastSentFlags;

        private CharacterController _characterController;
        private Animator _animator;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _animator = GetComponentInChildren<Animator>();
        }

        private void Start()
        {
            _lastSentPosition = transform.position;
            _lastSentYaw = transform.eulerAngles.y;
        }

        private void Update()
        {
            if (!SteamSocketsTransport.Instance.IsConnected) return;

            _timeSinceLastSend += Time.deltaTime;
            float interval = 1f / sendRateHz;

            if (_timeSinceLastSend >= interval)
            {
                _timeSinceLastSend = 0f;
                SampleAndSendState();
            }
        }

        private void SampleAndSendState()
        {
            Vector3 currentPos = transform.position;
            float currentYaw = transform.eulerAngles.y;

            PlayerSyncFlags flags = PlayerSyncFlags.None;

            // Sample states (integrated with game's input / state system)
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C))
                flags |= PlayerSyncFlags.IsCrouching;

            if (Input.GetKey(KeyCode.LeftShift))
                flags |= PlayerSyncFlags.IsSprinting;

            // Check if player has moved or rotated beyond thresholds
            bool posDelta = Vector3.Distance(currentPos, _lastSentPosition) > positionThreshold;
            bool rotDelta = Mathf.Abs(Mathf.DeltaAngle(currentYaw, _lastSentYaw)) > angleThreshold;
            bool flagsChanged = flags != _lastSentFlags;

            // If nothing changed, skip sending this tick to save bandwidth
            if (!posDelta && !rotDelta && !flagsChanged)
            {
                return;
            }

            if (posDelta) flags |= PlayerSyncFlags.HasMoved;
            if (rotDelta) flags |= PlayerSyncFlags.HasRotated;

            // Construct compact packet
            using (PacketWriter writer = new PacketWriter(PacketType.PlayerStateUpdate))
            {
                // Write position
                writer.WriteVector3(currentPos);

                // Write compressed yaw (1 byte)
                writer.WriteByteYaw(currentYaw);

                // Write state flags byte
                writer.WriteByte((byte)flags);

                byte[] packetData = writer.ToArray();

                if (SteamSocketsTransport.Instance.IsHost)
                {
                    // Host broadcasts to all clients
                    SteamSocketsTransport.Instance.Broadcast(packetData, reliable: false);
                }
                else
                {
                    // Client sends to Host (unreliable no-nagle for lowest latency)
                    SteamSocketsTransport.Instance.Broadcast(packetData, reliable: false);
                }
            }

            _lastSentPosition = currentPos;
            _lastSentYaw = currentYaw;
            _lastSentFlags = flags;
        }

        /// <summary>
        /// Trigger discrete animation event across network (e.g. jump, punch, lockpick).
        /// </summary>
        public void SendAnimationTrigger(string triggerName)
        {
            if (!SteamSocketsTransport.Instance.IsConnected) return;

            using (PacketWriter writer = new PacketWriter(PacketType.PlayerAnimationTrigger))
            {
                writer.WriteString(triggerName);
                SteamSocketsTransport.Instance.Broadcast(writer.ToArray(), reliable: true);
            }
        }
    }
}
