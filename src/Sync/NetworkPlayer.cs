using System;
using System.Collections.Generic;
using ProbablyStolen.Multiplayer.Serialization;
using Steamworks;
using UnityEngine;

namespace ProbablyStolen.Multiplayer.Sync
{
    public struct TransformSnapshot
    {
        public float Timestamp;
        public Vector3 Position;
        public float Yaw;
        public PlayerSyncFlags Flags;
    }

    /// <summary>
    /// Represents a remote player in the local game world.
    /// Handles interpolation, visual smoothing, and animation sync from network packets.
    /// </summary>
    public class NetworkPlayer : MonoBehaviour
    {
        public CSteamID SteamID { get; private set; }
        public string PersonaName { get; private set; }

        [Header("Interpolation Settings")]
        [SerializeField] private float interpolationBackTime = 0.1f; // 100ms buffer
        [SerializeField] private float snapDistanceThreshold = 5.0f; // Teleport threshold

        private readonly List<TransformSnapshot> _snapshots = new List<TransformSnapshot>();

        private Animator _animator;
        private Transform _headTransform;
        private GameObject _lootCarriedVisual;

        // Visual name tag
        private TextMesh _nameTag;

        public void Initialize(CSteamID steamId)
        {
            SteamID = steamId;
            PersonaName = SteamFriends.GetFriendPersonaName(steamId);
            gameObject.name = $"RemotePlayer_{PersonaName}_{steamId}";

            _animator = GetComponentInChildren<Animator>();
            SetupNameTag();
        }

        private void SetupNameTag()
        {
            GameObject nameTagObj = new GameObject("NameTag");
            nameTagObj.transform.SetParent(transform);
            nameTagObj.transform.localPosition = new Vector3(0, 2.2f, 0);

            _nameTag = nameTagObj.AddComponent<TextMesh>();
            _nameTag.text = PersonaName;
            _nameTag.characterSize = 0.15f;
            _nameTag.fontSize = 24;
            _nameTag.alignment = TextAlignment.Center;
            _nameTag.anchor = TextAnchor.MiddleCenter;
            _nameTag.color = Color.yellow;
        }

        private void LateUpdate()
        {
            // Billboard nametag towards main camera
            if (_nameTag != null && Camera.main != null)
            {
                _nameTag.transform.rotation = Quaternion.LookRotation(_nameTag.transform.position - Camera.main.transform.position);
            }

            // Interpolate position and rotation
            InterpolateTransform();
        }

        /// <summary>
        /// Enqueue a newly received state packet from the network.
        /// </summary>
        public void ApplyStateUpdate(Vector3 position, float yaw, PlayerSyncFlags flags)
        {
            TransformSnapshot snapshot = new TransformSnapshot
            {
                Timestamp = Time.time,
                Position = position,
                Yaw = yaw,
                Flags = flags
            };

            _snapshots.Add(snapshot);

            // Keep buffer bounded
            if (_snapshots.Count > 30)
            {
                _snapshots.RemoveAt(0);
            }

            // Apply immediate discrete flags (e.g., crouching, sprinting, carrying loot)
            ApplyFlags(flags);
        }

        private void ApplyFlags(PlayerSyncFlags flags)
        {
            if (_animator != null)
            {
                _animator.SetBool("IsCrouching", flags.HasFlag(PlayerSyncFlags.IsCrouching));
                _animator.SetBool("IsSprinting", flags.HasFlag(PlayerSyncFlags.IsSprinting));
                _animator.SetBool("IsCarryingLoot", flags.HasFlag(PlayerSyncFlags.IsCarryingLoot));
            }
        }

        public void TriggerAnimation(string triggerName)
        {
            if (_animator != null)
            {
                _animator.SetTrigger(triggerName);
            }
        }

        private void InterpolateTransform()
        {
            if (_snapshots.Count == 0) return;

            float renderTime = Time.time - interpolationBackTime;

            // If behind oldest snapshot, clamp to oldest
            if (renderTime <= _snapshots[0].Timestamp)
            {
                transform.position = _snapshots[0].Position;
                transform.rotation = Quaternion.Euler(0, _snapshots[0].Yaw, 0);
                return;
            }

            // If ahead of newest snapshot, extrapolate or clamp to newest
            if (renderTime >= _snapshots[_snapshots.Count - 1].Timestamp)
            {
                var newest = _snapshots[_snapshots.Count - 1];
                transform.position = Vector3.Lerp(transform.position, newest.Position, Time.deltaTime * 20f);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, newest.Yaw, 0), Time.deltaTime * 20f);
                return;
            }

            // Find interpolation bracket [lhs, rhs]
            for (int i = 0; i < _snapshots.Count - 1; i++)
            {
                if (_snapshots[i].Timestamp <= renderTime && renderTime <= _snapshots[i + 1].Timestamp)
                {
                    var lhs = _snapshots[i];
                    var rhs = _snapshots[i + 1];

                    float t = Mathf.InverseLerp(lhs.Timestamp, rhs.Timestamp, renderTime);

                    // Teleport if too far
                    if (Vector3.Distance(lhs.Position, rhs.Position) > snapDistanceThreshold)
                    {
                        transform.position = rhs.Position;
                    }
                    else
                    {
                        transform.position = Vector3.Lerp(lhs.Position, rhs.Position, t);
                    }

                    float interpolatedYaw = Mathf.LerpAngle(lhs.Yaw, rhs.Yaw, t);
                    transform.rotation = Quaternion.Euler(0, interpolatedYaw, 0);
                    return;
                }
            }
        }
    }
}
