using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace DlssgLauncher;

/// <summary>Minimal comment-preserving INI model: edits keys in place, never rewrites the user's comments.</summary>
sealed class IniFile
{
    List<string> lines;
    public bool Bom;

    IniFile(List<string> l) { lines = l; }

    public static IniFile Parse(string text) => new(text.Replace("\r\n", "\n").Split('\n').ToList());

    public static IniFile Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var f = Parse(new UTF8Encoding(false).GetString(bytes).TrimStart('﻿'));
        f.Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return f;
    }

    public string Text => string.Join("\r\n", lines);

    public void Save(string path) => File.WriteAllText(path, Text, new UTF8Encoding(Bom));

    static readonly Regex SecRx = new(@"^\s*\[(.+?)\]\s*$");

    (int start, int end) Section(string sec)
    {
        int start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var m = SecRx.Match(lines[i]);
            if (!m.Success) continue;
            if (start >= 0) return (start, i);
            if (m.Groups[1].Value.Equals(sec, StringComparison.OrdinalIgnoreCase)) start = i;
        }
        return start >= 0 ? (start, lines.Count) : (-1, -1);
    }

    int FindKey(string sec, string key, out int secEnd)
    {
        var (s, e) = Section(sec); secEnd = e;
        if (s < 0) return -1;
        for (int i = s + 1; i < e; i++)
        {
            var l = lines[i].TrimStart();
            if (l.StartsWith(';') || l.StartsWith('#')) continue;
            int eq = l.IndexOf('=');
            if (eq > 0 && l[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    public string? Get(string sec, string key)
    {
        int i = FindKey(sec, key, out _);
        if (i < 0) return null;
        var l = lines[i]; return l[(l.IndexOf('=') + 1)..].Trim();
    }

    public void Set(string sec, string key, string value)
    {
        int i = FindKey(sec, key, out int end);
        if (i >= 0) { lines[i] = key + "=" + value; return; }
        var (s, _) = Section(sec);
        if (s < 0)
        {
            while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.Add(""); lines.Add("[" + sec + "]"); lines.Add(key + "=" + value); lines.Add("");
            return;
        }
        int at = end;
        while (at - 1 > s && lines[at - 1].Trim().Length == 0) at--;   // keep blank spacing before the next section
        lines.Insert(at, key + "=" + value);
    }

    public void Remove(string sec, string key)
    {
        int i = FindKey(sec, key, out _);
        if (i >= 0) lines.RemoveAt(i);
    }
}

/// <summary>Row of flat toggle buttons (one value selectable) in the launcher's dark theme.</summary>
sealed class Segmented : FlowLayoutPanel
{
    readonly List<(string v, Button b)> items = new();
    public string Value { get; private set; } = "";
    public event Action? Changed;

    public Segmented()
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; WrapContents = true;
        FlowDirection = FlowDirection.LeftToRight; Margin = new Padding(0, 4, 0, 4); BackColor = Color.Transparent;
        MaximumSize = new Size(640, 0);
    }

    public void SetOptions(IEnumerable<(string v, string label)> opts, string? select)
    {
        Controls.Clear(); items.Clear();
        foreach (var (v, label) in opts)
        {
            var b = new Button { Text = label, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = 32,
                                 Padding = new Padding(10, 0, 10, 0), Margin = new Padding(0, 0, 6, 6), Cursor = Cursors.Hand,
                                 FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Font = new Font("Segoe UI", 9.5f) };
            b.FlatAppearance.BorderSize = 1;
            var value = v;
            b.Click += (_, _) => { if (Enabled && Value != value) { Choose(value); Changed?.Invoke(); } };
            items.Add((v, b)); Controls.Add(b);
        }
        Choose(select ?? items.FirstOrDefault().v ?? "");
    }

    public bool Has(string v) => items.Any(i => i.v == v);

    public void Choose(string v) { Value = v; Paint(); }

    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Paint(); }

    void Paint()
    {
        foreach (var (v, b) in items)
        {
            bool sel = v == Value;
            b.BackColor = sel ? (Enabled ? Theme.Green : Color.FromArgb(70, 100, 50)) : Theme.Panel;
            b.ForeColor = !Enabled ? Color.FromArgb(95, 103, 115) : sel ? Color.FromArgb(14, 20, 10) : Theme.Text;
            b.FlatAppearance.BorderColor = sel && Enabled ? Theme.Green : Theme.Border;
            b.FlatAppearance.MouseOverBackColor = sel ? Theme.GreenDark : Color.FromArgb(40, 47, 58);
            b.FlatAppearance.MouseDownBackColor = sel ? Theme.GreenDark : Theme.Border;
        }
    }
}

sealed class SettingsForm : Form
{
    sealed class Bind
    {
        public string Sec = "", Key = "", Def = "";
        public string? RemoveWhen;
        public Segmented Seg = new();
        public Label Title = new(), Help = new();
        public Func<string, string> HelpFor = _ => "";
        public List<(string, string)> Opts = new();
    }

    readonly string iniPath, exeName;
    readonly string? factoryIni;
    IniFile ini;
    readonly List<Bind> binds = new();
    readonly List<Panel> pages = new();
    readonly List<Button> nav = new();
    readonly TextBox raw = new() { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10f),
                                   BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, AcceptsTab = true };
    readonly Label banner = new() { Dock = DockStyle.Top, Height = 0, ForeColor = Color.FromArgb(255, 196, 60), Padding = new Padding(20, 8, 0, 0) };
    Bind engine = null!, mult = null!;
    int current;
    bool rawDirty, loading;

    public SettingsForm(string gameName, string iniPath, string? factoryIni, string exeName)
    {
        this.iniPath = iniPath; this.factoryIni = factoryIni; this.exeName = exeName;
        ini = IniFile.Load(iniPath);

        Text = "Settings - " + gameName;
        Size = new Size(960, 700); MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9.5f);
        BackColor = Theme.Bg; ForeColor = Theme.Text;
        HandleCreated += (_, _) => Theme.DarkTitleBar(Handle);

        // ---- left navigation
        var left = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = Theme.Panel, Padding = new Padding(10, 16, 10, 10) };
        left.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, left.Width - 1, 0, left.Width - 1, left.Height); };
        var names = new[] { "Frame Generation", "Super Resolution", "Diagnostics", "Advanced", "Raw INI file" };

        // ---- bottom bar
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Panel, Padding = new Padding(16, 12, 16, 12) };
        bottom.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, 0, bottom.Width, 0); };
        var save = Theme.Btn("Save", true); save.Width = 120; save.Dock = DockStyle.Right;
        var cancel = Theme.Btn("Cancel", false); cancel.Width = 100; cancel.Dock = DockStyle.Right;
        var reset = Theme.Btn("Reset to factory", false); reset.Width = 140; reset.Dock = DockStyle.Left;
        var notepad = Theme.Btn("Open in Notepad", false); notepad.Width = 140; notepad.Dock = DockStyle.Left;
        bottom.Controls.AddRange(new Control[] { save, new Panel { Dock = DockStyle.Right, Width = 8 }, cancel, reset, new Panel { Dock = DockStyle.Left, Width = 8 }, notepad });
        save.Click += (_, _) => { if (SaveAll()) { DialogResult = DialogResult.OK; Close(); } };
        cancel.Click += (_, _) => Close();
        reset.Click += (_, _) => ResetFactory();
        notepad.Click += (_, _) => Process.Start(new ProcessStartInfo("notepad.exe", "\"" + iniPath + "\"") { UseShellExecute = true });

        // ---- pages
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        BuildFrameGeneration(); BuildSuperRes(); BuildDiagnostics(); BuildAdvanced(); BuildRaw();
        foreach (var p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; host.Controls.Add(p); }

        for (int i = names.Length - 1; i >= 0; i--)
        {
            int idx = i;
            var b = new Button { Text = "  " + names[i], Dock = DockStyle.Top, Height = 40, TextAlign = ContentAlignment.MiddleLeft,
                                 FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10f) };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => ShowPage(idx);
            nav.Insert(0, b); left.Controls.Add(b);
        }

        // game-running notice
        try
        {
            if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName)).Length > 0)
            { banner.Text = "The game is running. Changes apply the next time you start it."; banner.Height = 34; }
        }
        catch { }

        Controls.Add(host); Controls.Add(banner); Controls.Add(bottom); Controls.Add(left);
        LoadControls();
        ShowPage(0);
    }

    // ------------------------------------------------------------------ page builders

    FlowLayoutPanel NewPage(string title, string subtitle)
    {
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
                                      Padding = new Padding(26, 20, 10, 20), BackColor = Theme.Bg };
        p.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 16f), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 2) });
        p.Controls.Add(new Label { Text = subtitle, AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Theme.Dim, Margin = new Padding(0, 0, 0, 14) });
        pages.Add(p); return p;
    }

    void Heading(FlowLayoutPanel page, string text) =>
        page.Controls.Add(new Label { Text = text.ToUpperInvariant(), AutoSize = true, ForeColor = Theme.Green, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Margin = new Padding(0, 14, 0, 6) });

    Bind Row(FlowLayoutPanel page, string title, string sec, string key, string def, (string v, string label)[] opts,
             Func<string, string> help, string? removeWhen = null)
    {
        var b = new Bind { Sec = sec, Key = key, Def = def, RemoveWhen = removeWhen, HelpFor = help };
        b.Title = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(0, 8, 0, 0) };
        b.Help = new Label { AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Theme.Dim, Margin = new Padding(0, 0, 0, 10) };
        b.Opts = opts.ToList();
        b.Seg.SetOptions(b.Opts, def);
        b.Seg.Changed += () => { b.Help.Text = b.HelpFor(b.Seg.Value); if (!loading) OnChanged(b); };
        page.Controls.Add(b.Title); page.Controls.Add(b.Seg); page.Controls.Add(b.Help);
        binds.Add(b); return b;
    }

    static readonly (string, string)[] OnOff = { ("1", "On"), ("0", "Off") };

    void BuildFrameGeneration()
    {
        var p = NewPage("Frame Generation", "How frames are generated. Restart the game after saving. Settings not shown here keep their safe defaults.");
        Heading(p, "Main");
        Row(p, "Frame generation", "General", "Enabled", "1", OnOff, v => v == "1"
            ? "On: the mod's DLSS-G runs on your GPU."
            : "Off: the game loads its own DLSS-G unchanged. On RTX 20 / 30 that means no frame generation.");
        engine = Row(p, "Engine", "FrameGeneration", "Engine", "DLSS", new[] { ("DLSS", "DLSS-G (NVIDIA)"), ("XeSS", "XeSS-FG (Intel)") }, v => v == "DLSS"
            ? "NVIDIA's DLSS-G model on this project's tuned kernels. Default; fastest on RTX 30 at 4K."
            : "Intel's XeSS-FG model 15. Up to 4X. Faster than DLSS-G at 1440p and below, slower at 4K on RTX 30; uses about 0.4-0.5 GB more VRAM. Cannot run an RGBA16F (HDR) back buffer: it is refused, never falling back.");
        mult = Row(p, "Frames per real frame (multiplier)", "FrameGeneration", "MaxGeneratedFrames", "3", MultOptions("DLSS"), MultHelp);

        Heading(p, "DLSS-G engine only");
        Row(p, "Speed vs. exactness (tier)", "DLSSG", "Optimized", "1",
            new[] { ("0", "0  Stock"), ("1", "1  Exact"), ("2", "2  Faster"), ("3", "3  Fastest") }, v => v switch
            {
                "0" => "Original NVIDIA numerics, no acceleration. Most conservative.",
                "1" => "Default. Every speed-up that keeps the image bit-identical to the official runtime.",
                "2" => "Adds lossy image kernels: about 1-3 % faster per generated frame, no visible difference side by side. Not bit-identical.",
                _ => "All lossy accelerations. Fastest, biggest quality cost."
            });
        Row(p, "Render preset", "DLSSG", "Preset", "A", new[] { ("A", "A  Default"), ("B", "B  UI recompose"), ("C", "C  FG Lite") }, v => v switch
        {
            "A" => "Default: no UI recomposition, no FG Lite.",
            "B" => "Cleaner HUD inside generated frames, but only when the game provides both a HUD-less image and a UI plane. Most games do not; then B looks like A.",
            _ => "FG Lite: 29-47 % less GPU time per generated frame, slightly lower quality (mainly HUD and occlusion edges)."
        });

        Heading(p, "XeSS engine only");
        Row(p, "Optical-flow resolution", "XeSSFG", "FlowResolution", "Middle",
            new[] { ("Coarse", "Coarse"), ("Middle", "Middle"), ("Fine", "Fine"), ("CoarseAt4K", "Coarse at 4K+") }, v => v switch
            {
                "Coarse" => "Fastest, least detail.",
                "Middle" => "Default balance.",
                "Fine" => "Most detail, slowest.",
                _ => "Coarse when the output is 3840x2160 or larger, otherwise Middle."
            });
        Row(p, "HUD handling", "XeSSFG", "UseHudless", "Auto", new[] { ("Auto", "Auto"), ("1", "Always use HUD-less"), ("0", "Never") }, v => v switch
        {
            "1" => "Strict: every frame needs the game's HUD-less image. If the game gives none, it ends with an error.",
            _ => "The whole image, UI included, is interpolated (Auto and 0 behave the same)."
        });
        UpdateDependencies();
    }

    (string, string)[] MultOptions(string eng) => eng == "XeSS"
        ? new[] { ("1", "2X"), ("2", "3X"), ("3", "4X") }
        : new[] { ("0", "Game decides"), ("1", "2X"), ("2", "3X"), ("3", "4X"), ("4", "5X"), ("5", "6X") };

    static string MultHelp(string v) => v switch
    {
        "0" => "Keep whatever the game and the runtime ask for.",
        "1" => "One generated frame per real frame.",
        "2" => "Two generated frames per real frame.",
        "3" => "Default. Three generated frames per real frame. Games that ship an older 4X plug-in stay at 4X whatever you pick.",
        "4" => "Four generated frames per real frame.",
        _ => "6X is the most the bundled runtime supports. It only happens if the game itself offers 6X / Dynamic frame generation. This is a request: the game's own menu may pick less."
    };

    void BuildSuperRes()
    {
        var p = NewPage("Super Resolution", "DLSS Super Resolution upscaling. RTX 30 / RTX 20 only. It only works in games that already have DLSS Super Resolution.");
        Heading(p, "Upscaler");
        Row(p, "Mode", "SuperResolution", "Mode", "Off",
            new[] { ("Off", "Off"), ("M-INT8", "M  INT8"), ("L-INT8", "L  INT8"), ("M-FP16", "M  FP16"), ("L-FP16", "L  FP16") }, v => v switch
            {
                "Off" => "Not touched (default). The game uses its own DLSS Super Resolution.",
                "M-INT8" => "Preset M on every quality mode, on this project's INT8 kernels: about 9-22 % less GPU time per upscale in Performance modes.",
                "L-INT8" => "Preset L on every quality mode, INT8 kernels: about 6-16 % less GPU time per upscale (best with Ultra Performance).",
                "M-FP16" => "Preset M on NVIDIA's own kernels. For comparing against INT8.",
                _ => "Preset L on NVIDIA's own kernels. For comparing against INT8."
            });
        p.Controls.Add(new Label { Text = "Note: on Quality / Balanced / DLAA, presets M and L are slower than the game's own preset K. The gain is in Performance and Ultra Performance. No effect on RTX 40 / 50.",
                                   AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Theme.Dim });
    }

    void BuildDiagnostics()
    {
        var p = NewPage("Diagnostics", "Logging and on-screen markers for finding problems. Leave at the defaults for normal play.");
        Heading(p, "Log file");
        Row(p, "Log level", "Logging", "Level", "1", new[] { ("0", "0  Off"), ("1", "1  Errors"), ("2", "2  Info"), ("3", "3  Trace") }, v => v switch
        {
            "0" => "No log at all.",
            "1" => "Default: errors only. The log folder may stay empty.",
            "2" => "Adds configuration and capability checks. Use this when frame generation will not turn on.",
            _ => "Adds kernel and per-frame traces. Large logs."
        });
        Row(p, "Frame timing file", "Logging", "FrameTiming", "0", OnOff, v => v == "1"
            ? "Writes frame_timing.txt (frame interval, GPU time of super resolution and frame generation per real frame)."
            : "Off.");
        Heading(p, "On-screen");
        Row(p, "Mark generated frames", "Debug", "MarkGeneratedFrames", "0", OnOff, v => v == "1"
            ? "Draws a small marker such as \"FG 1/3\" on generated frames, to tell whether an artifact is on a generated or a real frame."
            : "Off.");
    }

    void BuildAdvanced()
    {
        var p = NewPage("Advanced", "Only change these if instructed, or if you know what they do. Wrong values can stop frame generation from starting.");
        Heading(p, "Compatibility");
        Row(p, "Kernel family", "Compatibility", "Router", "Auto", new[] { ("Auto", "Auto"), ("SM86", "SM86 (RTX 30+)"), ("SM75", "SM75 (RTX 20)") }, v => v == "Auto"
            ? "Picked from your physical GPU. Recommended."
            : "Forces a kernel family. SM75 is experimental. A wrong choice for your GPU will fail.");
        Row(p, "Report architecture to the game", "Compatibility", "SpoofArchToGame", "Auto", new[] { ("Auto", "Auto"), ("1", "Force on"), ("0", "Force off") }, v => v == "Auto"
            ? "Automatic: unlocks the game's frame-generation option on RTX 20 / 30. Recommended."
            : v == "1" ? "Always tell the game a newer architecture." : "Never. The game's gate stays closed on RTX 20 / 30.", removeWhen: "Auto");
        Row(p, "Kernel image format", "Compatibility", "KernelImage", "Auto", new[] { ("Auto", "Auto"), ("PTX", "PTX"), ("Cubin", "Cubin"), ("Original", "Original") }, v => v == "Auto"
            ? "Recommended."
            : "If the driver rejects the chosen image there is no retry: frame generation turns off with an error.");
        Heading(p, "Runtime");
        Row(p, "Runtime source", "Runtime", "Mode", "Bundled", new[] { ("Bundled", "Bundled") }, _ =>
            "The packaged DLSS-G runtime 310.9.16 beside the mod. Other sources are only for experts (edit the Raw INI file).");
    }

    void BuildRaw()
    {
        var p = new Panel { BackColor = Theme.Bg, Padding = new Padding(20) };
        var top = new Label { Dock = DockStyle.Top, Height = 54, ForeColor = Theme.Dim,
                              Text = "Full control: this is the actual dlssg_sm86.ini of this game. Any key from docs\\INSTALL.en.md works here.\r\nVisual pages update when you leave this page; Save writes exactly this text." };
        var wrap = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(10), BorderStyle = BorderStyle.FixedSingle };
        wrap.Controls.Add(raw);
        p.Controls.Add(wrap); p.Controls.Add(top);
        raw.TextChanged += (_, _) => { if (!loading) rawDirty = true; };
        pages.Add(p);
    }

    // ------------------------------------------------------------------ behaviour

    void ShowPage(int i)
    {
        // leaving the raw page: pull edits back into the model and refresh the visual controls
        if (current == pages.Count - 1 && i != current && rawDirty) { ini = IniFile.Parse(raw.Text) is var f ? CopyBom(f) : ini; rawDirty = false; LoadControls(); }
        // entering the raw page: show the current model
        if (i == pages.Count - 1) { PushToModel(); loading = true; raw.Text = ini.Text; loading = false; rawDirty = false; }
        current = i;
        for (int k = 0; k < pages.Count; k++) pages[k].Visible = k == i;
        for (int k = 0; k < nav.Count; k++)
        {
            nav[k].BackColor = k == i ? Theme.Selected : Theme.Panel;
            nav[k].ForeColor = k == i ? Theme.Green : Theme.Text;
        }
    }

    IniFile CopyBom(IniFile f) { f.Bom = ini.Bom; return f; }

    void OnChanged(Bind b)
    {
        if (b == engine) { RebuildMultiplier(); UpdateDependencies(); }
    }

    void RebuildMultiplier()
    {
        var cur = mult.Seg.Value; var eng = engine.Seg.Value;
        var opts = MultOptions(eng);
        string pick = opts.Any(o => o.Item1 == cur) ? cur : (eng == "XeSS" ? "3" : "3");
        loading = true;
        mult.Opts = opts.ToList();
        mult.Seg.SetOptions(mult.Opts, pick);
        mult.Help.Text = mult.HelpFor(pick);
        loading = false;
    }

    void UpdateDependencies()
    {
        bool dlss = engine.Seg.Value == "DLSS";
        foreach (var b in binds)
        {
            bool on = b.Sec switch { "DLSSG" => dlss, "XeSSFG" => !dlss, _ => true };
            b.Seg.Enabled = on;
            b.Title.ForeColor = on ? Theme.Text : Color.FromArgb(95, 103, 115);
        }
    }

    void LoadControls()
    {
        loading = true;
        var eng = (ini.Get("FrameGeneration", "Engine") ?? "DLSS").Equals("XeSS", StringComparison.OrdinalIgnoreCase) ? "XeSS" : "DLSS";
        foreach (var b in binds)
        {
            if (b == engine) b.Seg.Choose(eng);
            var v = ini.Get(b.Sec, b.Key) ?? b.Def;
            if (b == mult) b.Opts = MultOptions(eng).ToList();
            var opts = b.Opts.ToList();
            // keep an unusual existing value visible and selectable instead of silently changing it
            if (!opts.Any(o => o.Item1 == v)) opts.Add((v, v + " (current)"));
            b.Seg.SetOptions(opts, v);
            b.Help.Text = b.HelpFor(b.Seg.Value);
        }
        loading = false;
        UpdateDependencies();
    }

    void PushToModel()
    {
        foreach (var b in binds)
        {
            var v = b.Seg.Value;
            if (b.RemoveWhen != null && v == b.RemoveWhen) { ini.Remove(b.Sec, b.Key); continue; }
            // keys that are absent in the factory file and sit at their default stay absent
            if (ini.Get(b.Sec, b.Key) == null && v == b.Def && b.Sec is "Compatibility" or "Debug") continue;
            ini.Set(b.Sec, b.Key, v);
        }
    }

    bool SaveAll()
    {
        if (current == pages.Count - 1 && rawDirty) { ini = CopyBom(IniFile.Parse(raw.Text)); rawDirty = false; LoadControls(); }
        else PushToModel();
        // the same rules the mod enforces: an out-of-range value is not replaced by a default, it disables the feature
        var eng = ini.Get("FrameGeneration", "Engine") ?? "DLSS";
        if (int.TryParse(ini.Get("FrameGeneration", "MaxGeneratedFrames") ?? "3", out var n))
        {
            if (n < 0 || n > 5) return Fail("MaxGeneratedFrames must be 0-5.");
            if (eng.Equals("XeSS", StringComparison.OrdinalIgnoreCase) && (n < 1 || n > 3)) return Fail("With the XeSS engine, MaxGeneratedFrames must be 1-3 (2X to 4X).");
        }
        else return Fail("MaxGeneratedFrames must be a number.");
        try
        {
            if (File.Exists(iniPath)) File.Copy(iniPath, iniPath + ".bak", true);
            ini.Save(iniPath);
            return true;
        }
        catch (Exception ex) { return Fail("Could not save:\n" + ex.Message); }
    }

    bool Fail(string msg) { MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }

    void ResetFactory()
    {
        if (factoryIni == null || !File.Exists(factoryIni)) { Fail("The factory INI was not found."); return; }
        if (MessageBox.Show(this, "Replace all settings with the factory defaults? (A .bak copy of the current file is kept when you Save.)", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        ini = IniFile.Load(factoryIni);
        LoadControls();
        if (current == pages.Count - 1) { loading = true; raw.Text = ini.Text; loading = false; rawDirty = false; }
    }
}
