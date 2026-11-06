using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UsageTray;

/// <param name="LocalId">The desktop app's id for the session ("local_…") when the desktop app runs it.</param>
/// <param name="Entrypoint">How it was started, e.g. "claude-desktop", "cli", "sdk-cli".</param>
/// <param name="Status">"idle", "busy" or "waiting", since <paramref name="StatusSince"/>.</param>
/// <param name="WaitingFor">What a waiting session needs, e.g. "input needed" or "dialog open".</param>
/// <param name="Cwd">The folder it works in.</param>
/// <param name="ParentId">The desktop app's id of the session that started this one, when it did.</param>
sealed record ClaudeSession(string? Title, string? LocalId, string? Entrypoint, string? Status, DateTimeOffset? StatusSince,
    string? WaitingFor = null, string? Cwd = null, string? ParentId = null);

/// <summary>
/// Running Claude Code sessions, from the file Claude Code keeps per process in
/// ~/.claude/sessions/&lt;pid&gt;.json (undocumented, like the usage endpoint).
/// </summary>
static class ClaudeSessions
{
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sessions");

    // The desktop app's own session records: <account>\<organization>\local_<id>.json (undocumented).
    static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");

    const string LocalIdPattern = "^local_[0-9a-f-]+$";

    // A session's parent never changes, so each record is read once.
    static readonly ConcurrentDictionary<string, string?> Parents = [];

    /// <returns>Sessions by pid.</returns>
    public static Dictionary<int, ClaudeSession> Read(IReadOnlyDictionary<int, Proc> procs)
    {
        var sessions = new Dictionary<int, ClaudeSession>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(Dir, "*.json"))
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out var pid)
                    || !procs.TryGetValue(pid, out var proc) || proc.Started is not { } started) continue;
                try
                {
                    // A file outlives a session that crashed; its process start tells a reused pid apart.
                    if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject doc
                        || doc["procStart"]?.ToString() != started.ToFileTimeUtc().ToString()) continue;

                    string? S(string name) => doc[name]?.ToString() is { Length: > 0 } s ? s : null;
                    var localId = S("hostSessionId") is { } id && Regex.IsMatch(id, LocalIdPattern) ? id : null;
                    DateTimeOffset? since = doc["statusUpdatedAt"] is JsonValue v && v.TryGetValue<long>(out var ms)
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                        : null;
                    sessions[pid] = new(S("name"), localId, S("entrypoint"), S("status"), since, S("waitingFor"), S("cwd"),
                        localId is null ? null : ParentOf(localId));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // Being rewritten or just removed: no title for this one.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No sessions folder: no titles.
        }
        return sessions;
    }

    /// <summary>The session that started this desktop session, from the app's record of it.</summary>
    static string? ParentOf(string localId)
    {
        if (Parents.TryGetValue(localId, out var known)) return known;
        try
        {
            var file = Directory.EnumerateDirectories(AppDir)
                .SelectMany(Directory.EnumerateDirectories)
                .Select(folder => Path.Combine(folder, localId + ".json"))
                .FirstOrDefault(File.Exists);
            if (file is null || JsonNode.Parse(File.ReadAllText(file)) is not JsonObject doc) return null;

            var parent = doc["spawnedFrom"] is JsonObject from && from["sessionId"] is JsonValue id
                && id.TryGetValue<string>(out var value) && Regex.IsMatch(value, LocalIdPattern) ? value : null;
            return Parents[localId] = parent;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // No app, or a record being rewritten: try again at the next refresh.
        }
    }
}
