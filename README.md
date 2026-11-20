# Probably Stolen - Peer-to-Peer (P2P) Multiplayer Mod

A peer-to-peer (host-client / listen server) multiplayer co-op mod for **Probably Stolen** built with **Steamworks.NET**, **Steam Lobbies**, and **SteamNetworkingSockets**.

Unlike dedicated server solutions, this mod requires **no port forwarding**, **no external server hosting**, and **no rented servers**. Players host and join games directly with Steam friends or browse public lobbies, utilizing Valve's Steam Datagram Relay (SDR) network for low-latency NAT traversal and encrypted communication.

---

## Features

- **No Dedicated Server Required**: One player acts as the authoritative Host (listen server); other players connect via Steam P2P.
- **Steam Lobby Matchmaking**:
  - Friends-Only or Public lobbies.
  - Native Steam friend invites via Steam Overlay (`Shift + Tab`).
  - In-game public lobby browser with ping/version filtering.
  - Direct join from Steam friends list or `+connect_lobby <id>` command-line launches.
- **Modern SteamNetworkingSockets Transport**:
  - Leverages Valve's modern socket API instead of legacy P2P.
  - Automatic connection handshake, lifecycle tracking, and graceful disconnect handling.
  - Differentiates between reliable events (item pickups, doors, alarms) and unreliable/no-nagle packets (motion ticks).
- **Smooth State Synchronization**:
  - High-frequency local player motion sampling (20–30 Hz) with deadband delta compression.
  - Remote avatar interpolation with configurable back-time buffers and teleport thresholds.
  - Animation and interaction flag synchronization (crouching, sprinting, carrying loot).
- **Host-Authoritative World Management**:
  - Authoritative loot theft validation to prevent race conditions or duplicate pickups.
  - Synchronized door/container locks and state toggles.
  - Synchronized alarm levels, countdown timers, and objective progress.
- **Late-Joining Support**:
  - Automatic full-world snapshot transmission to players connecting mid-heist.
- **In-Game IMGUI Overlay**:
  - Toggle anytime with **F1**.
  - Host Game, Join Friend, Lobby Browser, and Invite buttons.

---

## Architectural Overview

```
                      ┌─────────────────────────────────────────┐
                      │            Steam Matchmaking            │
                      │  - Lobby Discovery & Metadata Search    │
                      │  - Friends Overlay Invites & Rich Pres  │
                      └────────────────────┬────────────────────┘
                                           │ Auto-connect
                                           ▼
                      ┌─────────────────────────────────────────┐
                      │      Host Authority (Listen Server)     │
                      │  - SteamNetworkingSockets ListenSocket  │
                      │  - Authoritative World State (Loot, etc)│
                      │  - Late-Join Snapshot Provider          │
                      └───────────────▲─────────▲───────────────┘
                                      │         │
              P2P Connect (SteamID)   │         │   P2P Connect (SteamID)
              Valve SDR Routing       │         │   Valve SDR Routing
                                      │         │
                     ┌────────────────┴┐       ┌┴────────────────┐
                     │    Client 1     │       │    Client 2     │
                     │ - Local Player  │       │ - Local Player  │
                     │ - Remote Avatars│       │ - Remote Avatars│
                     └─────────────────┘       └─────────────────┘
```

### Layer Separation
1. **Lobby Management (`LobbyManager.cs`)**: Handles `SteamMatchmaking`, lobby metadata, friend invites, and auto-connection handoffs.
2. **Transport Layer (`SteamSocketsTransport.cs`)**: Modern `SteamNetworkingSockets` implementation managing virtual ports, message polling, connection status callbacks, and data delivery.
3. **Serialization Protocol (`PacketWriter.cs` / `PacketReader.cs`)**: Binary packing with angle compression and delta flags.
4. **State Synchronization (`WorldSyncManager.cs`, `NetworkPlayer.cs`, `PlayerSync.cs`, `NetworkManager.cs`)**: Host validation, entity interpolation, late snapshots, and packet dispatching.
5. **User Interface (`MultiplayerMenuUI.cs`)**: In-game menu for hosting, searching, joining, and inviting.

---

## Systems Breakdown: Synchronized vs. Local

| Game System | Classification | Authority Model | Details |
| :--- | :--- | :--- | :--- |
| **Player Positions & Rotations** | **Synchronized** | Client Authority + Host Relay | Transmitted at 20–30Hz with delta checks (>1cm, >1°). Interpolated smoothly on peers. |
| **Loot & Stealable Items** | **Synchronized** | **Host-Authoritative** | Clients request theft; Host verifies availability and broadcasts `ItemStolenEvent`. |
| **Doors & Containers** | **Synchronized** | **Host-Authoritative** | Host checks lock states and broadcasts state toggles. |
| **Alarm / Security System** | **Synchronized** | **Host-Authoritative** | Host manages alarm triggers, countdown timers, and broadcasts state changes. |
| **First-Person Camera** | **Local Only** | Local Client | Camera pitch, view bobbing, and FOV are strictly client-side. |
| **Cosmetics & Audio FX** | **Local Only** | Local Client | Footstep sounds and impact particles trigger locally from animation proxies. |

---

## Building & Installation

### Prerequisites
- [.NET 6.0 / 8.0 SDK](https://dotnet.microsoft.com/download)
- *Probably Stolen* installed via Steam (AppID `4348910`).

### Compiling
```bash
dotnet build -c Release
```

### Local Installation
1. Copy the output `ProbablyStolen.Multiplayer.dll` and `manifest.xml` into your game's `Mods` directory:
   ```
   <SteamLibrary>/steamapps/common/Probably Stolen/Mods/P2PMultiplayer/
   ```
2. Launch *Probably Stolen*.
3. Open **Mods** in the main menu, enable **Probably Stolen P2P Multiplayer**, and grant capabilities (`network`, `files`, `steam`).
4. Press **F1** in-game to open the multiplayer menu!

---

## Steam Workshop One-Click Install & Private Testing

*Probably Stolen* natively supports Steam Workshop mods.

To upload for private testing:
1. Create a `workshop_item.vdf` file:
   ```vdf
   "workshopitem"
   {
      "appid"            "4348910"
      "publishedfileid"  "0"
      "contentfolder"    "<Path-To-Mod-Folder>"
      "previewfile"      "<Path-To-Mod-Folder>/preview.png"
      "visibility"       "2"
      "title"            "Probably Stolen P2P Multiplayer (Private Test)"
      "description"      "Peer-to-peer co-op multiplayer mod."
   }
   ```
   *(Note: Visibility `2` = Private, `1` = Friends-Only, `0` = Public).*
2. Upload via SteamCMD:
   ```bash
   steamcmd +login <YourSteamUsername> +workshop_build_item workshop_item.vdf +quit
   ```
3. Friends can click **Subscribe** on the workshop page for a 1-click automatic installation.
