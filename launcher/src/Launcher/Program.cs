// Entry point: main window, --play (desktop shortcuts), --uninstall.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DlssgUi;
using Microsoft.Win32;

[assembly: AssemblyTitle("DLSSG Launcher")]
[assembly: AssemblyProduct("DLSSG Launcher")]
[assembly: AssemblyDescription("Finds your games, installs the DLSSG SM86 frame generation mod next to them and starts them")]
[assembly: AssemblyCompany("DLSSG Launcher")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: AssemblyInformationalVersion("2.0.0")]

namespace DlssgLauncher
{
    static class Program
    {
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DLSSGLauncher";

        [STAThread]
        static int Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Init();

            string play = null, select = null, finishDir = null;
            bool uninstall = false, purge = false, silent = false;
            int finishPid = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string k = args[i].TrimStart('-', '/').ToLowerInvariant();
                if (k == "play" && i + 1 < args.Length) play = args[++i];
                else if (k == "select" && i + 1 < args.Length) select = args[++i];
                else if (k == "uninstall") uninstall = true;
                else if (k == "uninstall-finish" && i + 2 < args.Length) { finishDir = args[++i]; int.TryParse(args[++i], out finishPid); }
                else if (k == "purge") purge = true;
                else if (k == "s" || k == "silent") silent = true;
            }

            if (finishDir != null) return Uninstaller.Finish(finishDir, finishPid, purge, silent);

            string err = Core.Init();
            if (uninstall) return Uninstaller.Run(silent);
            if (err != null) { Dialogs.Error(null, "DLSSG Launcher cannot start", err); return 1; }
            if (play != null) return PlayMode.Run(play);

