using System;
using System.IO;

namespace SmaetRec.Tests;

public sealed class InstallerTests
{
    private static string ReadRepositoryFile(string relativePath)
    {
        foreach (var start in new[]
        {
            new DirectoryInfo(Directory.GetCurrentDirectory()),
            new DirectoryInfo(AppContext.BaseDirectory)
        })
        {
            for (var current = start; current is not null; current = current.Parent)
            {
                var candidate = Path.Combine(current.FullName, relativePath);
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repository file '{relativePath}' from the test working directory.");
    }

    private static string Workflow =>
        ReadRepositoryFile(Path.Combine(".github", "workflows", "build.yml"));

    private static string Installer =>
        ReadRepositoryFile(Path.Combine("installer", "SmaetRec.iss"));

    [Fact]
    public void Workflow_uses_NetSecurity_firewall_objects()
        => Assert.Contains("Get-NetFirewallRule", Workflow);

    [Fact]
    public void Workflow_has_a_netsh_fallback_for_firewall_inspection()
        => Assert.Contains("netsh.exe advfirewall firewall show rule", Workflow);

    [Fact]
    public void Workflow_checks_both_firewall_rules_created_by_the_installer()
    {
        Assert.Contains("SMAET REC", Workflow);
        Assert.Contains("SMAET REC mDNS", Workflow);
    }

    [Fact]
    public void Workflow_does_not_parse_the_localized_netsh_rule_heading()
        => Assert.False(Workflow.Contains("Rule Name", StringComparison.Ordinal));

    [Fact]
    public void Workflow_captures_a_separate_install_log_for_each_round()
        => Assert.Contains("smaet-install-$round.log", Workflow);

    [Fact]
    public void Workflow_captures_a_separate_uninstall_log_for_each_round()
        => Assert.Contains("smaet-uninstall-$round.log", Workflow);

    [Fact]
    public void Workflow_checks_the_installer_exit_code()
        => Assert.Contains("$install.ExitCode -ne 0", Workflow);

    [Fact]
    public void Workflow_checks_the_uninstaller_exit_code()
        => Assert.Contains("$uninstall.ExitCode -ne 0", Workflow);

    [Fact]
    public void Workflow_checks_the_installed_application_and_usb_host()
    {
        Assert.Contains("SmaetRec.exe", Workflow);
        Assert.Contains("iphone-usb-host.exe", Workflow);
    }

    [Fact]
    public void Workflow_runs_the_installed_application_and_checks_shutdown()
    {
        Assert.Contains("--dev-simulated-source", Workflow);
        Assert.Contains("CloseMainWindow()", Workflow);
        Assert.Contains("WaitForExit(15000)", Workflow);
    }

    [Fact]
    public void Workflow_waits_for_uninstall_cleanup()
    {
        Assert.Contains("AddSeconds(15)", Workflow);
        Assert.Contains("PresentNames", Workflow);
    }

    [Fact]
    public void Workflow_reports_pending_file_rename_state_when_diagnosing()
        => Assert.Contains("PendingFileRenameOperations", Workflow);

    [Fact]
    public void Workflow_treats_missing_pending_file_rename_registry_value_as_optional()
    {
        Assert.Contains("OpenSubKey", Workflow);
        Assert.Contains("GetValue(\"PendingFileRenameOperations\", $null)", Workflow);
        Assert.DoesNotContain("Get-ItemPropertyValue", Workflow);
        Assert.Contains("No PendingFileRenameOperations registry value is present", Workflow);
        Assert.Contains("Final diagnostics could not be collected", Workflow);
    }

    [Fact]
    public void Workflow_uploads_installer_diagnostics_even_when_the_test_fails()
    {
        Assert.Contains("name: installer-test-diagnostics", Workflow);
        Assert.Contains("if: always()", Workflow);
        Assert.Contains("if-no-files-found: ignore", Workflow);
    }

    [Fact]
    public void Installer_adds_and_removes_both_named_firewall_rules()
    {
        Assert.Contains("name=\"\"SMAET REC\"\"", Installer);
        Assert.Contains("name=\"\"SMAET REC mDNS\"\"", Installer);
        Assert.Contains("delete rule name=\"\"SMAET REC\"\"", Installer);
        Assert.Contains("delete rule name=\"\"SMAET REC mDNS\"\"", Installer);
    }
}
