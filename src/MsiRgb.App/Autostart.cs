using System;
using System.Diagnostics;

namespace MsiRgb;

/// <summary>Logon task with highest privileges: PawnIO needs admin, and a task avoids a UAC prompt at every logon.</summary>
internal static class Autostart
{
    private const string TaskName = "MSI RGB";

    public static bool IsEnabled() => Schtasks($"/query /tn \"{TaskName}\"") == 0;

    public static void Set(bool enabled)
    {
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
            int rc = Schtasks($"/create /tn \"{TaskName}\" /tr \"\\\"{exe}\\\" --tray\" /sc onlogon /rl highest /it /f");
            if (rc != 0) throw new InvalidOperationException($"schtasks /create failed ({rc})");
        }
        else if (IsEnabled())
        {
            Schtasks($"/delete /tn \"{TaskName}\" /f");
        }
    }

    private static int Schtasks(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        p.WaitForExit(10000);
        return p.ExitCode;
    }
}
