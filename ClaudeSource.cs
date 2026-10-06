using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace UsageTray;

/// <summary>
/// Reads subscription usage the same way Claude Code's /usage does: the Claude Code CLI's
/// OAuth credentials (~/.claude/.credentials.json) against the undocumented
/// /api/oauth/usage endpoint. Refreshed tokens are written back so the CLI keeps working.
/// </summary>
static class ClaudeSource
{
    const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    static readonly string CredPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static readonly (string Key, string Label, TimeSpan Length, bool Always)[] Windows =
    [
        ("five_hour", "5h", TimeSpan.FromHours(5), true),
        ("seven_day", "7d", TimeSpan.FromDays(7), true),
        ("seven_day_opus", "opus", TimeSpan.FromDays(7), false),
        ("seven_day_sonnet", "sonnet", TimeSpan.FromDays(7), false),
    ];

    // The usage endpoint is rate limited (Claude Code only calls it on demand), so poll gently,
    // back off on 429 and keep showing the last good numbers in the meantime.
    static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);
    static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
    static ProviderStatus? _last;
    static DateTimeOffset _lastAt, _nextAllowed;
    static TimeSpan _backoff;

    sealed class RateLimitedException(TimeSpan? retryAfter) : Exception("rate limited")
    {
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }

    /// <returns>null when Claude Code isn't installed.</returns>
    public static async Task<ProviderStatus?> ReadAsync()
    {
        if (!Directory.Exists(Path.GetDirectoryName(CredPath))) return null;
        var now = DateTimeOffset.Now;
        if (_last is not null && now < _nextAllowed) return Cached();

        string? plan = null;
        try
        {
            if (!File.Exists(CredPath)
                || JsonNode.Parse(await File.ReadAllTextAsync(CredPath)) is not JsonObject doc
                || doc["claudeAiOauth"] is not JsonObject oauth)
                return LoginNeeded(L.T("not signed in · click to sign in", "niet ingelogd · klik om in te loggen"));

            plan = oauth["subscriptionType"]?.ToString();
            var usage = await FetchUsageAsync(doc, oauth, forceRefresh: false)
                     ?? await FetchUsageAsync(doc, oauth, forceRefresh: true);
            if (usage is null)
                return LoginNeeded(L.T("sign-in expired · click to sign in", "login verlopen · klik om in te loggen"), plan);

            (_last, _lastAt, _backoff, _nextAllowed) = (Build(usage, plan), now, TimeSpan.Zero, now + MinInterval);
            return _last;
        }
        catch (RateLimitedException ex)
        {
            _backoff = _backoff == TimeSpan.Zero ? MinInterval : TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, MaxBackoff.Ticks));
            if (ex.RetryAfter > _backoff) _backoff = ex.RetryAfter.Value;
            _nextAllowed = now + _backoff;
            return _last is not null
                ? Cached()
                : Fail(L.T($"rate limited · retrying at {_nextAllowed:HH:mm}", $"even geblokkeerd · opnieuw om {_nextAllowed:HH:mm}"), plan);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, plan);
        }
    }

    /// <summary>Last good result; windows that have reset since count as empty.</summary>
    static ProviderStatus Cached()
    {
        var now = DateTimeOffset.Now;
        var windows = _last!.Windows
            .Select(w => w.ResetsAt <= now ? w with { UsedPct = 0, ResetsAt = null } : w)
            .ToList();
        var stale = now - _lastAt > MinInterval + TimeSpan.FromMinutes(1)
            ? L.T($"as of {Fmt.AsOf(_lastAt)}", $"stand {Fmt.AsOf(_lastAt)}")
            : null;
        return _last with { Windows = windows, Note = Fmt.Join(_last.Note, stale) };
    }

    static ProviderStatus Fail(string error, string? plan = null) => new("Claude", [], plan, Error: error);

    static ProviderStatus LoginNeeded(string error, string? plan = null) =>
        new("Claude", [], plan, Error: error, NeedsLogin: true);

    static async Task<JsonObject?> FetchUsageAsync(JsonObject doc, JsonObject oauth, bool forceRefresh)
    {
        if (await AccessTokenAsync(doc, oauth, forceRefresh) is not { } token) return null;

        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        using var res = await Http.SendAsync(req);
        if (res.StatusCode == HttpStatusCode.Unauthorized) return null;
        if (res.StatusCode == HttpStatusCode.TooManyRequests) throw new RateLimitedException(res.Headers.RetryAfter?.Delta);
        res.EnsureSuccessStatusCode();
        return JsonNode.Parse(await res.Content.ReadAsStringAsync()) as JsonObject;
    }

    static async Task<string?> AccessTokenAsync(JsonObject doc, JsonObject oauth, bool force)
    {
        var expiresAt = oauth["expiresAt"]?.GetValue<long>() ?? 0;
        if (!force && DateTimeOffset.FromUnixTimeMilliseconds(expiresAt) > DateTimeOffset.Now.AddMinutes(1))
            return oauth["accessToken"]?.ToString();

        if (oauth["refreshToken"]?.ToString() is not { Length: > 0 } refresh) return null;
        var scopes = oauth["scopes"] is JsonArray a
            ? string.Join(' ', a.Select(s => s?.ToString()))
            : "user:profile user:inference";

        using var res = await Http.PostAsJsonAsync(TokenUrl, new
        {
            grant_type = "refresh_token",
            refresh_token = refresh,
            client_id = ClientId,
            scope = scopes,
        });
        if (!res.IsSuccessStatusCode) return null;

        var body = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
        var access = body["access_token"]!.ToString();
        oauth["accessToken"] = access;
        oauth["refreshToken"] = body["refresh_token"]?.ToString() ?? refresh;
        oauth["expiresAt"] = DateTimeOffset.Now.AddSeconds(body["expires_in"]?.GetValue<double>() ?? 3600).ToUnixTimeMilliseconds();

        // Write via temp file so a crash never leaves the CLI with a truncated credentials file.
        var tmp = CredPath + ".usagetray.tmp";
        await File.WriteAllTextAsync(tmp, doc.ToJsonString());
        File.Move(tmp, CredPath, overwrite: true);
        return access;
    }

    static ProviderStatus Build(JsonObject usage, string? plan)
    {
        var windows = new List<UsageWindow>();
        foreach (var (key, label, length, always) in Windows)
        {
            if (usage[key] is not JsonObject w || w["utilization"] is not JsonValue u) continue;
            var used = u.GetValue<double>();
            if (!always && used <= 0) continue;
            DateTimeOffset? reset = DateTimeOffset.TryParse(w["resets_at"]?.ToString(), CultureInfo.InvariantCulture, out var r) ? r : null;
            windows.Add(new(label, used, length, reset));
        }

        var extra = usage["extra_usage"] is JsonObject x && x["is_enabled"]?.GetValue<bool>() == true
            ? $"extra {(x["utilization"]?.GetValue<double>() ?? 0):0}%"
            : null;

        return new("Claude", windows, plan, extra);
    }
}
