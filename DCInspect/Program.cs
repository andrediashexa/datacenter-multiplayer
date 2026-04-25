using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// GameDir resolution order:
//   1. DC_GAME_DIR environment variable
//   2. <repo>/.. (assumes project lives at <GameDir>/Tools/DCInspect)
string gameDir = Environment.GetEnvironmentVariable("DC_GAME_DIR")
                 ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
string ASM_DIR = Path.Combine(gameDir, "MelonLoader", "Il2CppAssemblies");
string ASM = Path.Combine(ASM_DIR, "Assembly-CSharp.dll");
if (Environment.GetEnvironmentVariable("DC_ASM") is string envAsm && envAsm.Length > 0)
    ASM = Path.IsPathRooted(envAsm) ? envAsm : Path.Combine(ASM_DIR, envAsm);

if (args.Length == 0)
{
    Console.WriteLine("usage: DCInspect <command> [args]");
    Console.WriteLine("  list-types [filter]      list types whose name contains <filter>");
    Console.WriteLine("  type <FullName>          dump fields/methods of a type");
    Console.WriteLine("  find-method <name>       find any method with this name across all types");
    Console.WriteLine("  ns                       show namespace histogram");
    return;
}

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(ASM));
using var asm = AssemblyDefinition.ReadAssembly(ASM, new ReaderParameters { AssemblyResolver = resolver });
var allTypes = asm.MainModule.GetTypes().ToList();

switch (args[0])
{
    case "list-types":
    {
        string filter = args.Length > 1 ? args[1].ToLowerInvariant() : null;
        foreach (var t in allTypes
            .Where(t => filter == null || t.FullName.ToLowerInvariant().Contains(filter))
            .OrderBy(t => t.FullName))
        {
            Console.WriteLine(t.FullName);
        }
        break;
    }
    case "type":
    {
        if (args.Length < 2) { Console.Error.WriteLine("need type name"); return; }
        string target = args[1];
        var t = allTypes.FirstOrDefault(x => x.FullName == target)
             ?? allTypes.FirstOrDefault(x => x.Name == target);
        if (t == null) { Console.Error.WriteLine("not found"); return; }
        Console.WriteLine($"// {t.FullName}");
        Console.WriteLine($"// base: {t.BaseType?.FullName}");
        if (t.HasInterfaces) Console.WriteLine($"// interfaces: {string.Join(", ", t.Interfaces.Select(i => i.InterfaceType.FullName))}");
        Console.WriteLine();
        foreach (var f in t.Fields.Where(x => !x.IsCompilerControlled))
            Console.WriteLine($"  field {VisField(f)} {f.FieldType} {f.Name}");
        Console.WriteLine();
        foreach (var m in t.Methods.OrderBy(x => x.Name))
        {
            string sig = string.Join(", ", m.Parameters.Select(p => $"{p.ParameterType} {p.Name}"));
            Console.WriteLine($"  method {VisMethod(m)} {m.ReturnType} {m.Name}({sig})");
        }
        break;
    }
    case "find-method":
    {
        if (args.Length < 2) { Console.Error.WriteLine("need method name"); return; }
        string target = args[1];
        foreach (var t in allTypes)
        {
            foreach (var m in t.Methods.Where(x => x.Name == target))
            {
                string sig = string.Join(", ", m.Parameters.Select(p => $"{p.ParameterType} {p.Name}"));
                Console.WriteLine($"{t.FullName}::{m.Name}({sig}) -> {m.ReturnType}");
            }
        }
        break;
    }
    case "ns":
    {
        var hist = allTypes
            .GroupBy(t => string.IsNullOrEmpty(t.Namespace) ? "<global>" : t.Namespace)
            .OrderByDescending(g => g.Count());
        foreach (var g in hist)
            Console.WriteLine($"{g.Count(),6}  {g.Key}");
        break;
    }
    default:
        Console.Error.WriteLine($"unknown command: {args[0]}");
        break;
}

static string VisField(FieldDefinition f)
{
    if (f.IsPublic) return "public";
    if (f.IsPrivate) return "private";
    if (f.IsFamily) return "protected";
    if (f.IsAssembly) return "internal";
    return "?";
}
static string VisMethod(MethodDefinition m)
{
    string vis = m.IsPublic ? "public" : m.IsPrivate ? "private" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "?";
    return (m.IsStatic ? "static " : "") + vis;
}
