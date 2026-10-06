using System.Diagnostics;
using Microsoft.Win32;

namespace UsageTray;

sealed class TrayApp : ApplicationContext
{
    static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(2);

    readonly PopupForm _popup = new(); // created first: installs the WinForms sync context for awaits
    readonly NotifyIcon _tray = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = (int)RefreshEvery.TotalMilliseconds };
    IReadOnlyList<ProviderStatus> _data = [];
    DateTimeOffset? _updated;
    bool _busy;

    public TrayApp()
    {
        var autostart = new ToolStripMenuItem(L.T("Start with Windows", "Start met Windows"))
        {
            Checked = Autostart.Enabled,
            CheckOnClick = true,
        };
        autostart.CheckedChanged += (_, _) => Autostart.Enabled = autostart.Checked;

        _tray.ContextMenuStrip = new ContextMenuStrip
        {
            Items =
            {
                new ToolStripMenuItem(L.T("Refresh", "Vernieuwen"), null, (_, _) => _ = RefreshAsync()),
                new ToolStripMenuItem(L.T("Sign in to Codex…", "Inloggen bij Codex…"), null, (_, _) => _ = LoginAsync("Codex")),
                new ToolStripMenuItem(L.T("Sign in to Claude…", "Inloggen bij Claude…"), null, (_, _) => _ = LoginAsync("Claude")),
                autostart,
                new ToolStripSeparator(),
                new ToolStripMenuItem(L.T("Quit", "Afsluiten"), null, (_, _) => ExitThread()),
            },
        };
        _tray.Icon = IconRenderer.Render(null);
        _tray.Text = "UsageTray";
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) TogglePopup(); };
        _tray.Visible = true;

        _popup.RefreshRequested += () => _ = RefreshAsync();
        _popup.LoginRequested += provider => _ = LoginAsync(provider);
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    void TogglePopup()
    {
        if (_popup.Visible) { _popup.Hide(); return; }
        // The click that deactivated (and hid) the popup lands here right after; don't reopen.
        if ((DateTime.Now - _popup.LastHidden).TotalMilliseconds < 300) return;

        _popup.SetData(_data, _updated);
        _popup.ShowNearTray();
        if (_updated is null || DateTimeOffset.Now - _updated > TimeSpan.FromSeconds(30)) _ = RefreshAsync();
    }

    async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var codex = Task.Run(CodexSource.ReadAsync);
            var claude = Task.Run(ClaudeSource.ReadAsync);
            _data = new[] { await codex, await claude }.OfType<ProviderStatus>().ToList();
            _updated = DateTimeOffset.Now;
            UpdateTray();
            _popup.SetData(_data, _updated);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Runs the provider's official CLI login in a console window, then refreshes.</summary>
    async Task LoginAsync(string provider)
    {
        var command = provider == "Codex" ? "codex login" : "claude auth login";
        try
        {
            using var login = Process.Start(new ProcessStartInfo("cmd.exe", $"/c title {provider} sign-in & {command} || pause")
            {
                UseShellExecute = true,
            });
            if (login is not null) await login.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(5000, "UsageTray", ex.Message, ToolTipIcon.Error);
        }
        await RefreshAsync();
    }

    void UpdateTray()
    {
        // Icon shows the window most at risk: worst status first, then highest usage.
        var focus = _data.SelectMany(p => p.Windows)
            .OrderByDescending(w => w.Level)
            .ThenByDescending(w => w.UsedPct)
            .FirstOrDefault();
        var old = _tray.Icon;
        _tray.Icon = IconRenderer.Render(focus);
        old?.Dispose();

        var tip = string.Join("\n", _data.Select(p => p.Error is not null
            ? $"{p.Name}: {p.Error}"
            : $"{p.Name}  " + string.Join("  ", p.Windows.Select(w => $"{w.Label} {w.UsedPct:0}% ({Fmt.Delta(w.Delta)})"))));
        if (tip.Length == 0) tip = "UsageTray";
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    protected override void ExitThreadCore()
    {
        _timer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _popup.Dispose();
        base.ExitThreadCore();
    }
}

static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "UsageTray";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is not null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(Name, throwOnMissingValue: false);
        }
    }
}
