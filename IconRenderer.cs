using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace UsageTray;

static class IconRenderer
{
    /// <summary>Tray icon: a status-colored tile showing the % of the most pressing window.</summary>
    public static Icon Render(UsageWindow? focus)
    {
        using var bmp = Tile(focus, Math.Max(16, SystemInformation.SmallIconSize.Width));
        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    public static Bitmap Tile(UsageWindow? focus, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (var tile = Theme.RoundRect(new RectangleF(0, 0, size, size), size * 0.22f))
        using (var fill = new SolidBrush(focus is null ? Theme.Muted : Theme.For(focus.Level)))
            g.FillPath(fill, tile);

        var text = focus is null ? "?" : focus.UsedPct >= 99.5 ? "!" : $"{focus.UsedPct:0}";
        using var font = new Font("Segoe UI", size * (text.Length > 1 ? 0.64f : 0.8f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var ink = new SolidBrush(Theme.Bg);
        g.DrawString(text, font, ink, new RectangleF(-1, size / 32f, size + 2, size), format);
        return bmp;
    }

    /// <summary>App logo: two usage bars crossed by a pace tick.</summary>
    public static Bitmap Logo(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var inset = size * 0.03f;
        using (var tile = Theme.RoundRect(new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset), size * 0.22f))
        using (var bg = new SolidBrush(Theme.Bg))
            g.FillPath(bg, tile);

        float left = size * 0.2f, width = size * 0.6f, barH = Math.Max(2f, size * 0.12f);
        (float Y, float Fill, Color Color)[] bars = [(size * 0.36f, 0.72f, Theme.Warn), (size * 0.64f, 0.4f, Theme.Ok)];
        foreach (var (y, fill, color) in bars)
        {
            var top = y - barH / 2;
            using (var track = Theme.RoundRect(new RectangleF(left, top, width, barH), barH / 2))
            using (var brush = new SolidBrush(Theme.Track))
                g.FillPath(brush, track);
            using (var bar = Theme.RoundRect(new RectangleF(left, top, width * fill, barH), barH / 2))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, bar);
        }

        var tickW = Math.Max(1.5f, size * 0.045f);
        var tickX = left + width * 0.55f - tickW / 2;
        using var tick = new SolidBrush(Theme.Tick);
        g.FillRectangle(tick, tickX, size * 0.24f, tickW, size * 0.52f);
        return bmp;
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}
