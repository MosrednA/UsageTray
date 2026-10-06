using System.Drawing.Imaging;

namespace UsageTray;

/// <summary>Generates the README images and the app icon (`UsageTray --render-assets`).</summary>
static class DocAssets
{
    public static void Render(string docsDir, string assetsDir)
    {
        Directory.CreateDirectory(docsDir);
        Directory.CreateDirectory(assetsDir);

        using (var popup = new PopupForm())
            popup.RenderTo(Demo(), Path.Combine(docsDir, "flyout.png"));
        using (var logo = IconRenderer.Logo(256))
            logo.Save(Path.Combine(docsDir, "logo.png"), ImageFormat.Png);
        using (var strip = TrayIconStrip(48, 16))
            strip.Save(Path.Combine(docsDir, "tray-icons.png"), ImageFormat.Png);
        WriteIco(Path.Combine(assetsDir, "app.ico"), [16, 20, 24, 32, 40, 48, 64, 128, 256]);
    }

    // One window per status color, so the screenshot shows the whole palette.
    static ProviderStatus[] Demo()
    {
        var now = DateTimeOffset.Now;
        return
        [
            new("Codex", [new("7d", 64, TimeSpan.FromDays(7), now.AddHours(79))], "plus", "1240 credits"),
            new("Claude",
            [
                new("5h", 42, TimeSpan.FromHours(5), now.AddMinutes(130)),
                new("7d", 92, TimeSpan.FromDays(7), now.AddHours(52)),
            ], "max"),
        ];
    }

    static Bitmap TrayIconStrip(int size, int gap)
    {
        double[] used = [42, 64, 92];
        var even = new[] { 57.0, 53, 69 };
        var strip = new Bitmap(used.Length * size + (used.Length - 1) * gap, size);
        using var g = Graphics.FromImage(strip);
        for (var i = 0; i < used.Length; i++)
        {
            // Reset time chosen so the window sits at the given even-pace point.
            var length = TimeSpan.FromDays(7);
            var w = new UsageWindow("7d", used[i], length, DateTimeOffset.Now + length * (1 - even[i] / 100));
            using var tile = IconRenderer.Tile(w, size);
            g.DrawImage(tile, i * (size + gap), 0);
        }
        return strip;
    }

    // PNG-compressed ICO (supported since Vista).
    static void WriteIco(string path, int[] sizes)
    {
        var images = sizes.Select(s =>
        {
            using var bmp = IconRenderer.Logo(s);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            var dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            w.Write(dim);
            w.Write(dim);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images) w.Write(image);
    }
}
