// DLSSG Launcher Setup - a self-contained installer: the launcher and the mod files ship inside it (payload.zip).
//
//   DLSSG_Launcher_Setup.exe                 wizard
//   ... /S [/D=<folder>]                    silent install (per user, no admin needed for the default folder)
//   ... /PORTABLE /D=<folder>               just the files: no shortcuts, no "Installed apps" entry
//   ... /NOSHORTCUTS  /NOLAUNCH
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DlssgUi;
using Microsoft.Win32;

[assembly: AssemblyTitle("DLSSG Launcher Setup")]
[assembly: AssemblyProduct("DLSSG Launcher Setup")]
[assembly: AssemblyDescription("Installs DLSSG Launcher and the DLSSG SM86 frame generation mod")]
[assembly: AssemblyCompany("DLSSG Launcher")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace DlssgSetup
{
    class Options
    {
        public string Dir;
        public bool Silent, Auto, Desktop = true, StartMenu = true, Register = true, Launch = true;
    }

    class NeedsAdminException : Exception { public NeedsAdminException(string dir) : base("Windows does not allow writing to:\n" + dir) { } }

    static class Info
    {
        public static string Version = "2.0.0", Mod = "", License = "";
        public static long Bytes;
        public static void Load()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("setup_info.txt"))
                if (s != null)
                    using (var r = new StreamReader(s))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            int eq = line.IndexOf('=');
                            if (eq < 0) continue;
                            string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                            if (k == "version") Version = v; else if (k == "mod") Mod = v; else if (k == "bytes") long.TryParse(v, out Bytes);
                        }
                    }
            using (var s = asm.GetManifestResourceStream("license.txt"))
                if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) License = r.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n");
        }
    }

    static class Installer
    {
        public const string ExeName = "DLSSGLauncher.exe";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DLSSGLauncher";

        public static string DefaultDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\DLSSG Launcher"); } }

        public static string ExistingDir()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    string d = k == null ? null : k.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, ExeName))) return d;
                }
            }
            catch { }
            return null;
        }

        static bool Writable(string dir)
        {
            try { string t = Path.Combine(dir, ".setup_write_test"); File.WriteAllText(t, ""); File.Delete(t); return true; } catch { return false; }
        }

        public static bool IsAdmin()
        {
            try { return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        public static void Run(Options o, Action<string, int> progress, Func<string, string, bool> ask)
        {
            string dir = Path.GetFullPath(o.Dir.Trim().Trim('"')).TrimEnd('\\');
            if (string.Equals(Path.GetPathRoot(dir).TrimEnd('\\'), dir, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a folder, not the root of a drive.");
            // a non-empty folder that is not an earlier install: install into a sub-folder instead of mixing files
            if (Directory.Exists(dir) && !File.Exists(Path.Combine(dir, ExeName)) && Directory.EnumerateFileSystemEntries(dir).Any())
                dir = Path.Combine(dir, "DLSSG Launcher");
            try { Directory.CreateDirectory(dir); }
            catch (UnauthorizedAccessException) { throw new NeedsAdminException(dir); }
            if (!Writable(dir)) throw new NeedsAdminException(dir);
            o.Dir = dir;

            CloseRunning(dir, ask);
            if (progress != null) progress("Preparing...", 0);
            string mod = Path.Combine(dir, "mod");
            if (File.Exists(Path.Combine(dir, ExeName)) && Directory.Exists(mod)) Directory.Delete(mod, true);   // fresh mod files on update
            Extract(dir, progress);

            string exe = Path.Combine(dir, ExeName);
            if (o.Register) Register(dir, exe);
            if (o.StartMenu) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DLSSG Launcher.lnk"), exe, dir);
            if (o.Desktop) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DLSSG Launcher.lnk"), exe, dir);
        }

        static void CloseRunning(string dir, Func<string, string, bool> ask)
        {
            var procs = new List<Process>();
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try { if (p.MainModule.FileName.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)) { procs.Add(p); continue; } } catch { }
                p.Dispose();
            }
            if (procs.Count == 0) return;
            if (ask != null && !ask("DLSSG Launcher is running", "Close it so setup can update it?")) throw new OperationCanceledException("Setup was cancelled.");
            foreach (var p in procs)
            {
                try { p.CloseMainWindow(); if (!p.WaitForExit(4000)) { p.Kill(); p.WaitForExit(3000); } } catch { }
                p.Dispose();
            }
        }

        static void Extract(string dir, Action<string, int> progress)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (s == null) throw new InvalidDataException("This setup file is damaged (no payload). Download it again.");
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    long total = Math.Max(1, zip.Entries.Sum(e => e.Length)), done = 0;
                    int last = -1;
                    var buf = new byte[1 << 20];
                    string root = dir + "\\";
                    foreach (var e in zip.Entries)
                    {
                        string name = e.FullName.Replace('/', '\\');
                        if (name.EndsWith("\\")) { Directory.CreateDirectory(Path.Combine(dir, name)); continue; }
                        string target = Path.GetFullPath(Path.Combine(dir, name));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bad path in the setup data: " + e.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        string tmp = target + ".new";
                        using (var i = e.Open())
                        using (var f = File.Create(tmp))
                        {
                            int r;
                            while ((r = i.Read(buf, 0, buf.Length)) > 0)
                            {
                                f.Write(buf, 0, r); done += r;
                                int pct = (int)(done * 100 / total);
                                if (progress != null && pct != last) { last = pct; progress(name, pct); }
                            }
                        }
                        File.SetLastWriteTime(tmp, e.LastWriteTime.LocalDateTime);
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(tmp, target);
                    }
                }
            }
        }

        static void Register(string dir, string exe)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "DLSSG Launcher");
                k.SetValue("DisplayVersion", Info.Version);
                k.SetValue("Publisher", "DLSSG Launcher");
                k.SetValue("Comments", "Frame generation for RTX 30 / RTX 20 - DLSSG SM86 mod " + Info.Mod);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall /S");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)Math.Max(1, Info.Bytes / 1024), RegistryValueKind.DWord);
            }
        }

        static void Shortcut(string lnk, string exe, string dir)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic sh = Activator.CreateInstance(t);
            try
            {
                dynamic sc = sh.CreateShortcut(lnk);
                sc.TargetPath = exe; sc.WorkingDirectory = dir; sc.IconLocation = exe + ",0";
                sc.Description = "Find your games and play them with DLSS frame generation";
                sc.Save();
                Marshal.FinalReleaseComObject(sc);
            }
            finally { Marshal.FinalReleaseComObject(sh); }
        }

        public static void Elevate(Options o, Form close)
        {
            var a = new StringBuilder("/AUTO");
            if (!o.Desktop) a.Append(" /NODESKTOP");
            if (!o.StartMenu) a.Append(" /NOSTARTMENU");
            if (!o.Launch) a.Append(" /NOLAUNCH");
            if (!o.Register) a.Append(" /PORTABLE");
            a.Append(" \"/D=" + o.Dir + "\"");
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, a.ToString()) { Verb = "runas", UseShellExecute = true });
                close.Close();
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    // ------------------------------------------------------------------------ wizard
    class Banner : Control
    {
        public int Step;
        static readonly string[] Steps = { "Welcome", "Options", "Install", "Done" };
        public Banner() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(28, 44, 14), Theme.Side, LinearGradientMode.Vertical)) g.FillRectangle(b, ClientRectangle);
            int x = Theme.S(28);
            Theme.DrawLogo(g, new RectangleF(x, Theme.S(36), Theme.S(58), Theme.S(58)));
            var fl = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;
            TextRenderer.DrawText(g, "DLSSG Launcher", Theme.H2, new Rectangle(x, Theme.S(108), Width - x - Theme.S(16), Theme.S(28)), Theme.Text, fl);
            TextRenderer.DrawText(g, "Setup  ·  version " + Info.Version, Theme.Small, new Rectangle(x, Theme.S(136), Width - x - Theme.S(16), Theme.S(20)), Theme.Muted, fl);
            int y = Theme.S(196);
            for (int i = 0; i < Steps.Length; i++)
            {
                bool done = i < Step, cur = i == Step;
                var c = new RectangleF(x, y + Theme.S(2), Theme.S(22), Theme.S(22));
                if (done || cur) Theme.FillRound(g, c, c.Width / 2, done ? Theme.AccentPress : Theme.Accent);
                else Theme.StrokeRound(g, c, c.Width / 2, Theme.Border, 1.5f);
                if (done) Theme.DrawGlyph(g, Glyphs.Check, Theme.GlyphSmall, Theme.OnAccent, Rectangle.Round(c));
                else TextRenderer.DrawText(g, (i + 1).ToString(), Theme.Small, Rectangle.Round(c), cur ? Theme.OnAccent : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, Steps[i], cur ? Theme.Bold : Theme.Body, new Rectangle(x + Theme.S(34), y, Width - x - Theme.S(40), Theme.S(26)), cur ? Theme.Text : Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                y += Theme.S(40);
            }
            TextRenderer.DrawText(g, "Frame generation mod:\nDLSSG SM86 " + Info.Mod + " by sdli1995\n(MIT, see the license)", Theme.Small,
                new Rectangle(x, Height - Theme.S(80), Width - x - Theme.S(16), Theme.S(60)), Theme.Muted, fl);
        }
    }

    class SetupForm : DarkForm
    {
        readonly Options o;
        readonly bool upgrade;
        int page;
        readonly Banner banner = new Banner();
        readonly Panel body = new Panel { BackColor = Theme.Bg };
        readonly Panel bar = new Panel { BackColor = Theme.Card };
        readonly FlatButton bBack = new FlatButton("Back", null, BtnKind.Secondary), bNext = new FlatButton("Next", null, BtnKind.Primary), bCancel = new FlatButton("Cancel", null, BtnKind.Ghost);
        readonly List<Panel> pages = new List<Panel>();
        Toggle tAccept, tDesktop, tStart, tLaunch;
        InputBox dirBox;
        Label lblStatus, lblPct, doneTitle, doneBody, doneGlyph;
        ProgressLine bar2;
        bool installing, finished, failed;

        public SetupForm(Options opts)
        {
            o = opts;
            upgrade = Installer.ExistingDir() != null;
            Text = "DLSSG Launcher Setup";
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(800), Theme.S(560));
            int bw = Theme.S(250);
            banner.SetBounds(0, 0, bw, ClientSize.Height);
            body.SetBounds(bw, 0, ClientSize.Width - bw, ClientSize.Height - Theme.S(66));
            bar.SetBounds(bw, ClientSize.Height - Theme.S(66), ClientSize.Width - bw, Theme.S(66));
            Controls.Add(banner); Controls.Add(body); Controls.Add(bar);
            foreach (var b in new[] { bBack, bNext, bCancel }) { b.BackColor = Theme.Card; bar.Controls.Add(b); }
            int r = bar.Width - Theme.S(24);
            bNext.SetBounds(r - Theme.S(120), Theme.S(16), Theme.S(120), Theme.S(36));
            bBack.SetBounds(r - Theme.S(120) - Theme.S(10) - Theme.S(100), Theme.S(16), Theme.S(100), Theme.S(36));
            bCancel.SetBounds(Theme.S(18), Theme.S(16), Theme.S(100), Theme.S(36));
            bBack.Click += delegate { if (page > 0 && page < 2) ShowPage(page - 1); };
            bNext.Click += delegate { Next(); };
            bCancel.Click += delegate { Close(); };
            FormClosing += (s, e) =>
            {
                if (installing) { e.Cancel = true; return; }
                if (!finished && !failed && !Dialogs.Ask(this, "Cancel setup?", "DLSSG Launcher is not installed yet.", "Cancel setup", "Continue")) e.Cancel = true;
            };
            BuildWelcome(); BuildOptions(); BuildProgress(); BuildDone();
            ShowPage(0);
            if (o.Auto) Shown += delegate { tAccept.Checked = true; ShowPage(2); StartInstall(); };
        }

        Panel NewPage()
        {
            var p = new Panel { BackColor = Theme.Bg, Visible = false };
            p.SetBounds(0, 0, body.Width, body.Height);
            body.Controls.Add(p); pages.Add(p);
            return p;
        }

        static Label Lbl(Panel p, string text, Font f, Color c, int x, int y, int w, int h)
        {
            var l = new Label { Text = text, Font = f, ForeColor = c, AutoSize = false, BackColor = Theme.Bg };
            if (h <= 0) h = TextRenderer.MeasureText(text, f, new Size(w, 0), TextFormatFlags.WordBreak).Height + Theme.S(2);
            l.SetBounds(x, y, w, h);
            p.Controls.Add(l);
            return l;
        }

        void BuildWelcome()
        {
            var p = NewPage();
            int x = Theme.S(32), w = p.Width - 2 * x, y = Theme.S(32);
            y = Lbl(p, upgrade ? "Update DLSSG Launcher" : "Welcome", Theme.H1, Theme.Text, x, y, w, 0).Bottom + Theme.S(10);
            string intro = "DLSSG Launcher adds frame generation (2X up to 6X) to games on RTX 30 and RTX 20 graphics cards, using the DLSSG SM86 mod " + Info.Mod +
                ". It finds your games, puts the mod next to the game and starts it in one click - and lets you change the DLSS settings per game.";
            if (upgrade) intro += "\n\nAn earlier version is installed. It is updated in place; your settings and games are kept.";
            y = Lbl(p, intro, Theme.Body, Theme.Mix(Theme.Text, Theme.Muted, 0.3f), x, y, w, 0).Bottom + Theme.S(14);
            var n = new Notice(NoticeKind.Warning, "Single-player and offline games only",
                "Games with anti-cheat (Call of Duty, Valorant, Fortnite, Apex Legends, ...) treat this mod as cheating - you can be banned. The launcher warns you when it detects one.");
            n.BackColor = Theme.Bg;
            int nh = n.HeightFor(w);
            n.SetBounds(x, y, w, nh); p.Controls.Add(n); y += nh + Theme.S(14);
            var lic = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                BackColor = Theme.Card, ForeColor = Theme.Muted, Font = Theme.Small, Text = Info.License, WordWrap = true
            };
            var licHost = new Panel { BackColor = Theme.Card, Padding = new Padding(Theme.S(10)) };
            int lh = p.Height - y - Theme.S(48);
            licHost.SetBounds(x, y, w, lh);
            lic.Dock = DockStyle.Fill;
            lic.HandleCreated += delegate { Native.DarkScroll(lic); };
            licHost.Controls.Add(lic);
            p.Controls.Add(licHost);
            y += lh + Theme.S(12);
            tAccept = new Toggle("I accept the license terms") { BackColor = Theme.Bg };
            tAccept.SetBounds(x, y, w, Theme.S(28));
            tAccept.CheckedChanged += delegate { UpdateButtons(); };
            p.Controls.Add(tAccept);
        }

        void BuildOptions()
        {
            var p = NewPage();
            int x = Theme.S(32), w = p.Width - 2 * x, y = Theme.S(32);
            y = Lbl(p, "Install options", Theme.H1, Theme.Text, x, y, w, 0).Bottom + Theme.S(18);
            y = Lbl(p, "Install folder", Theme.Bold, Theme.Text, x, y, w, 0).Bottom + Theme.S(6);
            dirBox = new InputBox(Glyphs.Folder, null) { BackColor = Theme.Input };
            dirBox.Text = o.Dir;
            var browse = new FlatButton("Browse...", null, BtnKind.Secondary) { BackColor = Theme.Bg };
            int bw = browse.PreferredWidth;
            dirBox.SetBounds(x, y, w - bw - Theme.S(8), Theme.S(36));
            browse.SetBounds(x + w - bw, y, bw, Theme.S(36));
            p.Controls.Add(dirBox); p.Controls.Add(browse);
            browse.Click += delegate
            {
                string f = FolderPicker.Pick(this, "Choose where to install DLSSG Launcher", Directory.Exists(dirBox.Text) ? dirBox.Text : null);
                if (!string.IsNullOrEmpty(f)) dirBox.Text = f.EndsWith("DLSSG Launcher", StringComparison.OrdinalIgnoreCase) ? f : Path.Combine(f, "DLSSG Launcher");
            };
            y += Theme.S(42);
            y = Lbl(p, "Needs about " + (Info.Bytes / 1048576 + 1) + " MB. The default folder needs no administrator rights; Program Files does.", Theme.Small, Theme.Muted, x, y, w, 0).Bottom + Theme.S(24);
            tDesktop = new Toggle("Create a desktop shortcut") { Checked = o.Desktop, BackColor = Theme.Bg };
            tStart = new Toggle("Add to the Start menu") { Checked = o.StartMenu, BackColor = Theme.Bg };
            tLaunch = new Toggle("Start DLSSG Launcher when setup finishes") { Checked = o.Launch, BackColor = Theme.Bg };
            foreach (var t in new[] { tDesktop, tStart, tLaunch }) { t.SetBounds(x, y, w, Theme.S(28)); p.Controls.Add(t); y += Theme.S(38); }
            y += Theme.S(8);
            var n = new Notice(NoticeKind.Info, "What gets installed",
                "The launcher and the mod files. Nothing is put into your games until you press Play in the launcher - and \"Remove mod\" or uninstalling takes it out again.");
            n.BackColor = Theme.Bg;
            n.SetBounds(x, y, w, n.HeightFor(w)); p.Controls.Add(n);
        }

        void BuildProgress()
        {
            var p = NewPage();
            int x = Theme.S(32), w = p.Width - 2 * x, y = Theme.S(32);
            y = Lbl(p, "Installing...", Theme.H1, Theme.Text, x, y, w, 0).Bottom + Theme.S(28);
            lblStatus = Lbl(p, "Preparing...", Theme.Body, Theme.Muted, x, y, w, Theme.S(22)); lblStatus.AutoEllipsis = true;
            y += Theme.S(30);
            bar2 = new ProgressLine { BackColor = Theme.Bg, Height = Theme.S(8) };
            bar2.SetBounds(x, y, w, Theme.S(8)); p.Controls.Add(bar2);
            y += Theme.S(18);
            lblPct = Lbl(p, "", Theme.Small, Theme.Muted, x, y, w, Theme.S(20));
        }

        void BuildDone()
        {
            var p = NewPage();
            int x = Theme.S(32), w = p.Width - 2 * x, y = Theme.S(60);
            doneGlyph = Lbl(p, Glyphs.Accept, Theme.GlyphHuge, Theme.Accent, x, y, Theme.S(70), Theme.S(70));
            y += Theme.S(86);
            doneTitle = Lbl(p, "DLSSG Launcher is ready", Theme.H1, Theme.Text, x, y, w, Theme.S(40));
            y += Theme.S(50);
            doneBody = Lbl(p, "", Theme.Body, Theme.Mix(Theme.Text, Theme.Muted, 0.3f), x, y, w, Theme.S(200));
        }

        void ShowPage(int i)
        {
            page = i;
            for (int k = 0; k < pages.Count; k++) pages[k].Visible = k == i;
            banner.Step = i; banner.Invalidate();
            UpdateButtons();
        }

        void UpdateButtons()
        {
            bBack.Visible = page == 1;
            bCancel.Visible = page < 2;
            bNext.Visible = page != 2;
            bNext.Text = page == 1 ? "Install" : page == 3 ? (failed ? "Close" : "Finish") : "Next";
            bNext.Enabled = page != 0 || (tAccept != null && tAccept.Checked);
            bNext.Invalidate();
        }

        void Next()
        {
            if (page == 0) ShowPage(1);
            else if (page == 1)
            {
                o.Dir = dirBox.Text; o.Desktop = tDesktop.Checked; o.StartMenu = tStart.Checked; o.Launch = tLaunch.Checked;
                if (string.IsNullOrWhiteSpace(o.Dir)) { Dialogs.Warn(this, "Choose a folder", "Enter the folder to install DLSSG Launcher into."); return; }
                ShowPage(2); StartInstall();
            }
            else if (page == 3)
            {
                if (!failed && o.Launch)
                    try { Process.Start(new ProcessStartInfo(Path.Combine(o.Dir, Installer.ExeName)) { WorkingDirectory = o.Dir, UseShellExecute = true }); } catch { }
                Close();
            }
        }

        void StartInstall()
        {
            installing = true;
            bar2.Value = 0;
            var t = new Thread(() =>
            {
                Exception err = null;
                try
                {
                    Installer.Run(o,
                        (f, pct) => BeginInvoke(new Action(() => { lblStatus.Text = f; bar2.Value = pct; lblPct.Text = pct + " %"; })),
                        (title, text) => (bool)Invoke(new Func<bool>(() => Dialogs.Ask(this, title, text, "Close it", "Cancel"))));
                }
                catch (Exception ex) { err = ex; }
                BeginInvoke(new Action(() => Done(err)));
            }) { IsBackground = true };
            t.Start();
        }

        void Done(Exception err)
        {
            installing = false;
            if (err is NeedsAdminException && !Installer.IsAdmin())
            {
                if (Dialogs.Ask(this, "Administrator permission needed", err.Message + "\n\nRestart setup as administrator? (Or go back and pick a folder in your user profile.)", "Restart as admin", "Back"))
                { finished = true; Installer.Elevate(o, this); return; }
                ShowPage(1); return;
            }
            if (err != null)
            {
                failed = true;
                doneGlyph.Text = Glyphs.Error; doneGlyph.ForeColor = Theme.Danger;
                doneTitle.Text = "Setup could not finish";
                doneBody.Text = err.Message + "\n\nNothing in your games was changed. Close any running DLSSG Launcher and try again.";
            }
            else
            {
                finished = true;
                doneBody.Text = "Installed in:\n" + o.Dir + "\n\n" +
                    "Open DLSSG Launcher, pick a game and press Play with DLSS mod. Then turn on frame generation in the game's graphics menu.\n\n" +
                    (o.Register ? "To uninstall: Windows Settings > Apps > Installed apps > DLSSG Launcher." : "Portable install: delete the folder to remove it (use Remove mod in the launcher first).");
            }
            ShowPage(3);
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Init();
            Info.Load();
            var o = new Options();
            string dir = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i], up = a.ToUpperInvariant();
                if (up == "/S" || up == "/SILENT") o.Silent = true;
                else if (up == "/AUTO") o.Auto = true;
                else if (up.StartsWith("/D=")) dir = a.Substring(3).Trim('"');
                else if (up == "/DIR" && i + 1 < args.Length) dir = args[++i];
                else if (up == "/NOSHORTCUTS") { o.Desktop = false; o.StartMenu = false; }
                else if (up == "/NODESKTOP") o.Desktop = false;
                else if (up == "/NOSTARTMENU") o.StartMenu = false;
                else if (up == "/PORTABLE") { o.Register = false; o.Desktop = false; o.StartMenu = false; }
                else if (up == "/NOLAUNCH") o.Launch = false;
            }
            o.Dir = dir ?? Installer.ExistingDir() ?? Installer.DefaultDir;
            if (o.Silent)
            {
                try { Installer.Run(o, null, null); return 0; }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "DLSSG_Launcher_Setup.log"), DateTime.Now + "  " + ex); } catch { }
                    return ex is NeedsAdminException ? 2 : 1;
                }
            }
            Application.Run(new SetupForm(o));
            return 0;
        }
    }
}
