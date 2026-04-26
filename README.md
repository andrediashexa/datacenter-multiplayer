<p align="center">
  <img src="docs/img/banner.png" alt="Data Center — Multiplayer Support" width="100%">
</p>

<p align="center">
  <img src="https://img.shields.io/badge/status-in%20development-orange?style=flat-square" alt="Status: in development">
  <img src="https://img.shields.io/github/v/release/andrediashexa/datacenter-multiplayer?include_prereleases&style=flat-square&label=latest" alt="Latest release">
  <img src="https://img.shields.io/github/actions/workflow/status/andrediashexa/datacenter-multiplayer/test.yml?branch=main&style=flat-square&label=tests" alt="Tests">
  <img src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" alt="License: MIT">
  <img src="https://img.shields.io/badge/MelonLoader-0.7.2-7c3aed?style=flat-square" alt="MelonLoader 0.7.2">
  <img src="https://img.shields.io/badge/Unity-6000.4.2f1-222c37?style=flat-square&logo=unity" alt="Unity 6000.4.2f1">
</p>

# Data Center — Multiplayer Mod

Unofficial multiplayer mod for **Data Center** (by Waseku, Steam AppID `4170200`).
Built on top of [MelonLoader](https://melonwiki.xyz/) for IL2CPP and the game's
existing Steamworks.NET integration.

Developed by **[André Dias](https://github.com/andrediashexa)**.

> 📦 **Just want to play?** Follow the [installation guide](INSTALL.md).
> The rest of this README is aimed at people building or contributing to
> the mod.

> 🚧 **This is a work in progress.**
> The mod is under active development and **not feature-complete**. Wire formats,
> hotkeys, and on-disk layout may change between any two versions before `0.1`.
> Both peers must run the **exact same** version — joining a host on a different
> mod version is detected and surfaced as a banner in the in-game lobby panel,
> but full compatibility is not guaranteed.

## What's replicated (host → peers)

The "see the host's data center" picture is complete on the read-only side.
A joining client gets the full visual + identity state of the host's world
without their own save loaded:

- **Steam lobby** (FriendsOnly, max 4 members) with create / join / leave /
  invite-overlay flows. Host advertises lobby metadata (`dcmp_version`,
  `dcmp_host_name`, `dcmp_workshop`); peers auto-accept session requests
  from lobby members. Joining client warns when the host's mod version or
  Workshop subscription set differs from its own (banner inside the in-game
  Multiplayer panel + log).
- **P2P transport** over `SteamNetworkingMessages` on three channels
  (control / state / event). Reliable + unreliable send paths, RX/TX stats
  shown in the in-game panel. `F5` toggles a debug loopback that also
  dispatches `Broadcast` to local handlers for solo round-trip testing.
- **Remote player avatars** — coloured capsule per peer at their world-space
  position, smoothed at 20 Hz, with a TextMeshPro nameplate that billboards
  toward the local camera. `F7` warps you to the first remote peer.
- **Authority model** — `Networking/Authority.cs` is the single source of
  truth for "am I the authoritative peer". Harmony patches in
  `Patches/ClientSuppression.cs` block AutoSave, `ShuffleAvailableCustomers`,
  and Player money/XP/reputation updates on non-host peers so the
  simulation can't run in parallel. `F6` toggles `ForceClient` for solo
  testing of the suppression path.
- **Client save suppression** (opt-in via `F4`) — when joining as client,
  `SaveSystem.LoadGame`'s output gets its world-state portion (network,
  racks, items, technicians, mods) replaced with empty containers so the
  scene loads geometry-only and waits to be populated by host snapshots.
  `playerData` and `loadedScenes` are kept so the player still spawns.
- **Economy sync** — host broadcasts `(money, xp, reputation)` at 1 Hz,
  client writes the values directly. Combined with the suppression
  patches, client numbers track the host instead of drifting or freezing.
- **Customer pool sync** — `MainGameManager.availableCustomerIndices` is
  pushed to clients on join, on every host shuffle, and on every customer
  choice. Clients mirror the same set of cards in their customer-choice
  canvas.
- **Customer base assignments** — `(customerBaseID, customerID)` pairs from
  `NetworkMap.customerBases`, applied on the client by writing the base's
  `customerID` and `customerItem` directly. Combined with the pool sync,
  client and host see the same customers in the same bases.
- **Server placements** — `NetworkMap.servers` ghosted as coloured cube
  primitives at the host's world-space position, hue derived from
  ServerID hash, brightness reflecting on/off, broken servers tinted
  red. Re-broadcast on every PowerButton / ServerInsertedInRack /
  ItIsBroken / RepairDevice / SetIP / UpdateAppID / UpdateCustomer
  postfix.
- **Switch placements** — same pattern as servers but slimmer ghosts in a
  green hue band so they're visually distinct.
- **Patch panel placements** — discovered via
  `Object.FindObjectsOfType<PatchPanel>` (no NetworkMap registry exists
  for them). Yellow-orange ghost band.
- **Cable connections** — `NetworkMap.cableConnections` shipped as
  identity-only `(cableId, endpointA, endpointB)` records. Client renders
  each as a `LineRenderer` between whatever ghost positions it currently
  has for the endpoints, refreshed every frame; cables whose endpoints
  aren't yet visible (e.g. customer-base terminations) hide themselves
  rather than dangle.
- **Cross-peer event log** — host's significant actions (server power /
  place / break / repair, switch power / place / break / repair, patch
  panel place, customer chosen, cable connect / disconnect) emit
  human-readable events that show up on every peer's in-game panel.
