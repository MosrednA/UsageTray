using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace UsageTray;

/// <summary>
/// Borderless flyout above the tray. Per window: label, bar with a pace tick,
/// used %, distance from even pace, time until reset.
/// </summary>
sealed class PopupForm : Form
{
    // Layout in 96-dpi units.
    const int W = 340, Pad = 12, HeadH = 24, RowH = 20, GapH = 8, FootH = 24;
    const int LabelW = 46, PctW = 40, DeltaW = 36, ResetW = 56;

    IReadOnlyList<ProviderStatus> _data = [];
    DateTimeOffset? _updated;
    Rectangle _footer;
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 30_000 };

    public event Action? RefreshRequested;
    public DateTime LastHidden { get; private set; }

    public PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Bg;
        DoubleBuffered = true;
        KeyPreview = true;
        _tick.Tick += (_, _) => Invalidate();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            cp.ExStyle |= 0x80;       // WS_EX_TOOLWINDOW: keep out of Alt+Tab
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var round = 2; // DWMWCP_ROUND (Windows 11)
        DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
    }

    public void SetData(IReadOnlyList<ProviderStatus> data, DateTimeOffset updated)
    {
        _data = data;
        _updated = updated;
        if (!Visible) return;
        Relayout();
        Invalidate();
    }

    public void ShowNearTray()
    {
        Relayout();
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        var margin = Px(10);
        var x = Math.Clamp(cursor.X - Width / 2, area.Left + margin, area.Right - Width - margin);
        var y = cursor.Y <= area.Top ? area.Top + margin : area.Bottom - Height - margin;
        Location = new Point(x, y);
        Show();
        Activate();
        _tick.Start();
    }

    public void RenderTo(IReadOnlyList<ProviderStatus> data, string path)
    {
        _data = data;
        _updated = DateTimeOffset.Now;
        Relayout();
        using var bmp = new Bitmap(Width, Height);
        DrawToBitmap(bmp, new Rectangle(Point.Empty, Size));
        bmp.Save(path);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
        LastHidden = DateTime.Now;
        _tick.Stop();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Hide();
        else if (e.KeyCode is Keys.F5 or Keys.R) RefreshRequested?.Invoke();
        base.OnKeyDown(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (_footer.Contains(e.Location)) RefreshRequested?.Invoke();
        base.OnMouseClick(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
    }

    float DpiScale => DeviceDpi / 96f;
    int Px(float v) => (int)Math.Round(v * DpiScale);

    void Relayout()
    {
        var body = _data.Count == 0
            ? HeadH
            : _data.Sum(p => HeadH + p.Windows.Count * RowH) + GapH * (_data.Count - 1);
        Size = new Size(Px(W), Px(Pad + body + FootH + 4));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var title = new Font("Segoe UI Semibold", Px(13), GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", Px(11.5f), GraphicsUnit.Pixel);
        using var mono = new Font("Consolas", Px(12), GraphicsUnit.Pixel);
        using var monoBold = new Font("Consolas", Px(12), FontStyle.Bold, GraphicsUnit.Pixel);

        int left = Px(Pad), right = Width - Px(Pad), y = Px(Pad);

        if (_data.Count == 0)
        {
            Label(g, "laden…", small, Theme.Muted, new Rectangle(left, y, right - left, Px(HeadH)));
            y += Px(HeadH);
        }

        foreach (var p in _data)
        {
            var head = new Rectangle(left, y, right - left, Px(HeadH));
            Label(g, p.Name, title, Theme.Text, head);
            var nameW = TextRenderer.MeasureText(g, p.Name, title, Size.Empty, TextFormatFlags.NoPadding).Width + Px(12);
            var meta = p.Error ?? Fmt.Join(p.Plan, p.Note);
            if (meta is not null)
                Label(g, meta, small, p.Error is null ? Theme.Muted : Theme.Hot,
                    Rectangle.FromLTRB(left + nameW, y, right, y + Px(HeadH)), right: true);
            y += Px(HeadH);

            foreach (var w in p.Windows)
            {
                DrawRow(g, w, mono, monoBold, left, right, y);
                y += Px(RowH);
            }
            y += Px(GapH);
        }
        if (_data.Count > 0) y -= Px(GapH);

        y += Px(4);
        using (var line = new Pen(Theme.Track, 1))
            g.DrawLine(line, left, y, right, y);
        _footer = new Rectangle(0, y, Width, Height - y);
        var stamp = _updated is { } u ? $"{u:HH:mm} · klik = verversen" : "";
        Label(g, stamp, small, Theme.Muted, Rectangle.FromLTRB(left, y, right, y + Px(FootH)));
        Label(g, "▏pace   + sneller / − trager", small, Theme.Muted,
            Rectangle.FromLTRB(left, y, right, y + Px(FootH)), right: true);
    }

    void DrawRow(Graphics g, UsageWindow w, Font mono, Font monoBold, int left, int right, int y)
    {
        var rowH = Px(RowH);
        var color = Theme.For(w.Level);

        Label(g, w.Label, mono, Theme.Muted, new Rectangle(left, y, Px(LabelW), rowH));

        var resetX = right - Px(ResetW);
        var deltaX = resetX - Px(DeltaW);
        var pctX = deltaX - Px(PctW);
        int barL = left + Px(LabelW), barR = pctX - Px(4);
        float barW = barR - barL, barH = Px(6), barY = y + (rowH - barH) / 2f;

        using (var track = Theme.RoundRect(new RectangleF(barL, barY, barW, barH), barH / 2))
        using (var brush = new SolidBrush(Theme.Track))
            g.FillPath(brush, track);

        var fillW = barW * (float)Math.Clamp(w.UsedPct / 100, 0, 1);
        if (fillW > 0.5f)
        {
            using var fill = Theme.RoundRect(new RectangleF(barL, barY, Math.Max(fillW, barH), barH), barH / 2);
            using var brush = new SolidBrush(color);
            g.FillPath(brush, fill);
        }

        if (w.EvenPct is { } even)
        {
            var tx = barL + barW * (float)(even / 100);
            using var tick = new SolidBrush(Theme.Tick);
            g.FillRectangle(tick, tx - Px(1), y + Px(4), Math.Max(2, Px(2)), rowH - Px(8));
        }

        Label(g, $"{w.UsedPct:0}%", monoBold, Theme.Text, new Rectangle(pctX, y, Px(PctW), rowH), right: true);
        Label(g, Fmt.Delta(w.Delta), mono, w.Delta > 1 ? color : Theme.Ok, new Rectangle(deltaX, y, Px(DeltaW), rowH), right: true);
        var reset = w.ResetsAt is { } r ? Fmt.Span(r - DateTimeOffset.Now) : "—";
        Label(g, reset, mono, Theme.Muted, new Rectangle(resetX, y, Px(ResetW), rowH), right: true);
    }

    static void Label(Graphics g, string text, Font font, Color color, Rectangle bounds, bool right = false)
    {
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding
                  | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                  | (right ? TextFormatFlags.Right : TextFormatFlags.Left);
        TextRenderer.DrawText(g, text, font, bounds, color, flags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tick.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
