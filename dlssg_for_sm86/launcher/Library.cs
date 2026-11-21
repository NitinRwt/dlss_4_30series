using System.Text.Json;
using System.Text.RegularExpressions;

namespace DlssgLauncher;

/// <summary>Games the user added by hand or from a shortcut survive restarts (%LOCALAPPDATA%\DLSSG-Launcher\games.json).</summary>
static class Library
{
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DLSSG-Launcher", "games.json");
    static readonly JsonSerializerOptions Opt = new() { IncludeFields = true, WriteIndented = true };

    public static List<Game> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return (JsonSerializer.Deserialize<List<Game>>(File.ReadAllText(FilePath), Opt) ?? new())
                .Where(g => Directory.Exists(g.Dir)).ToList();
        }
        catch { return new(); }
    }

    public static void Save(IEnumerable<Game> games)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(games.Where(g => g.Store is "Manual" or "Shortcut").ToList(), Opt));
        }
        catch { }
    }
}

/// <summary>Turns a .lnk / .url the user already has into a game entry.</summary>
static class ShortcutImporter
{
    public static bool IsShortcut(string path) =>
        path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    static Game? SteamById(string id, IEnumerable<Game> known) =>
        known.FirstOrDefault(g => g.Store == "Steam" && g.SteamAppId == id);

    public static (Game? game, string? error) Resolve(string path, IEnumerable<Game> known)
    {
        try
        {
            string target = "", args = "", url = "";
            if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in File.ReadAllLines(path))
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) url = line[4..].Trim();
                if (url.Length == 0) return (null, "That .url file has no address in it.");
            }
            else
            {
                var t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell not available");
                dynamic sh = Activator.CreateInstance(t)!;
                dynamic s = sh.CreateShortcut(path);
                target = (string)(s.TargetPath ?? ""); args = (string)(s.Arguments ?? "");
            }

            // Steam shortcuts: steam://rungameid/<id>, or steam.exe -applaunch <id>
            var m = Regex.Match(url + " " + args, @"(?:rungameid/|-applaunch\s+)(\d+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var g = SteamById(m.Groups[1].Value, known);
                return g != null ? (g, null)
                    : (null, "That Steam game (id " + m.Groups[1].Value + ") is not installed in any Steam library the launcher can see.");
            }
            if (url.Length > 0) return (null, "Only Steam web shortcuts are supported. For other .url files, use \"Add game (.exe)\".");

            if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
                return (null, "The shortcut does not point to a game program (.exe) that exists:\n" + (target.Length > 0 ? target : "(empty target)"));

            var name = Path.GetFileNameWithoutExtension(path);
            return (new Game { Name = name, Dir = Path.GetDirectoryName(target)!, Store = "Shortcut", PreferredExe = target, Lnk = path }, null);
        }
        catch (Exception ex) { return (null, "Could not read the shortcut:\n" + ex.Message); }
    }
}
