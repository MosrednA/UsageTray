using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace UsageTray;

/// <summary>
/// Runs the official <c>claude auth login</c> behind a small window. In a console its prompt turns off
/// echo and the console's Ctrl+V, so a pasted code seems to go nowhere; here it reaches stdin as one line.
/// </summary>
sealed class ClaudeLoginForm : Form
{
    readonly Process _login = new()
    {
        StartInfo = new ProcessStartInfo("cmd.exe", "/d /c claude auth login")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), // a byte order mark would become part of the code
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        },
    };
    readonly TextBox _code = new()
    {
        BackColor = Theme.Track,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
    };
    readonly Button _submit = new()
    {
        Text = L.T("Sign in", "Inloggen"),
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(8, 0, 0, 0),
    };
    readonly LinkLabel _page = new()
    {
        Text = L.T("No page opened? Open it here", "Geen pagina geopend? Open hem hier"),
        AutoSize = true,
        Visible = false,
        LinkColor = Theme.Text,
        ActiveLinkColor = Theme.Ok,
        Margin = new Padding(0, 8, 0, 0),
    };
    readonly Label _status = new()
    {
        AutoSize = true,
        ForeColor = Theme.Hot,
        Margin = new Padding(0, 8, 0, 0),
    };
    string? _pageUrl;
    bool _started;

    public ClaudeLoginForm()
    {
        Text = L.T("Sign in to Claude", "Inloggen bij Claude");
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AcceptButton = _submit;

        var textWidth = new Size(LogicalToDeviceUnits(440), 0);
        _status.MaximumSize = textWidth;
        _code.Width = LogicalToDeviceUnits(340);
        _code.Margin = new Padding(0, LogicalToDeviceUnits(3), 0, 0);

        var codeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
        codeRow.Controls.Add(_code);
        codeRow.Controls.Add(_submit);

        var body = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16),
        };
        body.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = textWidth,
            Margin = Padding.Empty,
            Text = L.T(
                "Finish signing in in your browser; this window then closes by itself. If the page shows a code, paste it here.",
                "Rond het inloggen af in je browser; dit venster sluit daarna vanzelf. Toont de pagina een code, plak die dan hier."),
        });
        body.Controls.Add(codeRow);
        body.Controls.Add(_page);
        body.Controls.Add(_status);
        Controls.Add(body);

        _submit.Click += (_, _) => _ = SubmitAsync();
        _page.LinkClicked += (_, _) => OpenPage();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _code.Focus();
        try
        {
            _login.Start();
            _started = true;
            _login.StandardInput.AutoFlush = true;
            await Task.WhenAll(ReadOutputAsync(), ReadErrorsAsync(), _login.WaitForExitAsync());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowFailure(ex.Message);
            return;
        }
        if (IsDisposed) return; // the user closed the window, which stopped the login

        if (_login.ExitCode == 0) Close();
        else ShowFailure(L.T("Sign-in failed. Close this window and try again.", "Inloggen mislukt. Sluit dit venster en probeer het opnieuw."));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_started && !_login.HasExited) _login.Kill(entireProcessTree: true);
        _login.Dispose();
        base.OnFormClosed(e);
    }

    async Task SubmitAsync()
    {
        var code = _code.Text.Trim();
        if (code.Length == 0 || !_started) return;
        _status.Text = "";
        await _login.StandardInput.WriteLineAsync(code);
    }

    async Task ReadOutputAsync()
    {
        while (await _login.StandardOutput.ReadLineAsync() is { } line)
        {
            if (Regex.Match(line, @"https://\S+") is not { Success: true } url) continue;
            _pageUrl = url.Value;
            if (!IsDisposed) _page.Visible = true;
        }
    }

    async Task ReadErrorsAsync()
    {
        while (await _login.StandardError.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || IsDisposed) continue;
            _status.Text = line;
            _code.Focus();
            _code.SelectAll();
        }
    }

    void OpenPage()
    {
        if (_pageUrl is null) return;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(_pageUrl) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    /// <summary>Keeps the CLI's own error when it gave one; the login cannot continue either way.</summary>
    void ShowFailure(string fallback)
    {
        if (_status.Text.Length == 0) _status.Text = fallback;
        _code.Enabled = _submit.Enabled = false;
    }
}
