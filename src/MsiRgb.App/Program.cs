using System;
using System.Threading;
using System.Windows.Forms;

namespace MsiRgb;

internal static class Program
{
    private const string InstanceName = @"Global\MsiRgbApp";
    private const string ShowEventName = @"Global\MsiRgbApp.Show";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, InstanceName, out bool first);
        if (!first)
        {
            // already running: ask it to show its window
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            return;
        }

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ApplicationConfiguration.Initialize();
        bool startHidden = Array.Exists(args, a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        Application.Run(new TrayApp(showEvent, startHidden));
    }
}
