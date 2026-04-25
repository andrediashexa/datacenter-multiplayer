DC Multiplayer — Installation
=============================

Requirements
------------
- Owned copy of Data Center on Steam (AppID 4170200)
- MelonLoader 0.7.2 — install via the official installer pointed at Data Center.exe:
    https://github.com/LavaGang/MelonLoader.Installer/releases
  Use version 0.7.2 specifically. Newer versions have not been tested.

Install
-------
1) Install MelonLoader 0.7.2 onto Data Center using the installer above.

2) Launch Data Center ONCE through Steam.
   MelonLoader needs this first run to generate the IL2CPP proxy assemblies.
   The game will likely fail or close on its own — that is expected. Wait for
   it to finish, then close it.

3) Run DCMultiplayer-Installer.exe.
   It will:
     - Locate your Data Center install via Steam library scan
     - Apply the <>O TypeDef fix to UnityEngine.CoreModule.dll
       (LavaGang/MelonLoader#1142 — affects Unity 6000.4.x + MelonLoader 0.7.2)
       A .bak of the original DLL is saved next to it.
     - Drop DCMultiplayer.dll into Mods/
   If auto-detection fails, pass the install folder as the first argument:

       DCMultiplayer-Installer.exe "C:\Path\To\steamapps\common\Data Center"

4) Launch Data Center via Steam again. Look for the DC Multiplayer overlay
   in the bottom-left corner of the main menu.

Hotkeys
-------
   F8   Host a Friends-Only Steam lobby
   F9   Leave the current lobby
   F10  Dump member list to the MelonLoader console
   F11  Open Steam's Invite Friends overlay
   F12  Broadcast a PING (debug)

Re-running after a game update
------------------------------
Each Data Center update typically forces MelonLoader to regenerate the IL2CPP
proxy assemblies, which re-introduces the <>O bug. After any update, repeat
steps 2 and 3.

Uninstall
---------
1) Delete  <game>/Mods/DCMultiplayer.dll
2) (optional) Restore the original UnityEngine.CoreModule.dll from its .bak.

This installer never touches files outside <game>/MelonLoader and <game>/Mods.
