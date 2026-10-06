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
                var data = new[] { CodexSource.Read(), ClaudeSource.ReadAsync().GetAwaiter().GetResult() };
                using (var popup = new PopupForm())
                    popup.RenderTo(data.OfType<ProviderStatus>().ToList(), path);
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
