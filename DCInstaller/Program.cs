using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using DCInstaller;

const string GAME_FOLDER_NAME = "Data Center";
const string GAME_EXE = "Data Center.exe";
const string CORE_MODULE = @"MelonLoader\Il2CppAssemblies\UnityEngine.CoreModule.dll";
const string MOD_NAME = "DCMultiplayer.dll";
const int APP_ID = 4170200;

Console.WriteLine();
Console.WriteLine("=== DC Multiplayer — Installer ===");
Console.WriteLine();

string gamePath = ResolveGamePath(args);
if (gamePath == null)
{
    Fail("Could not find Data Center. Pass the install folder as an argument:\n"
         + @"   DCMultiplayer-Installer.exe ""C:\Path\To\Data Center""");
    return 2;
}

Console.WriteLine($"Game folder: {gamePath}");

if (!File.Exists(Path.Combine(gamePath, GAME_EXE)))
{
    Fail($"'{GAME_EXE}' not found in folder.");
    return 2;
}

if (IsGameRunning())
{
    Fail($"'{GAME_EXE}' is running. Close the game and try again.");
    return 3;
}

if (!Directory.Exists(Path.Combine(gamePath, "MelonLoader")))
{
    Fail("MelonLoader is not installed in this folder.\n"
         + "Install MelonLoader 0.7.2 first:\n"
         + "  https://github.com/LavaGang/MelonLoader.Installer/releases\n"
         + "After running it once on Data Center, run this installer again.");
    return 4;
}

string coreDll = Path.Combine(gamePath, CORE_MODULE);
if (!File.Exists(coreDll))
{
    Fail("MelonLoader hasn't generated assemblies yet.\n"
         + "1) Launch the game once via Steam — let MelonLoader finish its first-run codegen.\n"
         + "2) Close the game.\n"
         + "3) Run this installer again.");
    return 5;
}

// 1. Patch <>O bug
Console.WriteLine();
Console.WriteLine("Step 1/2  Patching UnityEngine.CoreModule.dll …");
try
{
    string backup = coreDll + ".bak";
    string tmp = coreDll + ".tmp";
    int removed = CecilFix.Run(coreDll, tmp);
    if (!File.Exists(backup)) File.Copy(coreDll, backup);
    File.Delete(coreDll);
    File.Move(tmp, coreDll);
    if (removed == 0)
        Console.WriteLine("           DLL was clean (Cecil normalized metadata anyway).");
    else
        Console.WriteLine($"           Removed {removed} duplicate type definition(s).");
    Console.WriteLine($"           Backup: {backup}");
}
catch (Exception ex)
{
    Fail($"Patch failed: {ex.Message}");
    return 6;
}

// 2. Deploy DCMultiplayer.dll
Console.WriteLine();
Console.WriteLine("Step 2/2  Deploying DCMultiplayer.dll …");
try
{
    string modsDir = Path.Combine(gamePath, "Mods");
    Directory.CreateDirectory(modsDir);
    string target = Path.Combine(modsDir, MOD_NAME);

    var asm = Assembly.GetExecutingAssembly();
    using var rs = asm.GetManifestResourceStream(MOD_NAME)
                  ?? throw new InvalidOperationException("Embedded mod DLL missing from installer");
    using var fs = File.Create(target);
    rs.CopyTo(fs);
    fs.Flush();

    Console.WriteLine($"           {target}  ({rs.Length:N0} bytes)");
}
catch (Exception ex)
{
    Fail($"Deploy failed: {ex.Message}");
    return 7;
}

Console.WriteLine();
Console.WriteLine("Done.");
Console.WriteLine();
Console.WriteLine($"Launch via Steam (steam://rungameid/{APP_ID}) — never run Data Center.exe directly.");
Console.WriteLine("Once in the main menu, look for the DC Multiplayer overlay in the bottom-left corner.");
Console.WriteLine("Hotkeys: F8 host  F9 leave  F10 dump  F11 invite  F12 ping");
Console.WriteLine();
Console.WriteLine("Press any key to exit.");
try { Console.ReadKey(intercept: true); } catch { /* stdin redirected */ }
return 0;

// ─── helpers ──────────────────────────────────────────────────────────────

static void Fail(string msg)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("ERROR:");
    Console.ResetColor();
    Console.WriteLine(msg);
    Console.WriteLine();
    Console.WriteLine("Press any key to exit.");
    try { Console.ReadKey(intercept: true); } catch { /* no console */ }
}

static bool IsGameRunning()
{
    foreach (var p in System.Diagnostics.Process.GetProcessesByName("Data Center"))
    {
        p.Dispose();
        return true;
    }
    return false;
}

static string ResolveGamePath(string[] args)
{
    // Explicit path argument wins
    if (args.Length > 0 && Directory.Exists(args[0]))
        return Path.GetFullPath(args[0]);

    // Walk up from where the .exe was placed
    string here = AppContext.BaseDirectory;
    if (File.Exists(Path.Combine(here, GAME_EXE)))
        return here.TrimEnd(Path.DirectorySeparatorChar);

    // Steam library scan
    foreach (string libRoot in EnumerateSteamLibraries())
    {
        string candidate = Path.Combine(libRoot, "steamapps", "common", GAME_FOLDER_NAME);
        if (File.Exists(Path.Combine(candidate, GAME_EXE)))
            return candidate;
    }

    return null;
}

static IEnumerable<string> EnumerateSteamLibraries()
{
    string steamPath = null;
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        try
        {
            steamPath = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null)
                     ?? (string)Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", null);
        }
        catch { /* registry may be locked */ }
    }
    if (string.IsNullOrEmpty(steamPath)) yield break;

    yield return steamPath;

    string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
    if (!File.Exists(vdf)) yield break;

    foreach (string line in File.ReadAllLines(vdf))
    {
        // very forgiving: matches any quoted absolute path
        int q1 = line.IndexOf('"', line.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : -1);
        if (line.Contains("\"path\"") && line.IndexOf(':') > 0)
        {
            int a = line.IndexOf('"', line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase) + 6);
            int b = a >= 0 ? line.IndexOf('"', a + 1) : -1;
            if (a >= 0 && b > a) yield return line.Substring(a + 1, b - a - 1).Replace("\\\\", "\\");
        }
    }
}
