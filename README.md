# FanControl MSI RGB

A tiny [FanControl](https://github.com/Rem0o/FanControl.Releases) plugin that drives the RGB
lighting of MSI motherboards whose LEDs are run by MSI's **Renesas MCU** — without MSI Center /
Mystic Light.

The plugin adds a *control* called **MSI RGB** to FanControl. Assign it any curve you like (graph,
linear, mix of sensors, custom sensor…) exactly as you would for a fan. The 0–100 % output is turned
into a colour through a gradient (default: blue → green → amber → red). So "RGB follows CPU temperature"
is just a graph curve on the CPU sensor.

## Supported boards

| Board | SMBus subsystem | Status |
|---|---|---|
| MSI Z370I GAMING PRO CARBON AC (MS-7B43) | 1462:7B43 | verified |

Other MSI boards that Mystic Light lists with chipset `Renesas2…6` very likely use the same
protocol, but the plugin refuses to write to unverified boards. Open an issue with your board's
`MS-xxxx` and the plugin log line `SMBus controller id …` to add it.

Boards whose RGB is on the Nuvoton Super I/O (MSI-RGB / OpenRGB "MSI-RGB") or on the newer USB
Mystic Light controller (Z390+) are **not** this protocol — use OpenRGB for those.

## Install

1. Install FanControl (it brings [PawnIO](https://pawnio.eu), the signed driver this plugin uses).
2. **Uninstall MSI Center** (or at least disable `Mystic_Light_Service` / `LightKeeperService`):
   Mystic Light resets the MCU at start-up and fights any other writer.
3. Copy `FanControl.MsiRgb.dll` into `FanControl\Plugins\`, restart FanControl.
4. In FanControl, the **Controls** section now shows **MSI RGB**. Create e.g. a *Graph* curve on
   `CPU Package` (40 °C → 0 %, 85 °C → 100 %) and select it for MSI RGB.

## Configuration

`Plugins\FanControl.MsiRgb.json` is created on first run:

```jsonc
{
  "channels": [
    {
      "id": "all",            // stable id - FanControl remembers your curve by it
      "name": "MSI RGB",
      "zones": "all",         // all | jrgb (12 V 4-pin header) | board (on-board LEDs)
      "mode": "gradient",     // gradient: one control; rgb: three controls (Red/Green/Blue %)
      "default": "#0040FF",   // shown until FanControl drives the control / after release
      "stops": [
        { "at": 0,   "color": "#0040FF" },
        { "at": 40,  "color": "#00FF40" },
        { "at": 70,  "color": "#FFC000" },
        { "at": 100, "color": "#FF0000" }
      ]
    }
  ],
  "reassertSeconds": 60      // re-send the colour periodically (sleep/resume, other writers)
}
```

Two channels (e.g. `board` on CPU temperature, `jrgb` on GPU temperature) give two independent
controls. Restart FanControl after editing.

## How it works

Reverse-engineered from MSI's `MBAPI_x86.dll` (`RenesasLEDControlV3`). Byte-data writes to SMBus
7-bit address **0x29** (MSI writes it as `0x52`, 8-bit notation):

| Reg | Meaning |
|---|---|
| 0x80–0x83 | zone bitmask (JRGB1 = 0x80 bit 0, board zones = 0x80 bits 1–7 and 0x81 bit 0) |
| 0x9F | mode; `2` = static (MSI first clears 0x88–0x8A) |
| 0x87 | brightness, 31 = max |
| 0x88 / 0x89 / 0x8A | R / G / B; writing 0x8A latches the colour |

The latching register is written 3× with a 30 ms gap, as MSI does. The MCU reads back 0xFF.

## ⚠️ Safety

On this board **7-bit 0x50 and 0x52 are the DDR4 SPD EEPROMs and SPD write protection is off**.
A tool that took MSI's "0x52" literally as a 7-bit address would overwrite a DIMM's SPD. The
address in this plugin is a hard-coded constant and writes are gated on the exact board ID.

All SMBus traffic goes through PawnIO's signed `SmbusI801` module and the shared
`Global\Access_SMBUS.HTP.Method` mutex, so it coexists with LibreHardwareMonitor inside FanControl.

## Build

```
# put FanControl.Plugins.dll from your FanControl folder into lib/ (or pass -p:FanControlDir=...)
dotnet test
dotnet build src/FanControl.MsiRgb -c Release
```

License: MIT (plugin), LGPL-2.1 (embedded PawnIO module).