- **Action intents** (client → host) — `MsgIntent` envelope with subtype
  byte and variable payload, delivered reliably on the event channel.
  Host validates `Authority.IsAuthoritative` then applies. Current
  subtypes:
    - `IntentRefresh` — re-broadcast every snapshot. Wired to a Refresh
      button on the lobby panel for manual resync.
    - `IntentToggleServerPower(serverId)` — host calls
      `Server.PowerButton(!isOn)`; the existing Harmony postfix on
      PowerButton re-broadcasts the server snapshot.
    - `IntentToggleSwitchPower(switchId)` — same flow with `GetSwitchById`.
- **In-game lobby UI** — there is no longer an IMGUI overlay. The lobby
  state lives on the in-game ComputerShop terminal: the menu grid gets a
  cloned "Multiplayer" button alongside Shop / Network Map / Asset
  Management / Balance Sheet / Hire; clicking it swaps the screen to a
  panel with the lobby ID, member list, RX/TX stats, version + workshop
  mismatch banners, and action buttons (Host Lobby / Leave Lobby /
  Invite / Copy ID / Ping / Refresh) that toggle visibility based on
  lobby state. Back button mirrors the existing secondary screens.

## Wire format

Every cross-peer message starts with a one-byte type. Channels are
[`Transport.cs`]: 0 = control (PING/PONG, debug text), 1 = state
(positions, ticks — unreliable OK), 2 = event (snapshots, intents —
reliable only).

| Type   | Message | Channel | Direction |
|--------|---------|---------|-----------|
| `0x10` | `PlayerPose` (xyz + yaw + pitch) | state | every peer → every peer @ 20 Hz |
| `0x20` | `EconomyTick` (money + xp + rep) | state | host → peers @ 1 Hz |
| `0x30` | `EventText` (UTF-8 line) | event | sender → peers, on demand |
| `0x40` | `CustomerPool` (Int32 list) | event | host → peers, on join + on shuffle |
| `0x50` | `ServerSnapshot` (records) | event | host → peers, on join + on power/place/break/repair |
| `0x60` | `BaseAssignments` (pairs) | event | host → peers, on join + on customer chosen |
| `0x70` | `SwitchSnapshot` (records) | event | host → peers, on join + on power/place/break/repair |
| `0x80` | `CableSnapshot` (records) | event | host → peers, on join + on connect/disconnect |
| `0x90` | `PatchPanelSnapshot` (records) | event | host → peers, on join + on place |
| `0xA0` | `Intent` (subtype + payload) | event | client → host |

Intent subtypes (after the `0xA0` type byte):

| Subtype | Action | Payload |
|---------|--------|---------|
| `0x01` | `Refresh` — re-broadcast every snapshot | none |
| `0x02` | `ToggleServerPower` | byte len + UTF-8 serverId |
| `0x03` | `ToggleSwitchPower` | byte len + UTF-8 switchId |

`Tests/DCMultiplayer.Mod.Tests/NetMsgTests.cs` round-trips every
serializer with edge values and runs in CI on every push/PR.

## What's *not* replicated yet

- **Client-side intents beyond power toggle** — buying items, connecting
  cables, choosing customers from the canvas, hiring technicians, all
  still local-only on a client. The intent envelope is in place; each
  new action just needs a subtype + apply method.
- **Per-app subnet / VLAN / accumulated-speed state** on customer bases
  isn't yet shipped — clients see which customer is on which base, but
  not their per-app health.
