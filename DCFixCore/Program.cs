using System;
using System.IO;
using Mono.Cecil;
using DCFixCore;

// Resolves the Data Center install via:
//   1. first CLI argument (path to game folder)
//   2. DC_GAME_DIR environment variable
//   3. <exe>/../../../../.. (assumes the project lives at <GameDir>/Tools/DCFixCore)
// In all cases we patch <GameDir>/MelonLoader/Il2CppAssemblies/UnityEngine.CoreModule.dll only.
string gameDir = (args.Length > 0 && Directory.Exists(args[0]))
                 ? Path.GetFullPath(args[0])
                 : Environment.GetEnvironmentVariable("DC_GAME_DIR")
                   ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

string TARGET = Path.Combine(gameDir, "MelonLoader", "Il2CppAssemblies", "UnityEngine.CoreModule.dll");

Console.WriteLine("DCFixCore — strips duplicate <>O TypeDefs from UnityEngine.CoreModule.dll");
Console.WriteLine($"Target: {TARGET}");
Console.WriteLine();

if (!File.Exists(TARGET))
{
    Console.Error.WriteLine("ERROR: target DLL not found. Has MelonLoader generated assemblies yet?");
    return 2;
}

if (IsGameRunning())
{
    Console.Error.WriteLine("ERROR: \"Data Center.exe\" appears to be running. Close the game first.");
    return 3;
}

if (!ScanForDuplicates(File.ReadAllBytes(TARGET), verbose: true))
{
    Console.WriteLine("DLL is already clean — nothing to do.");
    return 0;
}

string backup = TARGET + ".bak";
string tmp = TARGET + ".tmp";

try
{
    CecilFix.Run(TARGET, tmp);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: Cecil failed — {ex.Message}");
    if (File.Exists(tmp)) try { File.Delete(tmp); } catch { }
    return 4;
}

try
{
    if (!File.Exists(backup))
        File.Copy(TARGET, backup);
    File.Delete(TARGET);
    File.Move(tmp, TARGET);
}
catch (IOException ex)
{
    if (File.Exists(tmp)) try { File.Delete(tmp); } catch { }
    Console.Error.WriteLine($"ERROR: could not write patched DLL — {ex.Message}");
    return 5;
}

Console.WriteLine($"Backup: {backup}");
bool stillDirty = ScanForDuplicates(File.ReadAllBytes(TARGET), verbose: false);
Console.WriteLine(stillDirty ? "WARNING: duplicates still present after fix!" : "Fixed successfully.");
return stillDirty ? 6 : 0;

static bool IsGameRunning()
{
    foreach (var p in System.Diagnostics.Process.GetProcessesByName("Data Center"))
    {
        p.Dispose();
        return true;
    }
    return false;
}

// Counts duplicates respecting nesting scope: two `<>c` inside different
// parents are NOT duplicates, only same FullName within the same containing
// type or at the top level is.
static bool ScanForDuplicates(byte[] bytes, bool verbose)
{
    using var ms = new MemoryStream(bytes);
    var resolver = new DefaultAssemblyResolver();
    using var asm = AssemblyDefinition.ReadAssembly(ms, new ReaderParameters { AssemblyResolver = resolver });

    int dups = CountDups(asm.MainModule.Types, "top-level", verbose);
    foreach (TypeDefinition t in asm.MainModule.Types) dups += CountNested(t, verbose);
    if (verbose && dups == 0) Console.WriteLine("  no duplicates found");
    return dups > 0;
}

static int CountDups(Mono.Collections.Generic.Collection<TypeDefinition> col, string ctx, bool verbose)
{
    var seen = new System.Collections.Generic.HashSet<string>();
    int n = 0;
    foreach (TypeDefinition t in col)
    {
        if (!seen.Add(t.FullName))
        {
            n++;
            if (verbose) Console.WriteLine($"  duplicate [{ctx}]: {t.FullName}");
        }
    }
    return n;
}

static int CountNested(TypeDefinition parent, bool verbose)
{
    if (!parent.HasNestedTypes) return 0;
    int n = CountDups(parent.NestedTypes, parent.FullName, verbose);
    foreach (TypeDefinition c in parent.NestedTypes) n += CountNested(c, verbose);
    return n;
}
