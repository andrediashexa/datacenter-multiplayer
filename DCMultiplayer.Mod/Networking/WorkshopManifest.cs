using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DCMultiplayer.Networking;

// Each peer has its own copy of the Steam Workshop content under
// <Game>/Data Center_Data/StreamingAssets/Mods/workshop_<id>. We exchange the
// list of subscribed IDs as lobby metadata so peers can detect mismatches
// (one side missing a workshop pack means item IDs and prefab references
// won't line up once we start replicating entities).

internal static class WorkshopManifest
{
    /// <summary>Folder that holds workshop_* subdirectories.</summary>
    static string ModsRoot
    {
        get
        {
            // UnityEngine.Application.streamingAssetsPath would be cleaner but
            // requires touching Unity types. We can derive it from the game's
            // working directory: the .exe runs from the install root.
            string gameRoot = System.IO.Directory.GetCurrentDirectory();
            return Path.Combine(gameRoot, "Data Center_Data", "StreamingAssets", "Mods");
        }
    }

    public static IReadOnlyList<string> LocalIds()
    {
        var root = ModsRoot;
        if (!Directory.Exists(root)) return System.Array.Empty<string>();
        var ids = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(root, "workshop_*"))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith("workshop_")) ids.Add(name.Substring("workshop_".Length));
        }
        ids.Sort(System.StringComparer.Ordinal);
        return ids;
    }

    public static string Encode(IEnumerable<string> ids) => string.Join(",", ids);

    public static IReadOnlyList<string> Decode(string blob)
    {
        if (string.IsNullOrEmpty(blob)) return System.Array.Empty<string>();
        return blob.Split(',', System.StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Returns (missingHere, extraHere) — IDs the host has that we
    /// don't, and IDs we have that the host doesn't.</summary>
    public static (List<string> missing, List<string> extra) Diff(IReadOnlyList<string> hostIds, IReadOnlyList<string> mineIds)
    {
        var hostSet = new HashSet<string>(hostIds);
        var mineSet = new HashSet<string>(mineIds);
        var missing = hostIds.Where(x => !mineSet.Contains(x)).ToList();
        var extra = mineIds.Where(x => !hostSet.Contains(x)).ToList();
        return (missing, extra);
    }
}
