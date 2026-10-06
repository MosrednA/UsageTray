using Microsoft.Win32;

namespace UsageTray;

sealed class TrayApp : ApplicationContext
{
    static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(2);

    readonly PopupForm _popup = new(); // created first: installs the WinForms sync context for awaits
    readonly NotifyIcon _tray = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = (int)RefreshEvery.TotalMilliseconds };
    IReadOnlyList<ProviderStatus> _data = [];
    DateTimeOffset _updated;
    bool _busy;

    public TrayApp()
    {
        var autostart = new ToolStripMenuItem("Start met Windows") { Checked = Autostart.Enabled, CheckOnClick = true };
        autostart.CheckedChanged += (_, _) => Autostart.Enabled = autostart.Checked;

        _tray.ContextMenuStrip = new ContextMenuStrip
        {
            Items =
            {
                new ToolStripMenuItem("Vernieuwen", null, (_, _) => _ = RefreshAsync()),
                autostart,
                new ToolStripSeparator(),
                new ToolStripMenuItem("Afsluiten", null, (_, _) => ExitThread()),
            },
        };
        _tray.Icon = IconRenderer.Render(null);
        _tray.Text = "UsageTray — laden…";
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) TogglePopup(); };
        _tray.Visible = true;

        _popup.RefreshRequested += () => _ = RefreshAsync();
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
        if (DateTimeOffset.Now - _updated > TimeSpan.FromSeconds(30)) _ = RefreshAsync();
    }

    async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var codex = Task.Run(CodexSource.Read);
            var claude = Task.Run(ClaudeSource.ReadAsync);
            _data = [await codex, await claude];
            _updated = DateTimeOffset.Now;
            UpdateTray();
            _popup.SetData(_data, _updated);
        }
        finally
        {
            _busy = false;
        }
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