            Application.Run(new MainForm(select));
            Core.SaveSettings();
            return 0;
        }

        public static void RestartElevated(string args, Form close)
        {
            Core.SaveSettings();
            try
            {
                Process.Start(new ProcessStartInfo(Core.ExePath, args ?? "") { Verb = "runas", UseShellExecute = true });
                if (close != null) close.Close(); else Environment.Exit(0);
            }
            catch (System.ComponentModel.Win32Exception) { }   // the UAC prompt was cancelled
        }

        public static bool ConfirmAntiCheat(IWin32Window owner, Game g)
        {
            if (g.AntiCheat == null) return true;
            if (Core.Settings.AckAntiCheat.Contains(g.Exe, StringComparer.OrdinalIgnoreCase)) { Core.Log("Anti-cheat warning already acknowledged for " + g.Name + "."); return true; }
            bool yes = Dialogs.AskDanger(owner, "Anti-cheat warning - " + g.Name,
                g.AntiCheat + "\n\nAnti-cheat software treats proxy DLLs such as version.dll as cheating. Using the mod can get your account BANNED, " +
                "or the game may refuse to start.\n\nOnly use it in offline / single-player games without anti-cheat.", "I understand, continue", "Cancel");
            if (yes) { Core.Settings.AckAntiCheat.Add(g.Exe); Core.SaveSettings(); }
            else Core.Log("Cancelled (anti-cheat warning).");
            return yes;
        }
    }

    // ------------------------------------------------------------------------ --play: desktop shortcuts
    static class PlayMode
    {
        public static int Run(string exe)
        {
            string logPath = Path.Combine(Core.DataDir, "play.log");
            try { if (File.Exists(logPath) && new FileInfo(logPath).Length > 512 * 1024) File.Delete(logPath); } catch { }
            Core.LogLine += l => { try { File.AppendAllText(logPath, l + Environment.NewLine); } catch { } };
            try { exe = Path.GetFullPath(exe.Trim('"')); } catch { }
            if (!File.Exists(exe))
            {
                Dialogs.Error(null, "Game not found", "This shortcut starts:\n" + exe + "\n\nThe game may have moved or been uninstalled. Open DLSSG Launcher to make a new shortcut.");
                return 1;
            }
            Core.Log("Play: " + exe);
            var g = Core.GameForExe(exe);
            if (!Program.ConfirmAntiCheat(null, g)) return 1;
            string proxy = g.State.Ours.Count > 0 ? g.State.Ours[0] : "version.dll";
            int code = 1;
            var splash = new SplashForm(g);
            splash.Shown += delegate
            {
                new Thread(() =>
                {
                    try
                    {
                        bool ok = Core.Install(g, proxy, false,
                            (t, x) => (bool)splash.Invoke(new Func<bool>(() => Dialogs.Ask(splash, t, x, "Replace", "Cancel"))),
                            (t, p) => splash.SetStatusAsync(t, p));
                        if (ok)
                        {
                            splash.SetStatusAsync("Starting " + g.Name + "...", 100);
                            Core.StartGame(g);
                            code = 0;
                            Thread.Sleep(1200);
                        }
                    }
                    catch (Exception ex) { splash.Invoke(new Action(() => Fail(splash, ex, exe))); }
                    finally { try { splash.BeginInvoke(new Action(splash.Close)); } catch { } }
                }) { IsBackground = true }.Start();
            };
            Application.Run(splash);
            Core.SaveSettings();
            return code;
        }

        static void Fail(Form owner, Exception ex, string exe)
        {
            Core.Log("ERROR: " + ex.Message.Replace("\n", " "));
            if ((ex is NeedsAdminException || ex is UnauthorizedAccessException) && !Core.IsAdmin())
            {
                if (Dialogs.Ask(owner, "Administrator permission needed", ex.Message + "\n\nStart again as administrator?", "Restart as admin", "Cancel"))
                    Program.RestartElevated("--play \"" + exe + "\"", null);
                return;
            }
            Dialogs.Error(owner, "Could not start the game", ex.Message);
        }
    }

    class SplashForm : DarkForm
    {
        readonly Game game;
        string status = "Checking the DLSS mod...";
        readonly ProgressLine bar = new ProgressLine { Value = -1 };
        public SplashForm(Game g)
        {
            game = g;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = true;
            Text = "Starting " + g.Name;
            ClientSize = new Size(Theme.S(460), Theme.S(150));
            BackColor = Theme.Card;
            DoubleBuffered = true;
            bar.BackColor = Theme.Card;
            bar.SetBounds(Theme.S(24), Theme.S(118), Theme.S(412), Theme.S(6));
            Controls.Add(bar);
        }
        public void SetStatusAsync(string text, int pct)
        {
            try { BeginInvoke(new Action(() => { status = text; bar.Value = pct >= 100 ? 100 : pct; Invalidate(); })); } catch { }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            int ic = Theme.S(64);
            var ir = new Rectangle(Theme.S(24), Theme.S(28), ic, ic);
            if (game.Icon != null) g.DrawImage(game.Icon, ir); else Theme.DrawLogo(g, ir);
            int tx = ir.Right + Theme.S(18), tw = Width - tx - Theme.S(24);
            TextRenderer.DrawText(g, game.Name, Theme.H2, new Rectangle(tx, Theme.S(32), tw, Theme.S(28)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, status, Theme.Body, new Rectangle(tx, Theme.S(64), tw, Theme.S(22)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Theme.DrawLogo(g, new RectangleF(Width - Theme.S(40), Theme.S(12), Theme.S(22), Theme.S(22)));
        }
    }

    // ------------------------------------------------------------------------ --uninstall
    static class Uninstaller
    {
        public static int Run(bool silent)
        {
            string dir = Core.AppDir;
            bool installed = Directory.Exists(Path.Combine(dir, "mod"));
            if (!installed)
            {
                if (!silent) Dialogs.Error(null, "Nothing to uninstall", "This copy of DLSSG Launcher was not installed by its setup:\n" + dir + "\n\nJust delete the folder.");
                return 1;
            }
            if (!Core.IsWritable(dir) && !Core.IsAdmin()) { Program.RestartElevated("--uninstall" + (silent ? " /S" : ""), null); return 0; }

            var installs = Core.Settings.Installs.Where(d => Directory.Exists(d) && Core.GetModState(d).Kind != ModKind.None).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool removeMods = true, purge = false;
            if (!silent)
            {
                using (var f = new UninstallForm(installs))
                {
                    f.ShowDialog();
                    if (!f.Confirmed) return 1;
                    removeMods = f.RemoveMods; purge = f.Purge;
                }
            }
            var failed = new List<string>();
            if (removeMods)
                foreach (var d in installs)
                {
                    try { Core.Uninstall(d); } catch (Exception ex) { failed.Add(d + "  (" + ex.Message.Replace("\n", " ") + ")"); }
                }
            foreach (var l in Core.Settings.Shortcuts.ToList()) { try { if (File.Exists(l)) File.Delete(l); } catch { } }
            if (failed.Count > 0 && !silent)
                Dialogs.Warn(null, "Some games still have the mod", "These folders could not be cleaned (is the game running?). Remove the mod files there by hand:\n\n" + string.Join("\n", failed));

            string temp = Path.Combine(Path.GetTempPath(), "dlssg_uninstall_" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Core.ExePath, temp, true);
            Process.Start(new ProcessStartInfo(temp, "--uninstall-finish \"" + dir + "\" " + Process.GetCurrentProcess().Id + (purge ? " --purge" : "") + (silent ? " /S" : ""))
            { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
            return 0;
        }

        public static int Finish(string dir, int pid, bool purge, bool silent)
        {
            try { using (var p = Process.GetProcessById(pid)) p.WaitForExit(15000); } catch { }
            string full;
            try { full = Path.GetFullPath(dir).TrimEnd('\\'); } catch { return 1; }
            bool safe = full.Length > 8 && Directory.Exists(Path.Combine(full, "mod")) && File.Exists(Path.Combine(full, "DLSSGLauncher.exe"))
                && !Core.SamePath(full, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                && !Core.SamePath(full, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                && !Core.SamePath(full, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
                && !Core.SamePath(full, Path.GetPathRoot(full));
            bool ok = true;
            if (safe)
            {
                ok = false;
                for (int i = 0; i < 20 && !ok; i++)
                {
                    try { Directory.Delete(full, true); ok = true; } catch { Thread.Sleep(500); }
                }
            }
            foreach (var lnk in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DLSSG Launcher.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DLSSG Launcher.lnk") })
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallKey, false); } catch { }
            if (purge)
                try { Directory.Delete(Core.DefaultDataDir, true); } catch { }
            if (!silent)
            {
                if (ok) Dialogs.Success(null, "DLSSG Launcher was removed", "Thanks for trying it." + (purge ? "" : " Your launcher settings were kept in case you install it again."));
                else Dialogs.Warn(null, "Almost done", "Some files could not be deleted. Remove this folder by hand:\n" + full);
            }
            try
            {
                string self = Application.ExecutablePath;
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + self + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch { }
            return ok ? 0 : 1;
        }
    }

    class UninstallForm : DarkForm
    {
        public bool Confirmed, RemoveMods = true, Purge;
        public UninstallForm(List<string> installs)
        {
            Text = "Uninstall DLSSG Launcher";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            int w = Theme.S(560), pad = Theme.S(26), y = pad;
            var title = new Label { Text = "Uninstall DLSSG Launcher?", Font = Theme.H2, ForeColor = Theme.Text, AutoSize = false };
            title.SetBounds(pad, y, w - 2 * pad, Theme.S(30)); y += Theme.S(40);
            var tMods = new Toggle(installs.Count > 0 ? "Also remove the mod from " + installs.Count + " game folder" + (installs.Count == 1 ? "" : "s") + " (recommended)" : "No game has the mod installed by this launcher") { Checked = installs.Count > 0, Enabled = installs.Count > 0 };
            tMods.SetBounds(pad, y, w - 2 * pad, Theme.S(28)); y += Theme.S(34);
            Controls.Add(title); Controls.Add(tMods);
            if (installs.Count > 0)
            {
                var n = new Notice(NoticeKind.Info, null, string.Join("\n", installs.Take(8)) + (installs.Count > 8 ? "\n..." : ""));
                n.BackColor = Theme.Bg;
                int nh = n.HeightFor(w - 2 * pad);
                n.SetBounds(pad, y, w - 2 * pad, nh); y += nh + Theme.S(12);
                Controls.Add(n);
                tMods.CheckedChanged += delegate { n.Visible = tMods.Checked; };
            }
            var tPurge = new Toggle("Delete my launcher settings too");
            tPurge.SetBounds(pad, y, w - 2 * pad, Theme.S(28)); y += Theme.S(48);
            Controls.Add(tPurge);
            var bYes = new FlatButton("Uninstall", Glyphs.Delete, BtnKind.Danger) { BackColor = Theme.Bg };
            var bNo = new FlatButton("Cancel", null, BtnKind.Secondary) { BackColor = Theme.Bg };
            bNo.SetBounds(w - pad - Theme.S(100), y, Theme.S(100), Theme.S(36));
            bYes.SetBounds(w - pad - Theme.S(110) - Theme.S(130), y, Theme.S(130), Theme.S(36));
            bYes.Click += delegate { Confirmed = true; RemoveMods = tMods.Checked && installs.Count > 0; Purge = tPurge.Checked; Close(); };
            bNo.Click += delegate { Close(); };
            Controls.Add(bYes); Controls.Add(bNo);
            ClientSize = new Size(w, y + Theme.S(36) + pad);
            ActiveControl = bNo;
        }
    }
}
