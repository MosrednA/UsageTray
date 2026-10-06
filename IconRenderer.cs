using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace UsageTray;

/// <summary>Tray icon: a status-colored tile showing the % of the most pressing window.</summary>
static class IconRenderer
{
    public static Icon Render(UsageWindow? focus)
    {
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using (var tile = Theme.RoundRect(new RectangleF(0, 0, size, size), size * 0.22f))
            using (var fill = new SolidBrush(focus is null ? Theme.Muted : Theme.For(focus.Level)))
                g.FillPath(fill, tile);

            var text = focus is null ? "?" : focus.UsedPct >= 99.5 ? "!" : $"{focus.UsedPct:0}";
            using var font = new Font("Segoe UI", size * (text.Length > 1 ? 0.64f : 0.8f), FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var ink = new SolidBrush(Theme.Bg);
            g.DrawString(text, font, ink, new RectangleF(-1, 0.5f, size + 2, size), format);
        }

        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}
