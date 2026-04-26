<p align="center">
  <img src="docs/img/banner.png" alt="Data Center — Multiplayer Support" width="100%">
</p>

# Installing DC Multiplayer

This guide walks through installing the mod from a GitHub release on a
clean machine. The whole flow takes ~5 minutes.

> ⚠️ **Both you and the player you want to play with must be on the
> exact same mod version**, otherwise the in-game lobby panel will
> show a red mismatch banner and replication may misbehave. Always
> share the same `vX.Y.Z` zip from the releases page.

## What you need

- A copy of **Data Center** owned on Steam (AppID `4170200`).
- **MelonLoader 0.7.2** — newer versions haven't been tested.
- **Windows 10/11** — the installer is a Windows-only `.exe`.
- About **40 MB** of free space.

## Step 1 — Install MelonLoader 0.7.2

1. Download the official MelonLoader installer from
   [LavaGang/MelonLoader.Installer/releases](https://github.com/LavaGang/MelonLoader.Installer/releases).
2. Run `MelonLoader.Installer.exe`.
3. Click **Select** and point it at your **Data Center.exe**, which
   lives at:

   ```
   <Steam library>\steamapps\common\Data Center\Data Center.exe
   ```

   You can find this path quickly: right-click *Data Center* in your
   Steam library → *Manage* → *Browse local files*.

4. From the **Version** dropdown choose **0.7.2** (specifically — newer
   builds may break the mod).
5. Click **INSTALL** and wait for it to finish. MelonLoader has been
   installed when the installer says so.

## Step 2 — First launch (let MelonLoader generate proxies)

MelonLoader needs to run once against the game so it can generate the
IL2CPP proxy assemblies the mod links against.

1. Launch **Data Center** through Steam (`steam://rungameid/4170200` or
   the Library *Play* button).
2. Wait until the game either reaches the main menu or quits with an
   error — both outcomes are fine. MelonLoader will have generated the
   proxy DLLs by that point.
3. Close the game.

> If the game crashes on this first run, that's the
> [LavaGang/MelonLoader#1142](https://github.com/LavaGang/MelonLoader/issues/1142)
> "Duplicate type with name `<>O`" bug. The DC Multiplayer installer
> in step 3 fixes that automatically; just keep going.

## Step 3 — Run the DC Multiplayer installer

1. Open the latest release page:
   <https://github.com/andrediashexa/datacenter-multiplayer/releases/latest>
2. Under **Assets**, download `DCMultiplayer-vX.Y.Z-win-x64.zip`.
3. Extract the zip anywhere — you'll get `DCMultiplayer-Installer.exe`
   and a short `README.txt`.
4. Make sure the game is closed, then double-click
   `DCMultiplayer-Installer.exe`. The installer will:
   - find your Data Center install via Steam library scan,
   - apply the `<>O` TypeDef fix to
     `MelonLoader/Il2CppAssemblies/UnityEngine.CoreModule.dll`
     (keeping a `.bak` backup of the original),
   - drop `DCMultiplayer.dll` into `<Game>/Mods/`.

You should see something like:

```
=== DC Multiplayer — Installer ===

Game folder: C:\Program Files (x86)\Steam\steamapps\common\Data Center

Step 1/2  Patching UnityEngine.CoreModule.dll …
           Removed 0 duplicate type definition(s).
           Backup: …\UnityEngine.CoreModule.dll.bak

Step 2/2  Deploying DCMultiplayer.dll …
           …\Mods\DCMultiplayer.dll  (~50,000 bytes)

Done.
Press any key to exit.
```

If auto-detection fails, drop the installer inside your Data Center
folder and run it again — it picks up the local install when it can't
find one via Steam.

## Step 4 — Launch and verify

1. Launch **Data Center** through Steam again.
2. The MelonLoader console should show a line like:

   ```
   DC Multiplayer 0.0.21 alive — IL2CPP, Unity 6000.4.2f1
   All controls live on the in-game computer's Multiplayer panel.
   ```

3. Load a save. Walk up to any **ComputerShop** terminal in your data
   center.
4. The main screen now has a sixth icon: **Multiplayer**. Click it.
5. The screen swaps to the **DC Multiplayer** panel with status, member
   list, RX/TX counters, and action buttons (Host Lobby / Invite / etc.).

If you see the panel — you're set.

## Step 5 — Play with a friend

1. Both of you must run the **same version** (compare against the file
   name of the zip you both downloaded).
2. **Host:** open the Multiplayer panel, click **Host Lobby**, then
   **Invite** to open Steam's invite overlay.
3. **Joining peer:** accept the invite via the Steam overlay
   (Shift + Tab → Friends list → accept). The lobby panel on your side
   will switch to "lobby … · 2 members · client".
4. The Multiplayer panel now mirrors host state: Host's customer pool,
   server / switch / patch panel positions, cable connections, money,
   XP, and reputation appear on both screens.
5. Use **Warp to Peer** to teleport next to the other player's avatar.

## Troubleshooting

**"DC Multiplayer" doesn't appear on the computer.**
The mod DLL probably failed to load — open
`<Game>/MelonLoader/Latest.log` and check the `Loading Mods…` block
for an error. The most common case is the `<>O` bug coming back after
a Data Center update; re-run the installer to repatch.

**Lobby panel says "version mismatch" in red.**
You and your peer are on different mod versions. Compare the headers
(`DC Multiplayer 0.0.X`) on each side and re-install the matching zip
on whichever side is older.

**Lobby panel says "workshop mismatch" in red.**
You're missing (or have extra) Workshop items vs. the host. Subscribe
or unsubscribe in the Steam Workshop until both sides match.

**Can't see the host's data center.**
Click **Refresh** on the Multiplayer panel — that asks the host to
re-broadcast every snapshot.

**Updating to a new version.**
Just download the new zip and run the new installer over the old
install — it overwrites `Mods/DCMultiplayer.dll`. Tell your peer to
do the same.

**After a Data Center game update.**
Steam updates regenerate the IL2CPP assemblies and re-introduce the
`<>O` bug. Re-run `DCMultiplayer-Installer.exe` once after each game
update.

## Uninstalling

1. Delete `<Game>/Mods/DCMultiplayer.dll`.
2. (Optional) Restore the original `UnityEngine.CoreModule.dll` from
   the `.bak` backup created by the installer.
3. (Optional) Use the MelonLoader installer's **Uninstall** option to
   remove MelonLoader entirely.

The mod never touches files outside `<Game>/MelonLoader` and
`<Game>/Mods`, so there's nothing to clean up beyond those.
