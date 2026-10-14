using System.Runtime.InteropServices;

namespace UsageTray;

/// <summary>
/// Borderless, rounded, topmost tool window laid out in 96-dpi units. It is created once
/// and shown again and again, so it hides instead of closing.
/// </summary>
abstract class Flyout : Form
{
    protected Flyout()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Bg;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    /// <summary>When it last lost focus and hid; the tray click that hid it must not reopen it.</summary>
    public DateTime LastHidden { get; private set; }

    protected void HideOnDeactivate()
    {
        Hide();
        LastHidden = DateTime.Now;
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

    // Alt+F4 would dispose the form, and the next opening would then throw.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true;
        Hide();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
    }

    /// <summary>Sets the size (and, where the flyout owns it, the position) for the current data and DPI.</summary>
    protected abstract void Relayout();

    protected float DpiScale => DeviceDpi / 96f;
    protected int Px(float v) => (int)Math.Round(v * DpiScale);

    protected static void Label(Graphics g, string text, Font font, Color color, Rectangle bounds, bool right = false)
    {
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding
                  | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                  | (right ? TextFormatFlags.Right : TextFormatFlags.Left);
        TextRenderer.DrawText(g, text, font, bounds, color, flags);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
