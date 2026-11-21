// DLSSG_Launcher.exe - starts DLSSG_Launcher.ps1 (the launcher window) without a console window.
//
// Uses DLSSG_Launcher.ps1 beside the EXE when it is there (so edits to the script apply), otherwise the
// copy embedded in the EXE (extracted to %TEMP%\DLSSG_Launcher\). Either way the mod package folder is the
// EXE's folder, passed to the script in DLSSG_PACKAGE_DIR.
//
// Build: run build_launcher.ps1 in this folder (uses the C# compiler that ships with Windows / .NET 4.x).

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("DLSSG SM86 Launcher")]
[assembly: AssemblyProduct("DLSSG SM86 Launcher")]
[assembly: AssemblyDescription("Finds games, installs the DLSSG SM86 mod next to the game EXE and launches it")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Program
{
    const string Title = "DLSSG Launcher";
    const string ScriptName = "DLSSG_Launcher.ps1";

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            string exeDir = Path.GetDirectoryName(exePath);

            string script = Path.Combine(exeDir, ScriptName);
            if (!File.Exists(script))
                script = ExtractEmbeddedScript();

            string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                                     @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(ps)) ps = "powershell.exe";

            string extra = "";
            foreach (string a in args)
            {
                string v = a.Replace("\"", "");
                extra += (v.Length == 0 || v.IndexOf(' ') >= 0 || v.IndexOf('\t') >= 0) ? " \"" + v + "\"" : " " + v;
            }

            var psi = new ProcessStartInfo(ps,
                "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File \"" + script + "\"" + extra);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = exeDir;
            psi.EnvironmentVariables["DLSSG_PACKAGE_DIR"] = exeDir;
            psi.EnvironmentVariables["DLSSG_LAUNCHER_EXE"] = exePath;
            Process.Start(psi);
            return 0;
        }
        catch (Exception e)
        {
            MessageBox.Show("Could not start the launcher:\r\n\r\n" + e.Message, Title,
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static string ExtractEmbeddedScript()
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        using (Stream s = asm.GetManifestResourceStream(ScriptName))
        {
            if (s == null) throw new FileNotFoundException(ScriptName + " is missing next to the EXE and not embedded.");
            string dir = Path.Combine(Path.GetTempPath(), "DLSSG_Launcher");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, ScriptName);
            using (var f = File.Create(path)) s.CopyTo(f);
            return path;
        }
    }
}
