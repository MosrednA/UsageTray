namespace UsageTray;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        switch (args)
        {
            // Dev aids: render the flyout with live data, or regenerate README images + app icon from demo data.
            case ["--snapshot", var path]:
                ApplicationConfiguration.Initialize();
                var data = new[] { CodexSource.ReadAsync().GetAwaiter().GetResult(), ClaudeSource.ReadAsync().GetAwaiter().GetResult(), SystemSource.Read() };
                using (var popup = new PopupForm())
                    popup.RenderTo(data.OfType<ProviderStatus>().ToList(), path);
                return;

            case ["--procs", var path]:
                ApplicationConfiguration.Initialize();
                using (var procs = new ProcessForm())
                    procs.RenderTo(ProcessSource.Read(), path);
                return;

            // The memory flyout on its own, at the cursor; exits when it closes.
            case ["--procs"]:
                ApplicationConfiguration.Initialize();
                using (var procs = new ProcessForm())
                {
                    procs.VisibleChanged += (_, _) => { if (!procs.Visible) Application.Exit(); };
                    procs.ShowNear(new Rectangle(Cursor.Position, Size.Empty));
                    Application.Run();
                }
                return;

            case ["--preview-icons", var path]:
                DocAssets.PreviewIcons(path);
                return;

            case ["--render-assets"]:
                ApplicationConfiguration.Initialize();
                DocAssets.Render(docsDir: "docs", assetsDir: "assets");
                return;
        }

        using var single = new Mutex(true, @"Local\UsageTray", out var isFirst);
        if (!isFirst) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