- **Technicians** — NPCs aren't replicated. With save suppression on,
  the client just doesn't see them.
- **External Steam invites** (`+connect_lobby <id>` from launch args). In-
  game overlay invites work via `GameLobbyJoinRequested_t`; the launch-
  argument path isn't parsed yet.

## Layout

```
Tools/
├─ DCMultiplayer.Mod/        the actual MelonMod (net6.0)
│   ├─ Mod.cs                  entry point, hotkeys, OnUpdate dispatch
│   ├─ ModInfo.cs              version + name (single source of truth)
│   ├─ Networking/
│   │   ├─ SteamLobby.cs       Matchmaking + lobby callbacks +
│   │   │                        version/Workshop mismatch detection
│   │   ├─ Transport.cs        SteamNetworkingMessages send/recv
│   │   │                        (+ DebugLoopback for solo testing)
│   │   ├─ Authority.cs        IsHost / IsClient / IsAuthoritative
│   │   │                        (+ ForceClient and SuppressClientSave
│   │   │                        debug toggles)
│   │   └─ WorkshopManifest.cs enumerate StreamingAssets/Mods/workshop_*
│   ├─ Replication/
│   │   ├─ NetMsg.cs           wire format for every message type
│   │   ├─ PlayerSync.cs       20 Hz local-pose broadcast (sender)
│   │   ├─ RemotePlayers.cs    capsule avatars + nameplates (receiver)
│   │   ├─ EconomySync.cs      1 Hz money/xp/rep broadcast + apply
│   │   ├─ EventLog.cs         cross-peer text notifications
│   │   ├─ CustomerPoolSync.cs replicate availableCustomerIndices
│   │   ├─ BaseAssignmentsSync.cs   (baseId → customerId) pairs
│   │   ├─ ServerSnapshotSync.cs    NetworkMap.servers ghosts
│   │   ├─ SwitchSnapshotSync.cs    NetworkMap.switches ghosts
│   │   ├─ PatchPanelSnapshotSync.cs FindObjectsOfType<PatchPanel> ghosts
│   │   ├─ CableSnapshotSync.cs     LineRenderers between resolved endpoints
│   │   ├─ IntentBus.cs        client → host action requests + apply
│   │   └─ ComputerShopBadge.cs in-game lobby panel mounted on the
│   │                             ComputerShop terminal (cloned button +
│   │                             cloned screen + Back arrow)
│   ├─ Patches/
│   │   ├─ Observers.cs        read-only logging patches (debug)
│   │   ├─ ClientSuppression.cs  prefix patches that no-op the sim on
│   │   │                          clients (autosave, customer shuffle,
│   │   │                          money / xp / rep updates)
│   │   ├─ ClientSaveSuppress.cs   strips world-state from SaveData on
│   │   │                          clients with SuppressClientSave on
│   │   └─ HostEvents.cs       host-side postfixes that emit EventLog
│   │                            entries and trigger the matching
│   │                            snapshot rebroadcasts
│   └─ UI/Hud.cs               (legacy) IMGUI overlay — kept in the repo
│                                but no longer rendered; see
│                                ComputerShopBadge for the live UI
│
├─ DCInstaller/              single-file self-contained installer (net8.0)
│                              embeds the built mod DLL, runs the <>O fix,
│                              deploys to <Game>/Mods/.
│
├─ DCFixCore/                standalone variant of the <>O fix (dev tool)
│
├─ DCInspect/                Mono.Cecil-based dumper for Assembly-CSharp.dll
│                              (resolve real method signatures during dev)
│
├─ Tests/DCMultiplayer.Mod.Tests/   xunit round-trip tests for NetMsg
│                              (no game deps, runs in CI on every push/PR)
│
├─ docs/img/banner.png       repo banner (used at the top of this README)
├─ docs/CLAUDE.md            running technical briefing — what's known about
│                              the game's IL2CPP surface, plan, gotchas,
│                              current status
│
├─ scripts/release.ps1       cut a release: rebuild, zip, tag, push
├─ .github/workflows/        CI (test on push/PR, release shell on tag)
│
├─ README.md                 this file
├─ LICENSE                   MIT
└─ .gitignore
```

## Building

Requirements:

- A Steam install of **Data Center**.
- **MelonLoader 0.7.2** installed on it via the
  [official installer](https://github.com/LavaGang/MelonLoader.Installer/releases).
- The game launched at least once via Steam so MelonLoader has generated the
  IL2CPP proxy assemblies under `<Game>/MelonLoader/Il2CppAssemblies/`.
- **.NET 8 SDK**.

The simplest setup is to clone this repo as `<Game>/Tools/` so the MSBuild
defaults pick up the right `GameDir`:

```sh
cd "<Steam>/steamapps/common/Data Center"
git clone https://github.com/andrediashexa/datacenter-multiplayer.git Tools
cd Tools/DCMultiplayer.Mod
dotnet build -c Release
```

The build copies `DCMultiplayer.dll` into `<Game>/Mods/`.

To clone elsewhere, point each project at the game folder explicitly:

```sh
dotnet build -c Release -p:GameDir="C:\Path\To\Data Center"
```

`DCFixCore` and `DCInspect` accept the path via `DC_GAME_DIR` environment
variable or first CLI argument.

## Distributing

```sh
cd Tools/DCInstaller
dotnet publish -c Release
# -> bin/Release/net8.0/win-x64/publish/DCMultiplayer-Installer.exe
```

The single-file installer:

1. Locates Data Center via Steam library scan (or accepts a path argument).
2. Verifies MelonLoader is present.
3. Applies the `<>O` TypeDef fix to `UnityEngine.CoreModule.dll`
   ([LavaGang/MelonLoader#1142](https://github.com/LavaGang/MelonLoader/issues/1142)),
   keeping a `.bak` of the original.
4. Drops `DCMultiplayer.dll` into `<Game>/Mods/`.

Send the resulting `.exe` to a peer; it self-extracts and needs no .NET
runtime on the target machine.

### Cutting a release

The mod itself can't be built in CI — `Il2CppAssemblies/*.dll` are generated
locally by MelonLoader on a Data Center install and aren't redistributable.
So releases are built locally and published via a small helper script:

```pwsh
# 1. Bump ModInfo.Version + commit + push
# 2. Run from anywhere inside the repo:
pwsh ./scripts/release.ps1 0.0.9
```

The script:
- refuses to release from a dirty working tree or a non-`main` branch
- refuses if `ModInfo.Version` doesn't match the requested version
- clean-rebuilds the mod, publishes the single-file installer, packages
  `dist/DCMultiplayer-v<version>-win-x64.zip`
- tags `v<version>`, pushes the tag

`.github/workflows/release.yml` then opens the GitHub release shell with
notes pulled from the tag annotation and the latest commit body. Upload
the `.zip` to that release page (drag-and-drop in the UI, or
`gh release upload v0.0.9 dist/DCMultiplayer-v0.0.9-win-x64.zip`).

## In-game controls

The mod has no keyboard shortcuts. Every interaction lives on the **in-game
computer**: walk up to a ComputerShop terminal, click the **Multiplayer**
button alongside Shop / Network Map / Asset Management / Balance Sheet /
Hire, and use the panel that appears.

The panel surfaces, depending on lobby state:

- Lobby ID + member list + role (host / client) + RX-TX byte counters.
- Version and Workshop mismatch banners when joining a host on a different
  build or a different set of subscribed Workshop content.
- **Host Lobby** — create a Friends-Only Steam lobby (visible when idle).
- **Leave Lobby** — leave the current lobby (visible when in lobby).
- **Invite** — open Steam's invite overlay.
- **Copy ID** — write the lobby's CSteamID to the system clipboard.
- **Ping** — debug round-trip test, broadcasts a PING that peers reply to.
- **Refresh** — request a snapshot resync from the host.
- **Warp to Peer** — teleport your character next to the first remote
  avatar (visible when at least one peer's pose has been received).

The Back arrow in the top-right of the panel returns to the computer's
main menu, matching the existing Balance Sheet / Asset Management screens.

The few internal debug flags (`Authority.ForceClient`,
`Transport.DebugLoopback`, `Authority.SuppressClientSave`) are still
present in the code as static booleans for future experimentation, but
they are no longer bound to any input — change them at the source if you
need to flip them.

## Credits / third-party

- [MelonLoader](https://github.com/LavaGang/MelonLoader) — IL2CPP modding host.
- [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) — managed proxy
  assemblies for IL2CPP types.
- [Mono.Cecil](https://github.com/jbevain/cecil) — assembly rewriter used by
  the `<>O` fix.
- [FixCoreModule](https://github.com/V1ndicate1/FixCoreModule) by V1ndicate1 —
  the Cecil-based TypeDef stripping logic is adapted from this project under
  MIT.
- [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) by
  Riley Labrecque — the wrapper Data Center already ships with, which the mod
  reuses for lobby/networking calls.

## License

MIT — see [LICENSE](LICENSE).
