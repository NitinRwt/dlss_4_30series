// Game discovery, mod install / update / removal, dlssg_sm86.ini editing, settings.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using DlssgUi;
using Microsoft.Win32;

namespace DlssgLauncher
{
    public enum ModKind { None, Partial, Outdated, Installed }

    public class ModState
    {
        public List<string> Ours = new List<string>();
        public List<string> Foreign = new List<string>();
        public bool HasFolder, HasIni, Outdated;
        public ModKind Kind
        {
            get
            {
                if (Ours.Count > 0 && HasFolder && HasIni) return Outdated ? ModKind.Outdated : ModKind.Installed;
                if (HasFolder && Foreign.Count > 0) return ModKind.Outdated;          // an older release's proxy
                if (Ours.Count > 0 || HasFolder || HasIni) return ModKind.Partial;
                return ModKind.None;
            }
        }
        public string Text
        {
            get
            {
                switch (Kind)
                {
                    case ModKind.Installed: return "Mod installed  \u00B7  " + string.Join(", ", Ours);
                    case ModKind.Outdated: return "Update available";
                    case ModKind.Partial: return "Partly installed";
                    default: return "Mod not installed";
                }
            }
        }
        public string Short
        {
            get
            {
                switch (Kind)
                {
                    case ModKind.Installed: return "DLSS mod ready";
                    case ModKind.Outdated: return "Update available";
                    case ModKind.Partial: return "Partly installed";
                    default: return "Not installed";
                }
            }
        }
    }

    public class Game
    {
        public string Name, Folder, Exe, Source;
        public List<string> Candidates = new List<string>();
        public ModState State = new ModState();
        public string AntiCheat;
        public Bitmap Icon;
        public string ExeDir { get { return Path.GetDirectoryName(Exe); } }
        public override string ToString() { return Name; }
    }

    public class GameCacheEntry
    {
        public string Name { get; set; }
        public string Folder { get; set; }
        public string Exe { get; set; }
        public string Source { get; set; }
        public List<string> Candidates { get; set; }
    }

    public class AppSettings
    {
        public List<string> ScanRoots { get; set; }
        public bool AutoDetect { get; set; }
        public List<string> ExtraGames { get; set; }
        public List<string> HiddenGames { get; set; }
        public Dictionary<string, string> ExeChoice { get; set; }
        public Dictionary<string, string> Args { get; set; }
        public List<string> AckAntiCheat { get; set; }
        public List<string> Installs { get; set; }
        public List<string> Shortcuts { get; set; }
        public string LastExe { get; set; }
        public List<GameCacheEntry> Cache { get; set; }

        public AppSettings()
        {
            ScanRoots = new List<string> { @"C:\Games" }; AutoDetect = true;
            ExtraGames = new List<string>(); HiddenGames = new List<string>();
            ExeChoice = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AckAntiCheat = new List<string>(); Installs = new List<string>(); Shortcuts = new List<string>();
            LastExe = ""; Cache = new List<GameCacheEntry>();
        }

        public void Normalize()
        {
            var d = new AppSettings();
            if (ScanRoots == null) ScanRoots = d.ScanRoots;
            if (ExtraGames == null) ExtraGames = d.ExtraGames;
            if (HiddenGames == null) HiddenGames = d.HiddenGames;
            ExeChoice = new Dictionary<string, string>(ExeChoice ?? d.ExeChoice, StringComparer.OrdinalIgnoreCase);
            Args = new Dictionary<string, string>(Args ?? d.Args, StringComparer.OrdinalIgnoreCase);
            if (AckAntiCheat == null) AckAntiCheat = d.AckAntiCheat;
            if (Installs == null) Installs = d.Installs;
            if (Shortcuts == null) Shortcuts = d.Shortcuts;
            if (LastExe == null) LastExe = "";
            if (Cache == null) Cache = d.Cache;
        }
    }

    public class NeedsAdminException : Exception
    {
        public NeedsAdminException(string dir) : base("Windows does not allow writing to:\n" + dir) { }
    }

    public class LibraryRoot { public string Path, Label; public bool Auto; }

    public static class Core
    {
        public const string AppName = "DLSSG Launcher";
        public static readonly string[] ProxyNames = { "version.dll", "winmm.dll", "dbghelp.dll", "dinput8.dll", "dxgi.dll", "d3d12.dll" };
        public const string MarkerName = "launcher_install.json";
        public const string BackupSuffix = ".dlssg-backup";

        public static string AppDir, ExePath, PackageDir, DataDir, SettingsPath;
        public static string PackageVersion = "";
        public static AppSettings Settings = new AppSettings();
        public static event Action<string> LogLine;

        static readonly Dictionary<string, string> hashCache = new Dictionary<string, string>();
        static Dictionary<string, string> packageProxyHashes = new Dictionary<string, string>();
        static readonly object settingsLock = new object();

        public static void Log(string s)
        {
            var h = LogLine;
            if (h != null) h(DateTime.Now.ToString("HH:mm:ss") + "   " + s);
        }

