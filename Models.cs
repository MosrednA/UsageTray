namespace UsageTray;

enum Level { Ok, Warn, Hot }

/// <summary>One rolling limit window, e.g. "Codex 7d" or "Claude 5h".</summary>
sealed record UsageWindow(string Label, double UsedPct, TimeSpan Length, DateTimeOffset? ResetsAt)
{
    /// <summary>Where usage would be now if spread evenly over the window.</summary>
    public double? EvenPct
    {
        get
        {
            if (ResetsAt is not { } reset || Length <= TimeSpan.Zero) return null;
            var elapsed = DateTimeOffset.Now - (reset - Length);
            return Math.Clamp(elapsed / Length, 0, 1) * 100;
        }
    }

    /// <summary>Positive = burning faster than even pace.</summary>
    public double? Delta => UsedPct - EvenPct;

    public Level Level =>
        UsedPct >= 90 || Delta > 15 ? Level.Hot
        : Delta > 1 ? Level.Warn
        : Level.Ok;
}

sealed record ProviderStatus(
    string Name,
    IReadOnlyList<UsageWindow> Windows,
    string? Plan = null,
    string? Note = null,
    string? Error = null,
    bool NeedsLogin = false);

static class Fmt
{
    public static string Span(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalHours < 1) return $"{(int)t.TotalMinutes}m";
        if (t.TotalDays < 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        return $"{(int)t.TotalDays}d {t.Hours}h";
    }

    public static string AsOf(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return local.Date == DateTime.Today ? $"{local:HH:mm}" : $"{local:d MMM HH:mm}";
    }

    public static string Delta(double? d) => d switch
    {
        null => "",
        < 0.5 and > -0.5 => "±0",
        > 0 => $"+{d:0}",
        _ => $"−{-d:0}",
    };

    public static string? Join(params string?[] parts)
    {
        var joined = string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
        return joined.Length == 0 ? null : joined;
    }
}
