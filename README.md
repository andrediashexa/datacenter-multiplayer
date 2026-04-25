# Data Center — Multiplayer Mod

Unofficial multiplayer mod for **Data Center** (by Waseku, Steam AppID `4170200`).
Built on top of [MelonLoader](https://melonwiki.xyz/) for IL2CPP and the game's
existing Steamworks.NET integration.

> **Status:** experimental. Lobby + transport + transform/economy replication
> validated end-to-end with two real Steam users. Discrete-event observability
> (server power, customer chosen, etc.) works cross-peer. Full entity
> replication is not implemented yet — see "What works / what doesn't" below.

## What works

- **Steam lobby** (FriendsOnly, max 4 members) with create / join / leave /
  invite-overlay flows — host advertises lobby metadata, peers auto-accept
  session requests from lobby members.
- **P2P transport** over `SteamNetworkingMessages` on three channels (control,
  state, event). Reliable + unreliable send paths, RX/TX stats in HUD.
- **Remote player avatars** — coloured capsule per peer at their world-space
  position, smoothed at 20 Hz. `F7` warps you to the first remote peer.
- **Authority model** — `Networking/Authority.cs` is the single source of
  truth for "am I the authoritative peer". Harmony patches in
  `Patches/ClientSuppression.cs` block AutoSave, ShuffleAvailableCustomers,
  and Player money/XP/reputation updates on non-host peers so the simulation
  can't run in parallel. `F6` toggles `ForceClient` for solo testing.
- **Economy sync** — host broadcasts `(money, xp, reputation)` at 1 Hz, client
  writes the values directly. Combined with the suppression patches, client
  numbers track the host instead of drifting.
- **Cross-peer event log** — host's significant actions (server power, place,
  break, repair; switch power, place; customer chosen) emit human-readable
  events that show up on every peer's HUD as a notification stack.

## What doesn't (yet)

- **No entity replication.** Each peer loads their own save, so what you see
  in the world is your own data center, not the host's. The capsule shows
  where the host *would* be standing in their world, mapped onto your
  coordinates. Useful for proving transport works; not for actually playing
  together yet.
- **No initial state snapshot.** A joining client doesn't receive the host's
  existing servers / cables / customers, only money/XP and live events.
- **Joining via Steam external invite** (clicking "Join Game" before the game
  is running) is partially supported — the in-game `GameLobbyJoinRequested_t`
  callback works, but `+connect_lobby <id>` from the launcher command-line is
  not parsed yet.

## Layout

```
Tools/
├─ DCMultiplayer.Mod/        the actual MelonMod (net6.0)
│   ├─ Mod.cs                  entry point, hotkeys, OnUpdate dispatch
│   ├─ ModInfo.cs              version + name (single source of truth)
│   ├─ Networking/
│   │   ├─ SteamLobby.cs       Steam Matchmaking wrapper + callbacks
│   │   ├─ Transport.cs        SteamNetworkingMessages send/recv
│   │   └─ Authority.cs        IsHost / IsClient / IsAuthoritative
│   ├─ Replication/
│   │   ├─ NetMsg.cs           wire format (PlayerPose, EconomyTick, EventText)
│   │   ├─ PlayerSync.cs       20 Hz pose broadcast (sender)
│   │   ├─ RemotePlayers.cs    capsule avatars (receiver)
│   │   ├─ EconomySync.cs      1 Hz money/xp/rep broadcast + apply
│   │   └─ EventLog.cs         cross-peer text notifications
│   ├─ Patches/
│   │   ├─ Observers.cs        read-only logging patches (debug)
│   │   ├─ ClientSuppression.cs  prefix patches that no-op the sim on clients
│   │   └─ HostEvents.cs       host-side patches that emit EventLog entries
│   └─ UI/Hud.cs               IMGUI overlay (status panel + event stack)
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
├─ docs/CLAUDE.md            running technical briefing — what's known about
│                              the game's IL2CPP surface, Phase A/B/C/D plan,
│                              gotchas, hotkeys, current status
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

## Hotkeys (in-game)

| Key | Action |
|-----|--------|
| `F6` | Toggle `Authority.ForceClient` (debug — testing suppression solo) |
| `F7` | Warp local player to the first remote avatar |
| `F8` | Host a Friends-Only Steam lobby |
| `F9` | Leave the current lobby |
| `F10` | Dump member list to the MelonLoader console |
| `F11` | Open Steam's Invite Friends overlay |
| `F12` | Broadcast a `PING` (debug round-trip test) |

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
