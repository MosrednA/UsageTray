using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UsageTray;

/// <summary>
/// Reads the latest rate-limit snapshot Codex writes into its session logs
/// (~/.codex/sessions/YYYY/MM/DD/*.jsonl, "token_count" events). Fully local, no auth.
/// </summary>
static class CodexSource
{
    static readonly string SessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    // Many sessions (subagents, automations) never log rate limits; remember per file so we
    // don't re-scan unchanged files on every refresh.
    static readonly Dictionary<string, (long Length, DateTime Mtime, Snapshot? Hit)> Cache = [];

    sealed record Snapshot(JsonObject Limits, DateTimeOffset At);

    public static ProviderStatus Read()
    {
        try
        {
            foreach (var file in RecentSessionFiles())
            {
                if (LatestSnapshot(file) is { } hit)
                    return Build(hit);
            }
            return new("Codex", [], Error: "geen limietdata in recente sessies");
        }
        catch (Exception ex)
        {
            return new("Codex", [], Error: ex.Message);
        }
    }

    // A long-running session keeps writing into the folder of the day it started,
    // so look at a few day folders and order by last write.
    static IEnumerable<FileInfo> RecentSessionFiles()
    {
        if (!Directory.Exists(SessionsDir)) return [];
        return Directory.EnumerateDirectories(SessionsDir).OrderDescending(StringComparer.Ordinal)
            .SelectMany(y => Directory.EnumerateDirectories(y).OrderDescending(StringComparer.Ordinal))
            .SelectMany(m => Directory.EnumerateDirectories(m).OrderDescending(StringComparer.Ordinal))
            .Take(4)
            .SelectMany(d => new DirectoryInfo(d).EnumerateFiles("*.jsonl"))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(60)
            .ToList();
    }

    static Snapshot? LatestSnapshot(FileInfo file)
    {
        if (Cache.TryGetValue(file.FullName, out var c) && c.Length == file.Length && c.Mtime == file.LastWriteTimeUtc)
            return c.Hit;

        var hit = ScanTail(file);
        Cache[file.FullName] = (file.Length, file.LastWriteTimeUtc, hit);
        return hit;
    }

    static Snapshot? ScanTail(FileInfo file)
    {
        const long TailBytes = 4 << 20;
        using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, fs.Length - TailBytes);
        fs.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(fs);
        if (start > 0) reader.ReadLine(); // partial line

        var matches = new List<string>();
        for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
            if (line.Contains("\"rate_limits\":{", StringComparison.Ordinal)) matches.Add(line);

        for (var i = matches.Count - 1; i >= 0; i--)
        {
            JsonNode? root;
            try { root = JsonNode.Parse(matches[i]); }
            catch (JsonException) { continue; } // last line may still be half-written

            if (root?["payload"]?["rate_limits"] is not JsonObject limits) continue;
            var id = limits["limit_id"]?.ToString();
            if (id is not null && id != "codex") continue;
            if (limits["primary"] is null && limits["secondary"] is null) continue;

            var at = DateTimeOffset.TryParse(root["timestamp"]?.ToString(), CultureInfo.InvariantCulture, out var t)
                ? t : new DateTimeOffset(file.LastWriteTimeUtc);
            return new(limits, at);
        }
        return null;
    }

    static ProviderStatus Build(Snapshot s)
    {
        var windows = new[] { s.Limits["primary"], s.Limits["secondary"] }
            .Select(n => ToWindow(n, s.At))
            .OfType<UsageWindow>()
            .OrderBy(w => w.Length)
            .ToList();

        string? credits = null;
        if (s.Limits["credits"] is JsonObject c && c["has_credits"]?.GetValue<bool>() == true)
        {
            credits = c["unlimited"]?.GetValue<bool>() == true ? "credits ∞"
                : double.TryParse(c["balance"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var bal)
                    ? $"{bal:0} credits" : null;
        }

        // Codex only logs limits while you use it, so flag old snapshots.
        var age = DateTimeOffset.Now - s.At;
        var stale = age > TimeSpan.FromMinutes(15) ? $"{Fmt.Span(age)} oud" : null;

        return new("Codex", windows, s.Limits["plan_type"]?.ToString(), Fmt.Join(credits, stale));
    }

    static UsageWindow? ToWindow(JsonNode? n, DateTimeOffset at)
    {
        if (n is null) return null;
        var used = n["used_percent"]?.GetValue<double>() ?? 0;
        var length = TimeSpan.FromMinutes(n["window_minutes"]?.GetValue<double>() ?? 0);
        DateTimeOffset? reset =
            n["resets_at"] is JsonValue ra ? DateTimeOffset.FromUnixTimeSeconds((long)ra.GetValue<double>())
            : n["resets_in_seconds"] is JsonValue ri ? at.AddSeconds(ri.GetValue<double>())
            : null;

        // Window rolled over since the snapshot was written.
        if (reset <= DateTimeOffset.Now) (used, reset) = (0, null);

        var label = length.TotalDays >= 1 ? $"{length.TotalDays:0}d" : $"{length.TotalHours:0}h";
        return new(label, used, length, reset);
    }
}
