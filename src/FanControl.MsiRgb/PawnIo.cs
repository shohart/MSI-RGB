using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Win32;

namespace FanControl.MsiRgb;

/// <summary>
/// Minimal PawnIO client for the signed SmbusI801 module (Intel i801 SMBus host).
/// PawnIO is installed system-wide by FanControl / LibreHardwareMonitor.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PawnIoSmbus : IDisposable
{
    private const string Lib = "PawnIOLib";
    private const int SmbusRead = 1;
    private const int SmbusWrite = 0;
    private const int ProtoByteData = 2;

    // Shared with LibreHardwareMonitor (FanControl's own RAM temperature reads)
    private readonly Mutex _busMutex = new(false, @"Global\Access_SMBUS.HTP.Method");
    private IntPtr _handle;

    static PawnIoSmbus()
    {
        NativeLibrary.SetDllImportResolver(typeof(PawnIoSmbus).Assembly, (name, _, _) =>
        {
            if (name != Lib) return IntPtr.Zero;
            foreach (var dir in CandidateDirs())
            {
                var path = Path.Combine(dir, "PawnIOLib.dll");
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var h)) return h;
            }
            return IntPtr.Zero;
        });
    }

    private static System.Collections.Generic.IEnumerable<string> CandidateDirs()
    {
        string? fromRegistry = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            fromRegistry = key?.GetValue("InstallLocation") as string;
        }
        catch { }
        if (!string.IsNullOrEmpty(fromRegistry)) yield return fromRegistry!;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO");
    }

    [DllImport(Lib)] private static extern int pawnio_open(out IntPtr handle);
    [DllImport(Lib)] private static extern int pawnio_load(IntPtr handle, byte[] blob, UIntPtr size);
    [DllImport(Lib)] private static extern int pawnio_close(IntPtr handle);
    [DllImport(Lib)]
    private static extern int pawnio_execute(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string name,
        long[] input, UIntPtr inSize, long[] output, UIntPtr outSize, out UIntPtr returnSize);

    public static PawnIoSmbus Open()
    {
        int hr = pawnio_open(out var handle);
        if (hr != 0) throw new InvalidOperationException($"PawnIO not available (pawnio_open 0x{hr:X8}). Is PawnIO installed and FanControl running as admin?");
        var dev = new PawnIoSmbus { _handle = handle };
        byte[] blob;
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SmbusI801.bin")
                       ?? throw new InvalidOperationException("embedded SmbusI801.bin missing"))
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            blob = ms.ToArray();
        }
        hr = pawnio_load(handle, blob, (UIntPtr)blob.Length);
        if (hr != 0)
        {
            dev.Dispose();
            throw new InvalidOperationException($"PawnIO SmbusI801 module rejected (0x{hr:X8}); no Intel i801 SMBus?");
        }
        return dev;
    }

    private PawnIoSmbus() { }

    private int Execute(string fn, long[] input, long[] output)
        => pawnio_execute(_handle, fn, input, (UIntPtr)input.Length, output, (UIntPtr)output.Length, out _);

    /// <summary>PCI identity of the SMBus controller: subsystem in the high 32 bits (SSID:SVID), device:vendor low.</summary>
    public ulong PciIdentity()
    {
        var o = new long[3];
        int hr = Execute("ioctl_identity", Array.Empty<long>(), o);
        if (hr != 0) throw new InvalidOperationException($"ioctl_identity 0x{hr:X8}");
        return unchecked((ulong)o[2]);
    }

    /// <summary>Reads one byte register; null on NACK / bus error.</summary>
    public int? ReadByte(int addr, int reg)
    {
        var o = new long[1];
        int hr = Locked(() => Execute("ioctl_smbus_xfer", new long[] { addr, SmbusRead, reg, ProtoByteData }, o));
        return hr == 0 ? (int)(o[0] & 0xFF) : null;
    }

    public bool WriteByte(int addr, int reg, int value)
    {
        int hr = Locked(() => Execute("ioctl_smbus_xfer", new long[] { addr, SmbusWrite, reg, ProtoByteData, value & 0xFF }, Array.Empty<long>()));
        return hr == 0;
    }

    // One transaction per lock; never hold the bus across sleeps.
    private int Locked(Func<int> f)
    {
        bool got;
        try { got = _busMutex.WaitOne(500); }
        catch (AbandonedMutexException) { got = true; }
        if (!got) return -1;
        try { return f(); }
        finally { _busMutex.ReleaseMutex(); }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            pawnio_close(_handle);
            _handle = IntPtr.Zero;
        }
        _busMutex.Dispose();
    }
}
