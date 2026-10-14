using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace UsageTray;

sealed record Proc(int Pid, int ParentPid, string Name, long Commit, DateTime? Started, string? CommandLine);

/// <summary>One process as shown in the flyout; <see cref="Started"/> guards against PID reuse when ending it.</summary>
sealed record ProcRow(int Pid, DateTime? Started, string Name, string Detail, long Commit, bool Protected);

/// <summary>An app: a root process plus its descendants. <see cref="Members"/> has the root first.</summary>
/// <param name="Link">Opens the app's own view of it (Claude sessions: claude://… in the desktop app).</param>
sealed record ProcGroup(string Title, string Detail, long Commit, IReadOnlyList<ProcRow> Members, string? Link = null)
{
    public int RootPid => Members[0].Pid;
    public bool Protected => Members[0].Protected;
}

/// <summary>
/// Running processes grouped per app and sorted by commit (private bytes), the number that
/// matters for memory pressure: idle processes are paged out, so their working set looks small.
/// </summary>
static class ProcessSource
{
    // Children of these start their own group instead of joining the parent's.
    static readonly HashSet<string> Launchers = new(StringComparer.OrdinalIgnoreCase)
        { "explorer", "sihost", "svchost", "services", "wininit", "winlogon", "userinit" };

    static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
        { "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "fontdrvhost" };

    public static IReadOnlyList<ProcGroup> Read(int top = 40)
    {
        var procs = Snapshot();

        // A parent PID only counts when that process is older than the child (PIDs get reused).
        Proc? Parent(Proc p) => procs.TryGetValue(p.ParentPid, out var a) && a.Pid != p.Pid && a.Started <= p.Started ? a : null;

        bool NestedCli(Proc p)
        {
            var guard = 0;
            for (var a = Parent(p); a is not null && guard++ < 64; a = Parent(a))
                if (IsCli(a)) return true;
            return false;
        }

        var kids = new Dictionary<int, List<Proc>>();
        var roots = new List<Proc>();
        foreach (var p in procs.Values)
        {
            // Claude Code sessions are apps of their own, not part of whatever launched them.
            if (Parent(p) is { } parent && !Launchers.Contains(parent.Name) && !(IsCli(p) && !NestedCli(p)))
            {
                if (!kids.TryGetValue(parent.Pid, out var list)) kids[parent.Pid] = list = [];
                list.Add(p);
            }
            else roots.Add(p);
        }

        List<Proc> Tree(Proc root)
        {
            var all = new List<Proc> { root };
            for (var i = 0; i < all.Count; i++)
                if (kids.TryGetValue(all[i].Pid, out var list)) all.AddRange(list);
            return all;
        }

        var desktop = procs.Values.Where(p => IsDesktop(p) && !p.CommandLine!.Contains("--type=")).Select(p => p.Pid).ToHashSet();
        var sessions = ClaudeSessions.Match(roots.Where(IsCli).ToList(), desktop);

        return roots
            .Select(r => (Root: r, All: Tree(r)))
            .Select(x => (x.Root, x.All, Commit: x.All.Sum(p => p.Commit)))
            .OrderByDescending(x => x.Commit)
            .Take(top)
            .Select(x => Build(x.Root, x.All, x.Commit, sessions, desktop))
            .ToList();
    }

    static ProcGroup Build(Proc root, List<Proc> all, long commit, Dictionary<int, ClaudeSession> sessions, HashSet<int> desktop)
    {
        var browser = root.Name.ToLowerInvariant() is "chrome" or "msedge";
        var title = root.Name;
        string? detail, link = null;
        if (IsCli(root))
        {
            sessions.TryGetValue(root.Pid, out var s);
            title = s?.Title is { Length: > 0 } t ? (s.Exact ? t : "~ " + t) : L.T("Claude Code session", "Claude Code-sessie");
            var kind = desktop.Contains(root.ParentPid) ? null
                : Regex.IsMatch(root.CommandLine!, @"\s(-p|--print)(\s|$)") ? "print" : "cli";
            var idle = s?.LastActivity is { } at ? L.T("idle ", "stil ") + Fmt.Span(DateTimeOffset.Now - at) : null;
            detail = Fmt.Join("claude", kind, idle);
            if (s?.LocalId is { Length: > 0 } id) link = "claude://claude.ai/epitaxy/" + id;
        }
        else if (IsDesktop(root)) (title, detail) = ("Claude desktop", null);
        else if (browser) detail = $"{all.Count(p => Describe(p) == "renderer")} renderers";
        else detail = WindowTitle(root.Pid);

        var members = all.Skip(1).OrderByDescending(p => p.Commit).Prepend(root)
            .Select(p => new ProcRow(p.Pid, p.Started, p.Name, p == root && browser ? "browser" : Describe(p), p.Commit, IsProtected(p)))
            .ToList();
        return new(title, Fmt.Join(detail, all.Count > 1 ? $"{all.Count} proc" : null) ?? "", commit, members, link);
    }

    /// <summary>Ends the given processes; skips protected ones and PIDs that were reused since the snapshot.</summary>
    /// <returns>How many were ended.</returns>
    public static int End(IEnumerable<ProcRow> rows)
    {
        var ended = 0;
        foreach (var row in rows.Where(r => !r.Protected))
        {
            try
            {
                using var p = Process.GetProcessById(row.Pid);
                if (p.StartTime != row.Started) continue;
                p.Kill();
                ended++;
            }
            catch (Exception)
            {
                // Already gone (often because its parent was just ended) or access denied.
            }
        }
        return ended;
    }

    static bool IsProtected(Proc p) => p.Started is null || p.Pid == Environment.ProcessId || Critical.Contains(p.Name);

    static bool IsClaude(Proc p) => p.CommandLine is not null && p.Name.Equals("claude", StringComparison.OrdinalIgnoreCase);
    static bool IsDesktop(Proc p) => IsClaude(p) && Exe(p.CommandLine!).Contains(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase);
    static bool IsCli(Proc p) => IsClaude(p) && !IsDesktop(p);

    static string Exe(string cmd) => cmd.StartsWith('"') ? cmd[1..Math.Max(1, cmd.IndexOf('"', 1))] : cmd.Split(' ')[0];

    /// <summary>Chromium/Electron process role when there is one, else the start of the arguments.</summary>
    static string Describe(Proc p)
    {
        if (p.CommandLine is not { } cmd) return "";
        var type = Regex.Match(cmd, @"--type=([\w-]+)").Groups[1].Value;
        if (type == "renderer") return cmd.Contains("--extension-process") ? "extension" : type;
        if (type == "utility")
        {
            var sub = Regex.Match(cmd, @"--utility-sub-type=([\w.]+)").Groups[1].Value;
            return sub.Length > 0 ? sub[(sub.LastIndexOf('.') + 1)..] : type;
        }
        if (type.Length > 0) return type;

        // Arguments without the directories of paths, so the script or file name is what shows.
        var args = cmd[Math.Min(cmd.Length, Exe(cmd).Length + (cmd.StartsWith('"') ? 2 : 0))..];
        args = Regex.Replace(args, @"[A-Za-z]:\\(?:[^""\\:*?<>|]+\\)+", "").Replace("\"", "").Trim();
        return args.Length > 80 ? args[..80] : args;
    }

    static string? WindowTitle(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.MainWindowTitle is { Length: > 0 } title ? title : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static Dictionary<int, Proc> Snapshot()
    {
        var parents = ParentPids();
        var procs = new Dictionary<int, Proc>();
        var buffer = Marshal.AllocHGlobal(CommandLineBuffer);
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.Id == 0) continue;
                    try
                    {
                        DateTime? started = null;
                        try { started = p.StartTime; }
                        catch (Exception) { /* protected process: memory numbers only */ }

                        // The WSL/Hyper-V VM workers hold their memory as working set, not private bytes.
                        var commit = p.ProcessName.StartsWith("vmmem", StringComparison.OrdinalIgnoreCase)
                            ? Math.Max(p.PrivateMemorySize64, p.WorkingSet64)
                            : p.PrivateMemorySize64;
                        procs[p.Id] = new(p.Id, parents.GetValueOrDefault(p.Id), p.ProcessName, commit, started,
                            started is null ? null : CommandLine(p.Id, buffer));
                    }
                    catch (InvalidOperationException)
                    {
                        // Exited while we were looking.
                    }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return procs;
    }

    static Dictionary<int, int> ParentPids()
    {
        var map = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
        if (snapshot == -1) return map;
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                map[(int)entry.ProcessId] = (int)entry.ParentProcessId;
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return map;
    }

    const int CommandLineBuffer = 0x10100; // UNICODE_STRING header + its 64 KB maximum

    static string? CommandLine(int pid, IntPtr buffer)
    {
        var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (handle == 0) return null;
        try
        {
            if (NtQueryInformationProcess(handle, 60 /* ProcessCommandLineInformation */, buffer, CommandLineBuffer, out _) != 0) return null;
            var text = Marshal.PtrToStructure<UnicodeString>(buffer);
            return text.Buffer == 0 ? null : Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry32
    {
        public uint Size, Usage, ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct UnicodeString
    {
        public ushort Length, MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr buffer, int length, out int returned);
}
