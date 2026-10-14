using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UsageTray;

/// <param name="LocalId">The desktop app's id ("local_…"); opens the session via a claude:// link.</param>
sealed record ClaudeSession(string? LocalId, string? Title, DateTimeOffset? LastActivity, bool Exact);

/// <summary>
/// Maps running Claude Code session processes to the titles the desktop app shows, via its
/// per-session metadata (%APPDATA%\Claude\claude-code-sessions\**\local_*.json).
/// </summary>
static class ClaudeSessions
{
    sealed record Meta(string? LocalId, string? CliId, string? Title, DateTimeOffset? Created, DateTimeOffset? LastActivity);

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");

    // The metadata files are large and numerous; remember per file, and per resume id for the text search.
    static readonly Dictionary<string, (DateTime Mtime, Meta? Meta)> Cache = [];
    static readonly Dictionary<string, Meta?> ByResume = [];

    /// <param name="roots">Session root processes (start time and command line known).</param>
    /// <param name="desktopPids">Main processes of the desktop app; their sessions may lack a resume id.</param>
    public static Dictionary<int, ClaudeSession> Match(IReadOnlyList<Proc> roots, IReadOnlySet<int> desktopPids)
    {
        var result = new Dictionary<int, ClaudeSession>();
        if (roots.Count == 0 || !Directory.Exists(Dir)) return result;

        // Only parse files touched since the oldest running session; older ones are found by text search.
        var files = new DirectoryInfo(Dir).EnumerateFiles("local_*.json", SearchOption.AllDirectories).ToList();
        var since = roots.Min(r => r.Started!.Value).AddMinutes(-1);
        var metas = files.Where(f => f.LastWriteTime >= since).Select(Read).OfType<Meta>().ToList();
        var byCli = new Dictionary<string, Meta>();
        foreach (var m in metas)
            if (m.CliId is not null) byCli[m.CliId] = m;

        var resume = roots.ToDictionary(r => r.Pid, r => Regex.Match(r.CommandLine!, @"--resume[= ]""?([0-9a-f-]{36})").Groups[1].Value);
        Search(resume.Values.Where(id => id.Length > 0 && !byCli.ContainsKey(id)), files);

        var claimed = new HashSet<string>();
        foreach (var r in roots)
        {
            var id = resume[r.Pid];
            if (id.Length == 0) continue;
            var meta = byCli.GetValueOrDefault(id) ?? ByResume.GetValueOrDefault(id);
            if (meta is null) continue;
            result[r.Pid] = new(meta.LocalId, meta.Title, meta.LastActivity, Exact: true);
            if (meta.CliId is not null) claimed.Add(meta.CliId);
        }

        // New desktop sessions carry no id: take the nearest metadata created at/after process start.
        foreach (var r in roots.Where(r => !result.ContainsKey(r.Pid) && desktopPids.Contains(r.ParentPid)))
        {
            var start = new DateTimeOffset(r.Started!.Value);
            var best = metas
                .Where(m => m.CliId is not null && !claimed.Contains(m.CliId) && m.Created >= start.AddSeconds(-10))
                .MinBy(m => (m.Created!.Value - start).Duration());
            if (best is null) continue;
            result[r.Pid] = new(best.LocalId, best.Title, best.LastActivity, Exact: false);
            claimed.Add(best.CliId!);
        }
        return result;
    }

    static Meta? Read(FileInfo f)
    {
        if (Cache.TryGetValue(f.FullName, out var c) && c.Mtime == f.LastWriteTimeUtc) return c.Meta;
        Meta? meta = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(f.FullName));
            var root = doc.RootElement;
            string? S(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            DateTimeOffset? T(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(v.GetInt64())
                : null;
            meta = new(S("sessionId"), S("cliSessionId"), S("title"), T("createdAt"), T("lastActivityAt"));
        }
        catch (Exception)
        {
            // Half-written or unreadable: no title for this one.
        }
        Cache[f.FullName] = (f.LastWriteTimeUtc, meta);
        return meta;
    }

    /// <summary>One pass over all files for resume ids not seen as a cliSessionId; misses are remembered too.</summary>
    static void Search(IEnumerable<string> ids, List<FileInfo> files)
    {
        var open = ids.Where(id => !ByResume.ContainsKey(id)).Distinct().ToDictionary(id => id, Encoding.UTF8.GetBytes);
        if (open.Count == 0) return;
        foreach (var id in open.Keys) ByResume[id] = null;

        foreach (var f in files.OrderByDescending(f => f.LastWriteTimeUtc))
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(f.FullName); }
            catch (Exception) { continue; }
            foreach (var (id, needle) in open.Where(o => bytes.AsSpan().IndexOf(o.Value) >= 0).ToList())
            {
                ByResume[id] = Read(f);
                open.Remove(id);
            }
            if (open.Count == 0) return;
        }
    }
}
