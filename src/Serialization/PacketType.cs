using System;

namespace ProbablyStolen.Multiplayer.Serialization
{
    public enum PacketType : byte
    {
        // Connection & Handshake
        HandshakeRequest = 1,
        HandshakeResponse = 2,
        DisconnectNotice = 3,
        Ping = 4,
        Pong = 5,

        // World & Late Join
        WorldSnapshotRequest = 10,
        WorldSnapshotFull = 11,
        WorldStateDelta = 12,

        // Player Sync
        PlayerStateUpdate = 20,
        PlayerAnimationTrigger = 21,
        PlayerInteraction = 22,
        PlayerInventorySync = 23,

        // Gameplay & Host Authority Events
        ItemStolenEvent = 30,
        DoorStateToggle = 31,
        AlarmStateChanged = 32,
        GuardStateUpdate = 33,
        MissionObjectiveUpdate = 34
    }

    [Flags]
    public enum PlayerSyncFlags : byte
    {
        None = 0,
        HasMoved = 1 << 0,
        HasRotated = 1 << 1,
        IsCrouching = 1 << 2,
        IsSprinting = 1 << 3,
        IsCarryingLoot = 1 << 4,
        IsInteracting = 1 << 5
    }
}