        static readonly StringComparison IC = StringComparison.OrdinalIgnoreCase;
        public static bool SamePath(string a, string b)
        {
            if (a == null || b == null) return false;
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), IC); } catch { return false; }
        }
        public static bool IsUnder(string path, string folder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder)) return false;
            string f = folder.TrimEnd('\\');
            return path.Equals(f, IC) || path.StartsWith(f + "\\", IC);
        }

        // ------------------------------------------------------------------ init / settings
        // %APPDATA%\DLSSG Launcher, or the folder in DLSSG_LAUNCHER_DATA (portable use, tests)
        public static string DefaultDataDir
        {
            get
            {
                string ov = Environment.GetEnvironmentVariable("DLSSG_LAUNCHER_DATA");
                return !string.IsNullOrEmpty(ov) ? ov : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            }
        }

        // Returns an error message when the mod files are missing (settings are loaded either way)
        public static string Init()
        {
            ExePath = Application.ExecutablePath;
            AppDir = Path.GetDirectoryName(ExePath);
            string sub = Path.Combine(AppDir, "mod");
            PackageDir = File.Exists(Path.Combine(sub, "version.dll")) ? sub : AppDir;
            DataDir = DefaultDataDir;
            try { Directory.CreateDirectory(DataDir); } catch { }
            SettingsPath = Path.Combine(DataDir, "settings.json");
            Settings = LoadSettings();

            string[] required = { "version.dll", "dlssg_sm86.ini", @"dlssg_sm86\backend\sm86_backend.dll", @"dlssg_sm86\runtime\nvngx_dlssg.dll" };
            var missing = required.Where(r => !File.Exists(Path.Combine(PackageDir, r))).ToList();
            if (missing.Count > 0)
                return "The DLSSG mod files were not found in:\n" + PackageDir + "\n\nMissing: " + string.Join(", ", missing) + "\n\nReinstall DLSSG Launcher.";
            try
            {
                var m = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(PackageDir, "manifest.json"))) as Dictionary<string, object>;
                var b = m != null && m.ContainsKey("bundle") ? m["bundle"] as Dictionary<string, object> : null;
                if (b != null && b.ContainsKey("version")) PackageVersion = Convert.ToString(b["version"]);
            }
            catch { }
            foreach (var n in ProxyNames)
            {
                string p = ProxySource(n);
                if (File.Exists(p)) packageProxyHashes[Sha256(p)] = n;
            }
            return null;
        }

        static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var s = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                    if (s != null) { s.Normalize(); return s; }
                }
            }
            catch { }
            var d = new AppSettings(); d.Normalize(); return d;
        }

        public static void SaveSettings()
        {
            lock (settingsLock)
            {
                try
                {
                    string tmp = SettingsPath + ".tmp";
                    File.WriteAllText(tmp, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(Settings), new UTF8Encoding(false));
                    if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
                    File.Move(tmp, SettingsPath);
                }
                catch { }
            }
        }

        public static string ProxySource(string name)
        {
            return name.Equals("version.dll", IC) ? Path.Combine(PackageDir, "version.dll") : Path.Combine(PackageDir, "alternatives", name);
        }

        public static string Sha256(string path)
        {
            var fi = new FileInfo(path);
            string key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            lock (hashCache) { string h; if (hashCache.TryGetValue(key, out h)) return h; }
            string r;
            using (var s = File.OpenRead(path)) using (var sha = SHA256.Create()) r = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "");
            lock (hashCache) hashCache[key] = r;
            return r;
        }

        public static bool IsOurProxy(string path) { try { return packageProxyHashes.ContainsKey(Sha256(path)); } catch { return false; } }

        // ------------------------------------------------------------------ discovery
        static readonly Regex ExcludeExe = new Regex(@"(crash|handler|report|unins|install|setup|redist|dxweb|dxsetup|bootstrap|downloader|broker|service|cleaner|attestation|logsgenerator|updater|patcher|anticheat|battleye|beservice|eac_|cefprocess|cefsubprocess|webhelper|quicksfv|prereq|dotnet|helper|sender|cleanup|activation|launcher|configurator|settings|uploader|touchup|register|elevate|7z|unitycrash|vrmonitor|overlay)", RegexOptions.IgnoreCase);
        static readonly Regex ExcludeDir = new Regex(@"^(_?redist|_?commonredist|redistributables?|__installer|_?installers?|directx|dotnet|vcredist|support|easyanticheat.*|battleye|prereqs?|_cleanup|crashreport.*|thirdparty|engine|logs?|saves?|savegames?|screenshots|movies|videos?|shadercache|cache|\$recycle\.bin|_retail_cache|mods?|dlssg_sm86)$", RegexOptions.IgnoreCase);
        static readonly Regex GenericDir = new Regex(@"^(win64|win32|x64|x86|bin|bin64|binaries|game|retail|shipping|release|app|build|content)$", RegexOptions.IgnoreCase);
        static readonly Regex NotAGame = new Regex(@"^(steamworks shared|steamvr|steam controller configs|proton.*|steam linux runtime.*|launcher|directxredist|_commonredist|epic online services|common|downloads?|temp|\$.*|steamapps|steam)$", RegexOptions.IgnoreCase);

        public static List<FileInfo> FindFiles(string root, string pattern, int maxDepth)
        {
            var output = new List<FileInfo>();
            var stack = new Stack<KeyValuePair<string, int>>();
            stack.Push(new KeyValuePair<string, int>(root, 0));
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                try { foreach (var f in Directory.EnumerateFiles(item.Key, pattern)) output.Add(new FileInfo(f)); } catch { }
                if (item.Value < maxDepth)
                {
                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(item.Key))
                        {
                            try { if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                            if (!ExcludeDir.IsMatch(Path.GetFileName(sub))) stack.Push(new KeyValuePair<string, int>(sub, item.Value + 1));
                        }
                    }
                    catch { }
                }
            }
            return output;
        }

        static double ExeScore(FileInfo f)
        {
            double s = Math.Min(f.Length / 1048576.0, 900);
            if (Regex.IsMatch(f.Name, @"-(Win64|WinGDK)-Shipping\.exe$", RegexOptions.IgnoreCase)) s += 1000;
            if (Regex.IsMatch(f.FullName, @"\\(Binaries\\Win64|bin\\x64|bin64|x64)\\", RegexOptions.IgnoreCase)) s += 100;
            return s;
        }

        public static List<string> RankExes(string folder)
        {
            var all = FindFiles(folder, "*.exe", 5);
            var good = all.Where(f => !ExcludeExe.IsMatch(f.Name)).ToList();
            if (good.Count == 0) good = all;
            return good.OrderByDescending(ExeScore).Select(f => f.FullName).ToList();
        }

        public static string DisplayName(string path)
        {
            string dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            string d = dir;
            while (!string.IsNullOrEmpty(d) && GenericDir.IsMatch(Path.GetFileName(d))) d = Path.GetDirectoryName(d);
            if (string.IsNullOrEmpty(d)) d = dir;
            string leaf = Path.GetFileName(d.TrimEnd('\\'));
            return string.IsNullOrEmpty(leaf) ? d : leaf;
        }

        static List<string> SteamLibraries()
        {
            var r = new List<string>();
            try
            {
                string steam = null;
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) if (k != null) steam = k.GetValue("SteamPath") as string;
                if (steam == null) using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")) if (k != null) steam = k.GetValue("InstallPath") as string;
                if (steam == null) return r;
                steam = steam.Replace('/', '\\');
                r.Add(steam);
                string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
                        r.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch { }
            return r;
        }

        public static List<LibraryRoot> GetLibraryRoots()
        {
            var list = new List<LibraryRoot>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string, bool, string> add = (p, auto, label) =>
            {
                if (string.IsNullOrEmpty(p)) return;
                try { p = Path.GetFullPath(p).TrimEnd('\\'); } catch { return; }
                if (Directory.Exists(p) && seen.Add(p)) list.Add(new LibraryRoot { Path = p, Auto = auto, Label = label });
            };
            foreach (var r in Settings.ScanRoots) add(r, false, "Library");
            if (Settings.AutoDetect)
            {
                foreach (var s in SteamLibraries()) add(Path.Combine(s, @"steamapps\common"), true, "Steam");
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                        string x = d.RootDirectory.FullName;
                        add(x + "Games", true, "Games");
                        add(x + "XboxGames", true, "Xbox");
                        add(x + @"SteamLibrary\steamapps\common", true, "Steam");
                        add(x + "Epic Games", true, "Epic");
                        add(x + "GOG Games", true, "GOG");
                    }
                    catch { }
                }
                add(Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Epic Games"), true, "Epic");
                add(Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\GOG Galaxy\Games"), true, "GOG");
            }
            return list;
        }

        // Single games registered by Epic / GOG (install folders outside the library roots)
        static List<KeyValuePair<string, string>> StoreGames()
        {
            var r = new List<KeyValuePair<string, string>>();
            try
            {
                string man = Environment.ExpandEnvironmentVariables(@"%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests");
                if (Directory.Exists(man))
                    foreach (var f in Directory.GetFiles(man, "*.item"))
                    {
                        try
                        {
                            var j = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f)) as Dictionary<string, object>;
                            if (j == null || !j.ContainsKey("InstallLocation")) continue;
                            string loc = Convert.ToString(j["InstallLocation"]);
                            string name = j.ContainsKey("DisplayName") ? Convert.ToString(j["DisplayName"]) : DisplayName(loc);
                            r.Add(new KeyValuePair<string, string>(loc, name));
                        }
                        catch { }
                    }
            }
            catch { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games"))
                    if (k != null)
                        foreach (var id in k.GetSubKeyNames())
                            using (var g = k.OpenSubKey(id))
                            {
                                if (g == null) continue;
                                string path = g.GetValue("path") as string, name = g.GetValue("gameName") as string;
                                if (!string.IsNullOrEmpty(path)) r.Add(new KeyValuePair<string, string>(path, string.IsNullOrEmpty(name) ? DisplayName(path) : name));
                            }
            }
            catch { }
            return r;
        }

        public static Game NewGame(string folder, string name, string source)
        {
            folder = folder.TrimEnd('\\');
            var cands = RankExes(folder);
            string chosen;
            lock (settingsLock) Settings.ExeChoice.TryGetValue(folder, out chosen);
            if (!string.IsNullOrEmpty(chosen) && File.Exists(chosen))
            {
                if (!cands.Contains(chosen, StringComparer.OrdinalIgnoreCase)) cands.Insert(0, chosen);
            }
            else
            {
                if (cands.Count == 0) return null;
                chosen = cands[0];
            }
            return new Game { Name = name, Folder = folder, Exe = chosen, Candidates = cands, Source = source };
        }

        public static void Refresh(Game g)
        {
            try { g.State = GetModState(g.ExeDir); } catch { g.State = new ModState(); }
            g.AntiCheat = AntiCheatReason(g.Exe, g.Folder);
            if (g.Icon == null) g.Icon = ExtractIcon(g.Exe, Theme.S(64));
        }

        public static List<Game> GamesFromCache()
        {
            var r = new List<Game>();
            foreach (var c in Settings.Cache.ToList())
            {
                if (c == null || string.IsNullOrEmpty(c.Exe) || !File.Exists(c.Exe)) continue;
                var g = new Game { Name = c.Name, Folder = c.Folder, Exe = c.Exe, Source = c.Source, Candidates = c.Candidates ?? new List<string> { c.Exe } };
                string chosen;
                if (Settings.ExeChoice.TryGetValue(g.Folder, out chosen) && File.Exists(chosen)) g.Exe = chosen;
                if (!g.Candidates.Contains(g.Exe, StringComparer.OrdinalIgnoreCase)) g.Candidates.Insert(0, g.Exe);
                Refresh(g);
                r.Add(g);
            }
            return r;
        }

        public static List<Game> ScanGames(Action<string> progress)
        {
            var games = new List<Game>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Func<string, bool> covered = f => seen.Contains(f.TrimEnd('\\')) || games.Any(x => IsUnder(f, x.Folder));
            foreach (var root in GetLibraryRoots())
            {
                string[] subs;
                try { subs = Directory.GetDirectories(root.Path); } catch { continue; }
                foreach (var sub in subs)
                {
                    string leaf = Path.GetFileName(sub);
                    if (NotAGame.IsMatch(leaf) || covered(sub)) continue;
                    if (progress != null) progress(leaf);
                    var g = NewGame(sub, leaf, root.Label);
                    if (g != null) { seen.Add(g.Folder); games.Add(g); }
                }
            }
            if (Settings.AutoDetect)
                foreach (var sg in StoreGames())
                {
                    if (!Directory.Exists(sg.Key) || covered(sg.Key)) continue;
                    if (progress != null) progress(sg.Value);
                    var g = NewGame(sg.Key, sg.Value, "Store");
                    if (g != null) { seen.Add(g.Folder); games.Add(g); }
                }
            foreach (var f in Settings.ExtraGames.ToList())
            {
                if (!Directory.Exists(f) || covered(f)) continue;
                string exe;
                Settings.ExeChoice.TryGetValue(f, out exe);
                var g = NewGame(f, DisplayName(string.IsNullOrEmpty(exe) ? f : exe), "Added");
                if (g != null) { seen.Add(g.Folder); games.Add(g); }
            }
            var hidden = new HashSet<string>(Settings.HiddenGames, StringComparer.OrdinalIgnoreCase);
            games = games.Where(x => !hidden.Contains(x.Folder)).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var g in games) Refresh(g);
            lock (settingsLock)
                Settings.Cache = games.Select(x => new GameCacheEntry { Name = x.Name, Folder = x.Folder, Exe = x.Exe, Source = x.Source, Candidates = x.Candidates }).ToList();
            return games;
        }

        // Game record for an EXE given on the command line (desktop shortcuts)
        public static Game GameForExe(string exe)
        {
            foreach (var c in Settings.Cache)
                if (c != null && !string.IsNullOrEmpty(c.Folder) && IsUnder(exe, c.Folder))
                {
                    var g = new Game { Name = c.Name, Folder = c.Folder, Exe = exe, Source = c.Source, Candidates = new List<string> { exe } };
                    Refresh(g); return g;
                }
            string folder = Path.GetDirectoryName(exe);
            foreach (var root in GetLibraryRoots())
                if (IsUnder(exe, root.Path))
                {
                    string rel = exe.Substring(root.Path.Length + 1);
                    if (rel.Contains('\\')) folder = Path.Combine(root.Path, rel.Split('\\')[0]);
                    break;
                }
            var game = new Game { Name = DisplayName(folder == Path.GetDirectoryName(exe) ? exe : folder), Folder = folder, Exe = exe, Candidates = new List<string> { exe } };
            Refresh(game);
            return game;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint n, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public static Bitmap ExtractIcon(string exe, int size)
        {
            try
            {
                var h = new IntPtr[1]; var ids = new int[1];
                if (PrivateExtractIcons(exe, 0, size, size, h, ids, 1, 0) > 0 && h[0] != IntPtr.Zero)
                {
                    try { using (var ic = Icon.FromHandle(h[0])) return ic.ToBitmap(); }
                    finally { DestroyIcon(h[0]); }
                }
            }
            catch { }
            try { using (var ic = Icon.ExtractAssociatedIcon(exe)) return ic.ToBitmap(); } catch { return null; }
        }

        // ------------------------------------------------------------------ mod state
        // Same size and date (within the 2 s of zip / FAT timestamps) = same file. Same size but another date - a copy
        // made by a tool that does not keep dates - is decided by content.
        static bool SameFile(FileInfo a, FileInfo b)
        {
            if (!a.Exists || !b.Exists || a.Length != b.Length) return false;
            if (Math.Abs((a.LastWriteTimeUtc - b.LastWriteTimeUtc).TotalSeconds) <= 2.0) return true;
            try { return Sha256(a.FullName) == Sha256(b.FullName); } catch { return false; }
        }

        public static ModState GetModState(string dir)
        {
            var st = new ModState();
            foreach (var n in ProxyNames)
            {
                string p = Path.Combine(dir, n);
                if (!File.Exists(p)) continue;
                if (IsOurProxy(p)) st.Ours.Add(n);
                else if (!IsMicrosoftFile(p)) st.Foreign.Add(n);
            }
            st.HasFolder = File.Exists(Path.Combine(dir, @"dlssg_sm86\backend\sm86_backend.dll"));
            st.HasIni = File.Exists(Path.Combine(dir, "dlssg_sm86.ini"));
            if (st.HasFolder)
                foreach (var rel in new[] { @"dlssg_sm86\backend\sm86_backend.dll", @"dlssg_sm86\runtime\nvngx_dlssg.dll", @"dlssg_sm86\kernels\fg_kernels.dll" })
                    if (!SameFile(new FileInfo(Path.Combine(PackageDir, rel)), new FileInfo(Path.Combine(dir, rel)))) { st.Outdated = true; break; }
            return st;
        }

        static bool IsMicrosoftFile(string p)
        {
            try { var v = FileVersionInfo.GetVersionInfo(p); return v.CompanyName != null && v.CompanyName.IndexOf("Microsoft", IC) >= 0; } catch { return false; }
        }

        static readonly string[] AntiCheatMarkers = { "EasyAntiCheat", "EasyAntiCheat_EOS", "BattlEye", "BEService.exe", "BEService_x64.exe",
            "start_protected_game.exe", "EasyAntiCheat_EOS_Setup.exe", "EAAntiCheat.GameServiceLauncher.exe", "randgrid.sys",
            "CODSecureAttestationWizard.exe", "vgc.exe", "ACE", "xigncode", "nProtect", "GameGuard", "mhyprot2.sys" };
        static readonly Regex AntiCheatName = new Regex(@"(call of duty|warzone|valorant|fortnite|apex legends|pubg|rainbow six|siege|battlefield|destiny 2|the finals|marvel rivals|counter-strike|overwatch|escape from tarkov|dayz|naraka|delta force|xdefiant|black ops|modern warfare|league of legends|genshin|honkai|zenless)", RegexOptions.IgnoreCase);

        public static string AntiCheatReason(string exe, string gameFolder)
        {
            var m = AntiCheatName.Match((gameFolder ?? "") + "\\" + exe);
            if (m.Success) return "This looks like " + CultureTitle(m.Groups[1].Value) + ", which uses online anti-cheat.";
            string d = Path.GetDirectoryName(exe);
            while (!string.IsNullOrEmpty(d))
            {
                foreach (var mk in AntiCheatMarkers)
                {
                    string p = Path.Combine(d, mk);
                    if (File.Exists(p) || Directory.Exists(p)) return "Found " + mk + " in the game folder.";
                }
                if (string.IsNullOrEmpty(gameFolder) || !IsUnder(d, gameFolder) || SamePath(d, gameFolder)) break;
                d = Path.GetDirectoryName(d);
            }
            return null;
        }
        static string CultureTitle(string s) { return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s); }

        public static List<string> FindModInstalls(string folder)
        {
            return FindFiles(folder, "dlssg_sm86.ini", 5)
                .Where(f => Directory.Exists(Path.Combine(f.DirectoryName, "dlssg_sm86")) && !SamePath(f.DirectoryName, PackageDir))
                .Select(f => f.DirectoryName).ToList();
        }

        // ------------------------------------------------------------------ system helpers
        public static bool IsWritable(string dir)
        {
            try
            {
                string t = Path.Combine(dir, ".dlssg_write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(t, ""); File.Delete(t); return true;
            }
            catch { return false; }
        }

        public static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; }
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        public static bool IsRunning(string exe)
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
            {
                try
                {
                    IntPtr h = OpenProcess(0x1000, false, p.Id);
                    if (h == IntPtr.Zero) return true;            // same name, cannot look inside: assume it is the game
                    try
                    {
                        var sb = new StringBuilder(1024); int n = sb.Capacity;
                        if (QueryFullProcessImageName(h, 0, sb, ref n) && string.Equals(sb.ToString(), exe, IC)) return true;
                    }
                    finally { CloseHandle(h); }
                }
                catch { }
                finally { p.Dispose(); }
            }
            return false;
        }

        public static void StartGame(Game g)
        {
            string a;
            Settings.Args.TryGetValue(g.Exe, out a);
            var psi = new ProcessStartInfo(g.Exe) { WorkingDirectory = g.ExeDir, UseShellExecute = true };
            if (!string.IsNullOrEmpty(a)) psi.Arguments = a;
            Process.Start(psi);
            Log("Started " + Path.GetFileName(g.Exe) + (string.IsNullOrEmpty(a) ? "" : " " + a));
        }

        public static void OpenInExplorer(string file)
        {
            try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { }
        }

        public static void OpenText(string file)
        {
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + file + "\"") { UseShellExecute = true });
        }

        // ------------------------------------------------------------------ install / update / remove
        public static bool Install(Game g, string proxy, bool overwriteIni, Func<string, string, bool> confirm, Action<string, int> progress)
        {
            string dir = g.ExeDir;
            if (SamePath(dir, PackageDir) || SamePath(dir, AppDir)) throw new InvalidOperationException("That EXE is inside the launcher's own folder. Pick the real game.");
            if (!File.Exists(g.Exe)) throw new FileNotFoundException("Game EXE not found:\n" + g.Exe);
            if (!IsWritable(dir)) throw new NeedsAdminException(dir);

            var st = GetModState(dir);
            bool upToDate = st.Ours.Count == 1 && st.Ours[0].Equals(proxy, IC) && !st.Outdated && st.HasIni;
            if (!upToDate && IsRunning(g.Exe)) throw new InvalidOperationException("The game is running. Close it completely, then try again.");

            Log("Installing into " + dir);
            var backups = new List<string>();
            string pd = Path.Combine(dir, proxy), src = ProxySource(proxy);

            if (File.Exists(pd) && !IsOurProxy(pd))
            {
                if (confirm == null || !confirm("Replace " + proxy + "?",
                    "The game folder already has a different " + proxy + " (another mod, ReShade, or an older DLSSG release):\n" + pd +
                    "\n\nRename it to " + proxy + BackupSuffix + " and use the mod's " + proxy + " instead? \"Remove mod\" puts it back."))
                { Log("  cancelled."); return false; }
                string bak = pd + BackupSuffix;
                if (File.Exists(bak)) bak = pd + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + BackupSuffix;
                File.Move(pd, bak);
                backups.Add(Path.GetFileName(bak));
                Log("  backed up existing " + proxy + " -> " + Path.GetFileName(bak));
            }

            foreach (var n in ProxyNames)
            {
                if (n.Equals(proxy, IC)) continue;
                string p = Path.Combine(dir, n);
                if (!File.Exists(p)) continue;
                if (IsOurProxy(p)) { File.Delete(p); Log("  removed " + n + " (only one proxy may be active)"); }
                else if (!IsMicrosoftFile(p)) Log("  note: " + n + " is not from this mod. If it is from an older DLSSG release, delete it - it may load first.");
            }

            if (File.Exists(pd) && Sha256(pd) == Sha256(src)) Log("  " + proxy + " up to date");
            else { File.Copy(src, pd, true); Log("  copied " + proxy); }

            int copied = SyncFolder(Path.Combine(PackageDir, "dlssg_sm86"), Path.Combine(dir, "dlssg_sm86"), progress);
            Log(copied > 0 ? "  copied " + copied + " file(s) into dlssg_sm86\\" : "  dlssg_sm86\\ up to date");

            string ini = Path.Combine(dir, "dlssg_sm86.ini");
            if (!File.Exists(ini) || overwriteIni) { File.Copy(Path.Combine(PackageDir, "dlssg_sm86.ini"), ini, true); Log("  copied dlssg_sm86.ini"); }
            else Log("  kept your dlssg_sm86.ini");

            string marker = Path.Combine(dir, "dlssg_sm86", MarkerName);
            try
            {
                if (File.Exists(marker))
                {
                    var old = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(marker)) as Dictionary<string, object>;
                    var ob = old != null && old.ContainsKey("backups") ? old["backups"] as object[] : null;
                    if (ob != null) backups.InsertRange(0, ob.Select(o => Convert.ToString(o)));
                }
            }
            catch { }
            var mk = new Dictionary<string, object> {
                { "proxy", proxy }, { "package_version", PackageVersion }, { "launcher", ExePath },
                { "installed_at", DateTime.Now.ToString("s") }, { "backups", backups.Distinct().ToArray() } };
            File.WriteAllText(marker, new JavaScriptSerializer().Serialize(mk), new UTF8Encoding(false));

            lock (settingsLock) if (!Settings.Installs.Any(x => SamePath(x, dir))) Settings.Installs.Add(dir);
            SaveSettings();
            Log("Mod ready (" + proxy + ").");
            return true;
        }

        static int SyncFolder(string src, string dst, Action<string, int> progress)
        {
            string root = new DirectoryInfo(src).FullName.TrimEnd('\\');
            var todo = new List<FileInfo>();
            long total = 0;
            foreach (var f in new DirectoryInfo(src).GetFiles("*", SearchOption.AllDirectories))
            {
                string rel = f.FullName.Substring(root.Length + 1);
                if (rel.StartsWith(@"logs\", IC) || f.Name.Equals(MarkerName, IC)) continue;
                var t = new FileInfo(Path.Combine(dst, rel));
                if (!SameFile(f, t)) { todo.Add(f); total += f.Length; }
                else if (t.LastWriteTimeUtc != f.LastWriteTimeUtc) { try { File.SetLastWriteTimeUtc(t.FullName, f.LastWriteTimeUtc); } catch { } }
            }
            long done = 0; int lastPct = -1;
            var buf = new byte[1 << 20];
            foreach (var f in todo)
            {
                string rel = f.FullName.Substring(root.Length + 1);
                string t = Path.Combine(dst, rel), tmp = t + ".dlssg-tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(t));
                using (var i = f.OpenRead())
                using (var o = File.Create(tmp))
                {
                    int r;
                    while ((r = i.Read(buf, 0, buf.Length)) > 0)
                    {
                        o.Write(buf, 0, r); done += r;
                        int pct = (int)(done * 100 / Math.Max(1, total));
                        if (progress != null && pct != lastPct) { lastPct = pct; progress("Copying " + f.Name, pct); }
                    }
                }
                File.SetLastWriteTimeUtc(tmp, f.LastWriteTimeUtc);
                if (File.Exists(t)) File.Delete(t);
                File.Move(tmp, t);
            }
            Directory.CreateDirectory(Path.Combine(dst, "logs"));
            return todo.Count;
        }

        public static List<string> RemovalList(string dir)
        {
            var st = GetModState(dir);
            var l = new List<string>(st.Ours);
            if (File.Exists(Path.Combine(dir, "dlssg_sm86.ini"))) l.Add("dlssg_sm86.ini");
            if (Directory.Exists(Path.Combine(dir, "dlssg_sm86"))) l.Add("dlssg_sm86\\ (with its logs)");
            return l;
        }

        public static void Uninstall(string dir)
        {
            if (SamePath(dir, PackageDir) || SamePath(dir, AppDir)) throw new InvalidOperationException("That is the launcher's own folder.");
            if (!IsWritable(dir)) throw new NeedsAdminException(dir);
            var st = GetModState(dir);
            Log("Removing the mod from " + dir);
            foreach (var n in st.Ours) { File.Delete(Path.Combine(dir, n)); Log("  removed " + n); }
            foreach (var b in Directory.GetFiles(dir, "*" + BackupSuffix))
            {
                string name = Path.GetFileName(b);
                string orig = Regex.Replace(name.Substring(0, name.Length - BackupSuffix.Length), @"\.\d{14}$", "");
                string op = Path.Combine(dir, orig);
                if (!File.Exists(op)) { File.Move(b, op); Log("  restored " + orig); }
            }
            string mod = Path.Combine(dir, "dlssg_sm86");
            if (Directory.Exists(mod))
            {
                bool ours = Directory.Exists(Path.Combine(mod, "backend")) || Directory.Exists(Path.Combine(mod, "runtime")) || File.Exists(Path.Combine(mod, MarkerName));
                if (ours) { Directory.Delete(mod, true); Log("  removed dlssg_sm86\\"); }
                else Log("  left dlssg_sm86\\ alone (does not look like this mod)");
            }
            string ini = Path.Combine(dir, "dlssg_sm86.ini");
            if (File.Exists(ini)) { File.Delete(ini); Log("  removed dlssg_sm86.ini"); }
            lock (settingsLock) Settings.Installs.RemoveAll(x => SamePath(x, dir));
            SaveSettings();
            Log("Mod removed.");
        }

        // ------------------------------------------------------------------ shortcuts
        public static string DesktopDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }

        public static void CreateShortcut(string lnk, string target, string args, string workDir, string icon, string desc)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic sh = Activator.CreateInstance(t);
            try
            {
                dynamic sc = sh.CreateShortcut(lnk);
                sc.TargetPath = target; sc.Arguments = args ?? ""; sc.WorkingDirectory = workDir ?? "";
                sc.IconLocation = icon ?? target; sc.Description = desc ?? "";
                sc.Save();
                Marshal.FinalReleaseComObject(sc);
            }
            finally { Marshal.FinalReleaseComObject(sh); }
        }

        public static string CreateGameShortcut(Game g, string name)
        {
            string safe = Regex.Replace(name, "[\\\\/:*?\"<>|]", "").Trim();
            if (safe.Length == 0) throw new ArgumentException("Empty shortcut name.");
            string lnk = Path.Combine(DesktopDir, safe + ".lnk");
            CreateShortcut(lnk, ExePath, "--play \"" + g.Exe + "\"", AppDir, g.Exe + ",0", "Start " + safe + " with the DLSSG frame generation mod");
            lock (settingsLock) if (!Settings.Shortcuts.Contains(lnk, StringComparer.OrdinalIgnoreCase)) Settings.Shortcuts.Add(lnk);
            SaveSettings();
            Log("Created desktop shortcut " + safe);
            return lnk;
        }

        public static string DefaultShortcutName(Game g)
        {
            string n = g.Name;
            if (g.Candidates.Count > 1 && !g.Exe.Equals(g.Candidates[0], IC)) n += " - " + Path.GetFileNameWithoutExtension(g.Exe);
            return n + " (DLSS mod)";
        }
    }

    // ---------------------------------------------------------------------- dlssg_sm86.ini (edited in place)
    public class IniFile
    {
        public string Path;
        bool bom; string nl;
        public List<string> Lines = new List<string>();

        public static IniFile Load(string path)
        {
            var f = new IniFile { Path = path };
            byte[] b = File.ReadAllBytes(path);
            f.bom = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
            int skip = f.bom ? 3 : 0;
            string text = new UTF8Encoding(false).GetString(b, skip, b.Length - skip);
            f.nl = text.Contains("\r\n") ? "\r\n" : "\n";
            f.Lines = Regex.Split(text, "\r?\n").ToList();
            return f;
        }

        public void Save() { File.WriteAllText(Path, string.Join(nl, Lines), new UTF8Encoding(bom)); }

        void Find(string section, string key, out int line, out int header)
        {
            string cur = ""; line = -1; header = -1;
            var keyRx = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=", RegexOptions.IgnoreCase);
            for (int i = 0; i < Lines.Count; i++)
            {
                string l = Lines[i];
                var m = Regex.Match(l, @"^\s*\[([^\]]+)\]");
                if (m.Success) { cur = m.Groups[1].Value.Trim(); if (cur.Equals(section, StringComparison.OrdinalIgnoreCase)) header = i; continue; }
                if (cur.Equals(section, StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(l, @"^\s*[;#]") && keyRx.IsMatch(l)) { line = i; return; }
            }
        }

        public string Get(string section, string key)
        {
            int line, header; Find(section, key, out line, out header);
            if (line < 0) return null;
            int eq = Lines[line].IndexOf('=');
            return Lines[line].Substring(eq + 1).Trim();
        }

        public void Set(string section, string key, string value)
        {
            int line, header; Find(section, key, out line, out header);
            if (line >= 0) { string name = Lines[line].Substring(0, Lines[line].IndexOf('=')).Trim(); Lines[line] = name + "=" + value; }
            else if (header >= 0) Lines.Insert(header + 1, key + "=" + value);
            else
            {
                int at = Lines.Count;
                if (at > 0 && Lines[at - 1] == "") at--;
                Lines.Insert(at, ""); Lines.Insert(at + 1, "[" + section + "]"); Lines.Insert(at + 2, key + "=" + value);
            }
        }
    }

    public class IniField
    {
        public string Section, Key, Label, Default;
        public Choice[] Choices;
        public IniField(string s, string k, string label, string def, params Choice[] c) { Section = s; Key = k; Label = label; Default = def; Choices = c; }
        public string Id { get { return Section + "." + Key; } }

        public static readonly IniField Enabled = new IniField("General", "Enabled", "Frame generation", "1");
        public static readonly IniField Engine = new IniField("FrameGeneration", "Engine", "Engine", "DLSS");
        public static readonly IniField Frames = new IniField("FrameGeneration", "MaxGeneratedFrames", "Multiplier", "3");
        public static readonly IniField Tier = new IniField("DLSSG", "Optimized", "Speed / quality", "1",
            new Choice("0", "0  -  Stock NVIDIA kernels (slowest)"),
            new Choice("1", "1  -  Accelerated, identical image to NVIDIA (default)"),
            new Choice("2", "2  -  Faster, lossy image kernels (difference not visible)"),
            new Choice("3", "3  -  Fastest, all lossy (lowest image quality)"));
        public static readonly IniField Preset = new IniField("DLSSG", "Preset", "Preset", "A",
            new Choice("A", "A  -  Default"),
            new Choice("B", "B  -  UI recomposition (only if the game supplies UI planes)"),
            new Choice("C", "C  -  FG Lite: 32-54% less GPU time, slightly lower quality"));
        public static readonly IniField Flow = new IniField("XeSSFG", "FlowResolution", "Optical flow", "Middle",
            new Choice("Coarse", "Coarse  -  fastest"),
            new Choice("Middle", "Middle  -  balanced (default)"),
            new Choice("Fine", "Fine  -  slowest, most detailed"),
            new Choice("CoarseAt4K", "CoarseAt4K  -  Coarse at 4K output, Middle below"));
        public static readonly IniField Hudless = new IniField("XeSSFG", "UseHudless", "UI compositing", "Auto",
            new Choice("Auto", "Auto  -  same as 0 (default)"),
            new Choice("0", "0  -  Interpolate the whole image, UI included"),
            new Choice("1", "1  -  Always use the game's HUD-less image (errors if missing)"));
        public static readonly IniField SuperRes = new IniField("SuperResolution", "Mode", "Super Resolution", "Off",
            new Choice("Off", "Off  -  the game's own DLSS Super Resolution (default)"),
            new Choice("M-INT8", "M-INT8  -  preset M on this mod's INT8 kernels"),
            new Choice("L-INT8", "L-INT8  -  preset L on this mod's INT8 kernels"),
            new Choice("M-FP16", "M-FP16  -  preset M on NVIDIA's kernels"),
            new Choice("L-FP16", "L-FP16  -  preset L on NVIDIA's kernels"));
        public static readonly IniField LogLevel = new IniField("Logging", "Level", "Log level", "1",
            new Choice("0", "0  -  Off"),
            new Choice("1", "1  -  Errors only (default)"),
            new Choice("2", "2  -  Configuration (use this to check that the mod loads)"),
            new Choice("3", "3  -  Full trace (slow - debugging only)"));

        public static readonly IniField[] All = { Enabled, Engine, Frames, Tier, Preset, Flow, Hudless, SuperRes, LogLevel };
    }
}
