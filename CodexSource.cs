using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UsageTray;

/// <summary>
/// Codex limits. Live via the official `codex app-server` JSON-RPC interface, which uses
/// Codex's own login; falls back to the last snapshot Codex wrote into its session logs
/// (~/.codex/sessions/YYYY/MM/DD/*.jsonl) when the CLI isn't available.
/// </summary>
static class CodexSource
{
    static readonly string CodexHome = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    static readonly string SessionsDir = Path.Combine(CodexHome, "sessions");

    /// <returns>null when Codex isn't installed.</returns>
    public static async Task<ProviderStatus?> ReadAsync()
    {
        var exe = FindCli();
        if (exe is null && !Directory.Exists(CodexHome)) return null;
        try
        {
            if (exe is not null && await ReadLiveAsync(exe) is { } live) return live;
        }
        catch (Exception)
        {
            // Fall through to the session logs.
        }
        return ReadLogs();
    }

    // --- Live: codex app-server -------------------------------------------------------------

    static async Task<ProviderStatus?> ReadLiveAsync(string exe)
    {
        var psi = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo(exe, "app-server")
            : new ProcessStartInfo("cmd.exe", $"/d /c \"\"{exe}\" app-server\"");
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardInput = psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;

        using var proc = Process.Start(psi);
        if (proc is null) return null;
        _ = proc.StandardError.ReadToEndAsync(); // drain so the server never blocks on stderr

        try
        {
            var stdin = proc.StandardInput;
            await stdin.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"usagetray","version":"0.1.0"}}}""");
            await stdin.WriteLineAsync("""{"jsonrpc":"2.0","method":"initialized"}""");
            await stdin.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"account/read"}""");
            await stdin.WriteLineAsync("""{"jsonrpc":"2.0","id":3,"method":"account/rateLimits/read"}""");
            await stdin.FlushAsync();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            JsonNode? account = null, limits = null;
            while (account is null || limits is null)
            {
                var line = await proc.StandardOutput.ReadLineAsync(timeout.Token);
                if (line is null) return null;
                JsonNode? msg;
                try { msg = JsonNode.Parse(line); }
                catch (JsonException) { continue; }
                if (msg?["id"] is not JsonValue idValue || !idValue.TryGetValue<int>(out var id)) continue;
                if (id == 2) account = msg;
                else if (id == 3) limits = msg;
            }

            if (account!["result"] is JsonObject acc && acc["account"] is null && acc["requiresOpenaiAuth"]?.GetValue<bool>() == true)
                return new("Codex", [], Error: L.T("not signed in · click to sign in", "niet ingelogd · klik om in te loggen"), NeedsLogin: true);

            if (limits!["result"] is not JsonObject result || result["rateLimits"] is not JsonObject main)
                return limits["error"]?["message"]?.ToString() is { } err ? new("Codex", [], Error: err) : null;

            var resets = result["rateLimitResetCredits"]?["availableCount"]?.GetValue<int>() ?? 0;
            return Build(main, asOf: null, result["rateLimitsByLimitId"] as JsonObject, resets);
        }
        finally
        {
            try
            {
                proc.StandardInput.Close(); // stdio server exits on EOF
                if (!proc.WaitForExit(2000)) proc.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Already gone.
            }
        }
    }

    static string? FindCli()
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in new[] { "codex.exe", "codex.cmd" })
            foreach (var dir in dirs)
            {
                var path = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    // --- Fallback: session logs -------------------------------------------------------------

    // Many sessions (subagents, automations) never log rate limits; remember per file so we
    // don't re-scan unchanged files on every refresh.
    static readonly Dictionary<string, (long Length, DateTime Mtime, Snapshot? Hit)> Cache = [];

    sealed record Snapshot(JsonObject Limits, DateTimeOffset At);

    static ProviderStatus ReadLogs()
    {
        try
        {
            foreach (var file in RecentSessionFiles())
            {
                if (LatestSnapshot(file) is { } hit)
                    return Build(hit.Limits, hit.At, others: null, resets: 0);
            }
            return new("Codex", [], Error: L.T("no limit data in recent sessions", "geen limietdata in recente sessies"));
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

    // --- Shared: both sources carry the same shape, snake_case in logs, camelCase live ------

    static JsonNode? Get(JsonNode? n, string snake, string camel) => n?[snake] ?? n?[camel];

    static ProviderStatus Build(JsonObject main, DateTimeOffset? asOf, JsonObject? others, int resets)
    {
        var at = asOf ?? DateTimeOffset.Now;
        var windows = Windows(main, at, label: null).OrderBy(w => w.Length).ToList();

        // Secondary pools (e.g. a model-specific reserve) only once they're in use.
        var mainId = Get(main, "limit_id", "limitId")?.ToString();
        foreach (var (id, other) in others ?? new JsonObject())
        {
            if (id == mainId || other is not JsonObject o) continue;
            var label = Get(o, "limit_name", "limitName")?.ToString() ?? id;
            windows.AddRange(Windows(o, at, label).Where(w => w.UsedPct > 0));
        }

        string? credits = null;
        if (Get(main, "credits", "credits") is JsonObject c && Get(c, "has_credits", "hasCredits")?.GetValue<bool>() == true)
        {
            credits = Get(c, "unlimited", "unlimited")?.GetValue<bool>() == true ? "credits ∞"
                : double.TryParse(Get(c, "balance", "balance")?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var bal)
                    ? $"{bal:0} credits" : null;
        }

        var resetNote = resets switch { 0 => null, 1 => "1 reset", _ => $"{resets} resets" };

        // Log snapshots are only written while Codex is in use, so flag old ones.
        var stale = asOf is { } t && DateTimeOffset.Now - t > TimeSpan.FromMinutes(15)
            ? L.T($"as of {Fmt.AsOf(t)}", $"stand {Fmt.AsOf(t)}")
            : null;

        return new("Codex", windows, Get(main, "plan_type", "planType")?.ToString(), Fmt.Join(credits, resetNote, stale));
    }

    static IEnumerable<UsageWindow> Windows(JsonObject limits, DateTimeOffset at, string? label) =>
        new[] { limits["primary"], limits["secondary"] }
            .Select(n => ToWindow(n, at, label))
            .OfType<UsageWindow>();

    static UsageWindow? ToWindow(JsonNode? n, DateTimeOffset at, string? label)
    {
        if (n is null) return null;
        var used = Get(n, "used_percent", "usedPercent")?.GetValue<double>() ?? 0;
        var length = TimeSpan.FromMinutes(Get(n, "window_minutes", "windowDurationMins")?.GetValue<double>() ?? 0);
        DateTimeOffset? reset =
            Get(n, "resets_at", "resetsAt") is JsonValue ra ? DateTimeOffset.FromUnixTimeSeconds((long)ra.GetValue<double>())
            : n["resets_in_seconds"] is JsonValue ri ? at.AddSeconds(ri.GetValue<double>())
            : null;

        // Window rolled over since the snapshot was written.
        if (reset <= DateTimeOffset.Now) (used, reset) = (0, null);

        label ??= length.TotalDays >= 1 ? $"{length.TotalDays:0}d" : $"{length.TotalHours:0}h";
        return new(label, used, length, reset);
    }
}
