// Reused from V1ndicate1/FixCoreModule (MIT). Walks Module.Types and each
// nested-types collection, removes any TypeDef whose FullName already appeared
// at the same scope, then rewrites the assembly. Cecil's writer also normalizes
// metadata, which on its own resolves a class of Il2CppInterop output bugs.
using Mono.Cecil;
using System.Collections.Generic;
using System.IO;

namespace DCInstaller;

internal static class CecilFix
{
    public static int Run(string inputPath, string outputPath)
    {
        var bytes = File.ReadAllBytes(inputPath);
        using var ms = new MemoryStream(bytes);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(inputPath));
        using var asm = AssemblyDefinition.ReadAssembly(ms, new ReaderParameters { AssemblyResolver = resolver });

        int removed = RemoveFrom(asm.MainModule.Types);
        foreach (TypeDefinition t in asm.MainModule.Types)
            removed += RemoveNested(t);

        asm.Write(outputPath);
        return removed;
    }

    static int RemoveFrom(Mono.Collections.Generic.Collection<TypeDefinition> col)
    {
        var seen = new HashSet<string>();
        var toKill = new List<TypeDefinition>();
        foreach (TypeDefinition t in col)
            if (!seen.Add(t.FullName)) toKill.Add(t);
        foreach (TypeDefinition t in toKill) col.Remove(t);
        return toKill.Count;
    }

    static int RemoveNested(TypeDefinition parent)
    {
        if (!parent.HasNestedTypes) return 0;
        int n = RemoveFrom(parent.NestedTypes);
        foreach (TypeDefinition c in parent.NestedTypes) n += RemoveNested(c);
        return n;
    }
}
