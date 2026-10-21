using System;
using System.Runtime.Versioning;
using System.Threading;

namespace MsiRgb;

/// <summary>
/// MSI motherboard RGB MCU (Renesas, MSI "chipset" 0x26 / Renesas6) on the chipset SMBus.
/// Protocol recovered from MSI Mystic Light (MBAPI_x86.dll RenesasLEDControlV3).
///
/// SAFETY: on these boards 7-bit 0x50/0x52 are the DIMM SPD EEPROMs and SPD write
/// protection is NOT enabled. MSI writes the MCU as "0x52" in 8-bit notation, which is
/// 7-bit 0x29. The address is a constant on purpose and is never configurable.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class RenesasLed : IDisposable
{
    public const int Address = 0x29;

    private const int RegZoneMask0 = 0x80; // 0x80..0x83
    private const int RegBrightness = 0x87;
    private const int RegRed = 0x88;
    private const int RegGreen = 0x89;
    private const int RegBlue = 0x8A;      // latches the colour
    private const int RegMode = 0x9F;
    private const int ModeStatic = 2;
    private const int BrightnessMax = 31;
    private const int SettleMs = 30;

    /// <summary>Boards verified to use this MCU at 0x29: SMBus PCI subsystem (SSID:SVID).</summary>
    private static readonly uint[] SupportedSubsystems =
    {
        0x7B431462, // MS-7B43 Z370I GAMING PRO CARBON AC
    };

    private readonly PawnIoSmbus _bus;

    private RenesasLed(PawnIoSmbus bus) => _bus = bus;

    /// <summary>Opens the bus and checks this really is a supported board with the MCU present.</summary>
    public static RenesasLed Open(Action<string> log)
    {
        var bus = PawnIoSmbus.Open();
        try
        {
            ulong id = bus.PciIdentity();
            uint subsystem = (uint)(id >> 32);
            log($"SMBus controller id 0x{id:X16}");
            if (Array.IndexOf(SupportedSubsystems, subsystem) < 0)
                throw new NotSupportedException($"board subsystem {subsystem:X8} is not in the verified list; refusing to write the SMBus");

            // The MCU is effectively write-only (reads return 0xFF once it has been configured),
            // so presence = it ACKs its address. The subsystem check above is the real gate.
            if (bus.ReadByte(Address, 0x00) is null)
                throw new NotSupportedException($"RGB MCU not found at 0x{Address:X2} (no ACK)");

            return new RenesasLed(bus);
        }
        catch
        {
            bus.Dispose();
            throw;
        }
    }

    /// <summary>Static mode + full brightness for the given zones. May blink black once.</summary>
    public void Init(ZoneMask zones)
    {
        WriteMask(zones);
        Write(RegRed, 0);
        Write(RegGreen, 0);
        Write(RegBlue, 0);
        Repeat(RegMode, ModeStatic);
        WriteMask(zones);
        Repeat(RegBrightness, BrightnessMax);
    }

    /// <param name="fast">Animation frames: single latch write instead of MSI's 3x30 ms (≈10x faster).</param>
    public void SetColor(ZoneMask zones, Rgb c, bool fast = false)
    {
        WriteMask(zones);
        Write(RegRed, c.R);
        Write(RegGreen, c.G);
        if (fast) Write(RegBlue, c.B);
        else Repeat(RegBlue, c.B);
    }

    private void WriteMask(ZoneMask z)
    {
        Write(RegZoneMask0, z.M0);
        Write(RegZoneMask0 + 1, z.M1);
        Write(RegZoneMask0 + 2, z.M2);
        Write(RegZoneMask0 + 3, z.M3);
    }

    // MSI writes the latching register three times with a 30 ms gap; the MCU misses single writes.
    private void Repeat(int reg, int value)
    {
        for (int i = 0; i < 3; i++)
        {
            Write(reg, value);
            Thread.Sleep(SettleMs);
        }
    }

    private void Write(int reg, int value)
    {
        if (!_bus.WriteByte(Address, reg, value))
            throw new System.IO.IOException($"SMBus write 0x{Address:X2}[0x{reg:X2}] failed");
    }

    public void Dispose() => _bus.Dispose();
}
