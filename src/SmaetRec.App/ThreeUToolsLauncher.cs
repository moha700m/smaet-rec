using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SmaetRec.App;

/// <summary>Starts or foregrounds the user's installed 3uTools without changing its drivers.</summary>
internal static class ThreeUToolsLauncher
{
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    public static bool OpenOrActivate(out string message)
    {
        foreach (var process in FindRunningProcesses())
        {
            using (process)
            {
                try
                {
                    IntPtr handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        ShowWindowAsync(handle, SwRestore);
                        SetForegroundWindow(handle);
                    }
                    message = "3uTools is already running. Open Toolbox → Realtime Screen.";
                    return true;
                }
                catch (Exception error) when (error is InvalidOperationException or
                    System.ComponentModel.Win32Exception or System.Security.SecurityException)
                {
                }
            }
        }

        foreach (string candidate in CandidateExecutables())
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                Process.Start(new ProcessStartInfo(candidate)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(candidate) ?? AppContext.BaseDirectory
                });
                message = "3uTools was started. Open Toolbox → Realtime Screen.";
                return true;
            }
            catch (Exception error) when (error is InvalidOperationException or
                System.ComponentModel.Win32Exception or System.Security.SecurityException)
            {
                Log.Write("Could not start 3uTools from " + candidate + ": " + error.Message);
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo("3uTools.exe") { UseShellExecute = true });
            message = "A launch request was sent to Windows. Open Toolbox → Realtime Screen.";
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or
            System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            message = "Could not find or start 3uTools. Start 3uTools manually, then open Toolbox → Realtime Screen.";
            Log.Write("3uTools launch failed: " + error.Message);
            return false;
        }
    }

    private static IEnumerable<Process> FindRunningProcesses()
    {
        foreach (string name in new[] { "3uTools", "3uToolsService", "3uPlayer" })
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch (Exception error) when (error is InvalidOperationException or
                System.ComponentModel.Win32Exception or System.Security.SecurityException) { continue; }
            foreach (var process in processes) yield return process;
        }
    }

    private static IEnumerable<string> CandidateExecutables()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (path.EndsWith(",0", StringComparison.Ordinal)) path = path[..^2].Trim('"');
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && seen.Add(path))
                _candidates.Add(path);
        }

        _candidates.Clear();
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using (var appPath = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\3uTools.exe"))
                    Add(appPath?.GetValue(null) as string);

                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (string subName in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(subName);
                    string? displayName = entry?.GetValue("DisplayName") as string;
                    if (displayName?.Contains("3uTools", StringComparison.OrdinalIgnoreCase) != true) continue;

                    Add(Path.Combine(entry?.GetValue("InstallLocation") as string ?? string.Empty, "3uTools.exe"));
                    Add(entry?.GetValue("DisplayIcon") as string);
                    string? uninstallString = entry?.GetValue("UninstallString") as string;
                    if (uninstallString is not null)
                    {
                        int exeEnd = uninstallString.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                        if (exeEnd >= 0) Add(uninstallString[..(exeEnd + 4)].Trim().Trim('"'));
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or ArgumentException)
            {
            }
        }

        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        ];
        foreach (string root in roots.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            Add(Path.Combine(root, "3uTools", "3uTools.exe"));
            Add(Path.Combine(root, "Programs", "3uTools", "3uTools.exe"));
        }

        return _candidates.ToArray();
    }

    // Candidate enumeration is only called from the UI thread, but avoid
    // exposing registry-backed mutable state outside this helper.
    private static readonly List<string> _candidates = new();
}
