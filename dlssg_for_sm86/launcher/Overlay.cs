using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DlssgLauncher;

static class GameRunner
{
    public static void Start(string? steamAppId, string exe, string? lnk = null)
    {
        if (!string.IsNullOrEmpty(lnk) && File.Exists(lnk))
            Process.Start(new ProcessStartInfo(lnk) { UseShellExecute = true });   // keeps the user's own arguments
        else if (steamAppId != null)
            Process.Start(new ProcessStartInfo("steam://rungameid/" + steamAppId) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true });
    }
}

static class Cli
{
    public static Dictionary<string, string> Parse(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            var key = args[i][2..];
            d[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "";
        }
        return d;
    }

    /// <summary>Shortcut mode: install the mod if needed, start the game, then show only the status window.</summary>
    public static void Play(Dictionary<string, string> a)
    {
        var exe = a["play"];
        a.TryGetValue("name", out var name);
        a.TryGetValue("steam", out var steam);
        a.TryGetValue("lnk", out var lnk);
        name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(exe) : name;
        if (!File.Exists(exe)) { MessageBox.Show("Game program not found:\n" + exe, "DLSSG Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        var dir = Path.GetDirectoryName(exe)!;
        try
        {
            if (!Installer.IsInstalled(dir))
            {
                var payload = Installer.ResolvePayload();
                if (payload == null) { MessageBox.Show("Mod files not found.", "DLSSG Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                Installer.Install(payload, dir);
            }
            GameRunner.Start(string.IsNullOrEmpty(steam) ? null : steam, exe, lnk);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not start the game with the mod:\n" + ex.Message + "\n\nIs the game already running?", "DLSSG Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Application.Run(new Overlay(name, exe, exitWhenClosed: true));
    }

    public static string CreateShortcut(string gameName, string exe, string? steamAppId, string? lnkPath = null)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var safe = Regex.Replace(gameName, "[\\\\/:*?\"<>|]", "").Trim();
        var lnk = Path.Combine(desktop, safe + " (DLSSG).lnk");
        var self = Environment.ProcessPath!;
        var args = $"--play \"{exe}\" --name \"{gameName.Replace("\"", "")}\"" + (steamAppId != null ? $" --steam {steamAppId}" : "") + (lnkPath != null ? $" --lnk \"{lnkPath}\"" : "");
        var t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell not available");
        dynamic sh = Activator.CreateInstance(t)!;
        dynamic s = sh.CreateShortcut(lnk);
        s.TargetPath = self;
        s.Arguments = args;
        s.WorkingDirectory = Path.GetDirectoryName(self);
        s.IconLocation = self + ",0";
        s.Description = "Start " + gameName + " with DLSSG for SM86";
        s.Save();
        return lnk;
    }
}

/// <summary>
/// Small always-on-top status window. It never takes focus. It inspects the running game's loaded modules:
/// the proxy DLL from the game folder, the mod backend, and NVIDIA's DLSS-G runtime loaded from dlssg_sm86\runtime.
/// </summary>
sealed class Overlay : Form
{
    static Overlay? current;

    public static void ShowFor(string gameName, string exe)
    {
        current?.Close();
        var o = new Overlay(gameName, exe, false);
        o.Show();
    }

    static readonly string[] ProxyNames = { "version.dll", "winmm.dll", "dbghelp.dll", "dinput8.dll", "dxgi.dll", "d3d12.dll" };

    readonly string gameName, exe, exeDir, procName;
    readonly bool exitWhenClosed;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly DateTime started = DateTime.UtcNow;
    bool everSeen; int missing;
    Color dot = Theme.Dim; string head = "Waiting for the game to start...", detail = "", errLine = "";

    public Overlay(string gameName, string exe, bool exitWhenClosed)
    {
        this.gameName = gameName; this.exe = exe; this.exitWhenClosed = exitWhenClosed;
        exeDir = Path.GetDirectoryName(exe)!;
        procName = Path.GetFileNameWithoutExtension(exe);
        current = this;

        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; Size = new Size(330, 112);
        BackColor = Theme.Panel; Opacity = 0.94; DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - 18, wa.Top + 18);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && e.X < Width - 28) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } else if (e.X >= Width - 28 && e.Y < 26) Close(); };
        timer.Tick += (_, _) => Poll();
        timer.Start();
        FormClosed += (_, _) => { timer.Stop(); if (current == this) current = null; if (exitWhenClosed) Application.ExitThread(); };
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; return cp; } // NOACTIVATE | TOOLWINDOW
    }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

    void Poll()
    {
        try
        {
            var procs = Process.GetProcessesByName(procName);
            if (procs.Length == 0)
            {
                if (everSeen && ++missing >= 4) { Close(); return; }
                if (!everSeen && (DateTime.UtcNow - started).TotalMinutes > 5) { Close(); return; }
                if (!everSeen) { dot = Theme.Dim; head = "Waiting for the game to start..."; detail = "Launched from the DLSSG launcher"; Invalidate(); }
                return;
            }
            everSeen = true; missing = 0;
            var p = procs.OrderByDescending(x => { try { return x.WorkingSet64; } catch { return 0; } }).First();
            Inspect(p);
        }
        catch (Exception ex) { dot = Theme.Warn; head = "Cannot inspect the game"; detail = ex.Message; Invalidate(); }
    }

    void Inspect(Process p)
    {
        bool proxy = false, backend = false, fg = false, sr = false;
        try
        {
            foreach (ProcessModule m in p.Modules)
            {
                var f = m.FileName; var fl = f.ToLowerInvariant();
                if (fl.Contains(@"\dlssg_sm86\runtime\nvngx_dlssg.dll")) fg = true;
                else if (fl.Contains(@"\dlssg_sm86\sr\nvngx_dlss.dll")) sr = true;
                else if (fl.Contains(@"\dlssg_sm86\backend\sm86_backend.dll")) backend = true;
                else if (ProxyNames.Contains(Path.GetFileName(fl)) &&
                         string.Equals(Path.GetDirectoryName(f), exeDir, StringComparison.OrdinalIgnoreCase)) proxy = true;
            }
        }
        catch
        {
            dot = Theme.Warn; head = "Cannot inspect the game";
            detail = "Windows denied access (anti-cheat?). Run the launcher as administrator."; Invalidate(); return;
        }

        if (fg)
        {
            dot = Theme.Green; head = "Frame Generation: ACTIVE";
            detail = "DLSS-G runtime loaded from the mod" + (sr ? "  |  Super Resolution INT8: ON" : "");
        }
        else if (backend)
        {
            dot = Color.FromArgb(255, 196, 60); head = "Mod loaded - waiting for Frame Generation";
            detail = "Turn on DLSS Frame Generation in the game's graphics settings.";
        }
        else if (proxy)
        {
            var why = Gpu.Problem;
            if (why != null) { dot = Theme.Warn; head = "GPU / driver not supported"; detail = why; }
            else
            {
                dot = Color.FromArgb(255, 196, 60); head = "Mod loaded - waiting for DLSS";
                detail = "Your GPU is supported (" + Gpu.Summary + "). Open the game's graphics settings and turn on DLSS Frame Generation.";
            }
        }
        else
        {
            dot = Theme.Warn; head = "Mod NOT loaded in this game";
            detail = (DateTime.UtcNow - started).TotalSeconds < 25
                ? "The game may still be loading..."
                : "The game did not load version.dll. Try a different proxy from alternatives\\.";
        }
        errLine = LastError(p.Id);
        Invalidate();
    }

    /// <summary>Most recent error-level event this process wrote to the mod's log, if any.</summary>
    string LastError(int pid)
    {
        try
        {
            var log = Path.Combine(exeDir, "dlssg_sm86", "logs", "dlssg_sm86.log");
            if (!File.Exists(log)) return "";
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var take = (int)Math.Min(fs.Length, 96 * 1024);
            fs.Seek(-take, SeekOrigin.End);
            var buf = new byte[take]; fs.ReadExactly(buf, 0, take);
            var lines = System.Text.Encoding.UTF8.GetString(buf).Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var l = lines[i];
                if (!l.Contains($"pid {pid} ") || !Regex.IsMatch(l, @"\s E\s+\{")) continue;
                var m = Regex.Match(l, "\"event\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) return "Last error: " + m.Groups[1].Value;
            }
        }
        catch { }
        return "";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        using (var br = new SolidBrush(dot)) g.FillRectangle(br, 0, 0, 4, Height);

        TextRenderer.DrawText(g, "DLSSG  ·  " + gameName, new Font("Segoe UI", 8f, FontStyle.Bold),
            new Rectangle(16, 8, Width - 52, 18), Theme.Dim, TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
        TextRenderer.DrawText(g, "✕", new Font("Segoe UI", 9f), new Rectangle(Width - 28, 6, 22, 20), Theme.Dim, TextFormatFlags.HorizontalCenter);

        using (var br = new SolidBrush(dot)) g.FillEllipse(br, 16, 36, 12, 12);
        TextRenderer.DrawText(g, head, new Font("Segoe UI Semibold", 10.5f),
            new Rectangle(34, 30, Width - 44, 24), dot == Theme.Dim ? Theme.Text : (dot == Theme.Green ? Theme.Green : Theme.Text),
            TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, detail, new Font("Segoe UI", 8.5f),
            new Rectangle(16, 58, Width - 28, 32), Theme.Dim, TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        if (errLine.Length > 0)
            TextRenderer.DrawText(g, errLine, new Font("Segoe UI", 8f), new Rectangle(16, 90, Width - 28, 18), Theme.Warn,
                TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
    }
}


/// <summary>Looks at the NVIDIA GPU once so the status window can say exactly what is wrong.</summary>
static class Gpu
{
    static string? summary, problem; static bool done;

    static void Read()
    {
        if (done) return; done = true;
        try
        {
            using var q = new System.Management.ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
            string? best = null; double bestDrv = 0; bool anyRtx = false, anyNv = false;
            foreach (System.Management.ManagementObject m in q.Get())
            {
                var name = m["Name"]?.ToString() ?? ""; var drv = m["DriverVersion"]?.ToString() ?? "";
                if (!name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) continue;
                anyNv = true;
                var p = drv.Split('.'); double v = 0;
                if (p.Length == 4) { var d = p[2] + p[3]; if (d.Length >= 5) double.TryParse(d[^5..^2].TrimStart('0') + "." + d[^2..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v); }
                if (name.Contains("RTX", StringComparison.OrdinalIgnoreCase)) { anyRtx = true; if (best == null || v > bestDrv) { best = name.Replace("NVIDIA ", ""); bestDrv = v; } }
            }
            if (!anyNv) problem = "No NVIDIA GPU found. This mod needs an RTX 20 / 30 / 40 / 50 card.";
            else if (!anyRtx) problem = "Only a non-RTX NVIDIA GPU was found. The mod needs an RTX 20 / 30 / 40 / 50 card.";
            else if (bestDrv < 580) problem = $"Driver {bestDrv:0.00} is too old. Update the NVIDIA driver to R580 (580.xx) or newer.";
            else summary = $"{best}, driver {bestDrv:0.00}";
        }
        catch { summary = "GPU check unavailable"; }
    }

    public static string? Problem { get { Read(); return problem; } }
    public static string Summary { get { Read(); return summary ?? ""; } }
}
