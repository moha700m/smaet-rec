using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SmaetRec.App;

public sealed record NetworkProfileInfo(string? Name, string? Alias, string Category, string Ipv4, IReadOnlyList<string> Addresses);

/// <summary>
/// Checks the things that decide whether an iPhone can see the AirPlay service at all:
/// the active network profile, the Windows Firewall rule, and the local addresses we announce on.
/// The repair action is the only part that asks for administrator rights.
/// </summary>
public static class NetworkDiagnostics
{
    private static string PowerShell =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    public static async Task<NetworkProfileInfo?> GetNetworkProfileAsync()
    {
        const string script = """
            $p = Get-NetConnectionProfile -ErrorAction SilentlyContinue |
                 Sort-Object @{ Expression = {
                     if ($_.IPv4Connectivity -eq 'Internet') { 0 }
                     elseif ($_.IPv4Connectivity -eq 'LocalNetwork') { 1 }
                     else { 2 }
                 } } |
                 Select-Object -First 1 Name,InterfaceAlias,NetworkCategory,IPv4Connectivity
            if ($p) { $p | ConvertTo-Json -Compress }
            """;
        string? json = await RunPowerShellAsync(script, 6000).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json.Trim());
            JsonElement root = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().FirstOrDefault()
                : doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? name = root.TryGetProperty("Name", out var n) ? n.GetString() : null;
            string? alias = root.TryGetProperty("InterfaceAlias", out var a) ? a.GetString() : null;
            string category = ReadCategory(root);
            string connectivity = root.TryGetProperty("IPv4Connectivity", out var c) ? c.ToString() : "";
            var addresses = LocalAddresses();
            string ipv4 = addresses.FirstOrDefault() ?? "";
            return new NetworkProfileInfo(name, alias, category, ipv4, addresses);
        }
        catch (JsonException) { return null; }
    }

    private static string ReadCategory(JsonElement root)
    {
        if (!root.TryGetProperty("NetworkCategory", out var c)) return "Unknown";
        if (c.ValueKind == JsonValueKind.Number)
            return c.GetInt32() switch { 0 => "Public", 1 => "Private", 2 => "Domain", _ => "Unknown" };
        string? s = c.GetString();
        return s ?? "Unknown";
    }

    public static IReadOnlyList<string> LocalAddresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var u in nic.GetIPProperties().UnicastAddresses)
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork && !result.Contains(u.Address.ToString()))
                        result.Add(u.Address.ToString());
            }
        }
        catch (NetworkInformationException) { }
        return result;
    }

    /// <summary>True when at least one enabled inbound allow rule for SMAET REC exists. Null when it cannot be checked.</summary>
    public static async Task<bool?> FirewallRulePresentAsync()
    {
        const string script = """
            $rules = Get-NetFirewallRule -DisplayName 'SMAET REC*' -ErrorAction SilentlyContinue |
                     Where-Object { $_.Enabled -eq 'True' -and $_.Direction -eq 'Inbound' -and $_.Action -eq 'Allow' }
            @($rules).Count
            """;
        string? output = await RunPowerShellAsync(script, 6000).ConfigureAwait(false);
        return int.TryParse(output?.Trim(), out int count) ? count > 0 : null;
    }

    /// <summary>Adds the program and mDNS firewall rules (UAC), and makes the active network private when needed.</summary>
    public static async Task<(bool ok, string message)> RepairAsync(NetworkProfileInfo? profile)
    {
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SmaetRec.exe");
        bool makePrivate = profile is { Category: "Public" } && !string.IsNullOrWhiteSpace(profile.Alias);
        string script = $"""
            $ErrorActionPreference = 'SilentlyContinue'
            $exe = '{Escape(exe)}'
            netsh advfirewall firewall delete rule name="SMAET REC" | Out-Null
            netsh advfirewall firewall add rule name="SMAET REC" dir=in action=allow program="$exe" enable=yes profile=private,domain | Out-Null
            netsh advfirewall firewall delete rule name="SMAET REC mDNS" | Out-Null
            netsh advfirewall firewall add rule name="SMAET REC mDNS" dir=in action=allow program="$exe" protocol=UDP localport=5353 enable=yes profile=private,domain | Out-Null
            """;
        if (makePrivate)
            script += $"\nSet-NetConnectionProfile -InterfaceAlias '{Escape(profile!.Alias!)}' -NetworkCategory Private -ErrorAction SilentlyContinue\n";
        script += "\nexit 0\n";

        string temp = Path.Combine(Path.GetTempPath(), "smaet-repair-" + Guid.NewGuid().ToString("N") + ".ps1");
        try
        {
            await File.WriteAllTextAsync(temp, script, new UTF8Encoding(false)).ConfigureAwait(false);
            var psi = new ProcessStartInfo
            {
                FileName = PowerShell,
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{temp}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(psi);
            if (process == null) return (false, "Could not start the repair");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40)).ConfigureAwait(false);
            if (process.ExitCode != 0) return (false, "Windows returned " + process.ExitCode);
            bool? rule = await FirewallRulePresentAsync().ConfigureAwait(false);
            return rule == true
                ? (true, makePrivate ? "Firewall updated and the network was set to Private." : "Firewall updated.")
                : (false, "The firewall rule could not be confirmed.");
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            return (false, "Administrator permission was cancelled.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            return (false, e.Message);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Escape(string value) => value.Replace("'", "''");

    private static async Task<string?> RunPowerShellAsync(string script, int timeoutMs)
    {
        try
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo
            {
                FileName = PowerShell,
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeoutMs);
            try { await process.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } return null; }
            string output = await stdout.ConfigureAwait(false);
            _ = await stderr.ConfigureAwait(false);
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    public static void OpenNetworkSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public static void OpenAppleDevicesStore()
    {
        try { Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9NP83LWLPZ9K") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception)
        {
            try { Process.Start(new ProcessStartInfo("https://apps.microsoft.com/detail/9np83lwlpz9k") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
