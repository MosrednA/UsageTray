namespace UsageTray;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Dev aid: `UsageTray --snapshot out.png` renders the popup with live data and exits.
        if (args is ["--snapshot", var path])
        {
            ApplicationConfiguration.Initialize();
            ProviderStatus[] data = [CodexSource.Read(), ClaudeSource.ReadAsync().GetAwaiter().GetResult()];
            using var popup = new PopupForm();
            popup.RenderTo(data, path);
            return;
        }

        using var single = new Mutex(true, @"Local\UsageTray", out var isFirst);
        if (!isFirst) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
