using System;
using System.IO;
using MsiRgb;
using Xunit;

public class RgbTests
{
    [Fact]
    public void Parses_hex_and_names()
    {
        Assert.Equal(new Rgb(0x12, 0xAB, 0xEF), Rgb.Parse("#12abef"));
        Assert.Equal(new Rgb(0xFF, 0, 0), Rgb.Parse("FF0000"));
        Assert.Equal(new Rgb(255, 0, 0), Rgb.Parse(" Красный "));
        Assert.Equal(Rgb.Parse("желтый"), Rgb.Parse("жёлтый"));
        Assert.Throws<FormatException>(() => Rgb.Parse("#FFF"));
    }

    [Fact]
    public void Lerp_clamps() 
    {
        var a = new Rgb(0, 0, 255); var b = new Rgb(255, 0, 0);
        Assert.Equal(a, Rgb.Lerp(a, b, -1));
        Assert.Equal(new Rgb(128, 0, 128), Rgb.Lerp(a, b, 0.5f));
        Assert.Equal(b, Rgb.Lerp(a, b, 7));
    }

    [Fact]
    public void Scale_is_software_brightness() => Assert.Equal(new Rgb(128, 64, 0), new Rgb(255, 128, 0).Scale(0.5f));

    [Theory]
    [InlineData(0f, 255, 0, 0)]
    [InlineData(120f, 0, 255, 0)]
    [InlineData(240f, 0, 0, 255)]
    [InlineData(360f, 255, 0, 0)]
    [InlineData(-120f, 0, 0, 255)]
    public void Hsv(float h, int r, int g, int b) => Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), Rgb.FromHsv(h, 1, 1));

    [Fact]
    public void Zone_masks_match_msi_board_table()
    {
        Assert.Equal(new ZoneMask(0x01, 0, 0, 0), ZoneMask.Parse("JRGB"));
        Assert.Equal(new ZoneMask(0xFE, 0x01, 0, 0), ZoneMask.Parse("board"));
        Assert.Throws<FormatException>(() => ZoneMask.Parse("0x52"));
    }

    [Fact]
    public void Gradient_sorts_stops()
    {
        var g = new Gradient(new[] { (100f, new Rgb(255, 0, 0)), (0f, new Rgb(0, 0, 255)), (50f, new Rgb(0, 255, 0)) });
        Assert.Equal(new Rgb(0, 128, 128), g.At(25));
        Assert.Equal(new Rgb(0, 0, 255), g.At(float.NaN));
    }
}

public class ColorEngineTests
{
    private static readonly AppSettings Temp = new AppSettings
    {
        Mode = LightMode.Temperature, TempFrom = 40, TempTo = 80, TempColorFrom = "#0000FF", TempColorTo = "#FF0000",
    };

    [Theory]
    [InlineData(20f, 0, 0, 255)]
    [InlineData(40f, 0, 0, 255)]
    [InlineData(60f, 128, 0, 128)]
    [InlineData(80f, 255, 0, 0)]
    [InlineData(99f, 255, 0, 0)]
    public void Temperature_maps_from_to(float t, int r, int g, int b)
        => Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), ColorEngine.Compute(Temp, 0, t, null));

    [Fact]
    public void Missing_sensor_shows_from_colour() => Assert.Equal(new Rgb(0, 0, 255), ColorEngine.Compute(Temp, 0, null, null));

    [Fact]
    public void Brightness_applies_to_every_mode()
        => Assert.Equal(new Rgb(0, 0, 128), ColorEngine.Compute(Temp with { Brightness = 50 }, 0, 10, null));

    [Fact]
    public void FanControl_percent_and_no_link()
    {
        var s = new AppSettings { Mode = LightMode.FanControl, FanColorFrom = "#000000", FanColorTo = "#FFFFFF" };
        Assert.Equal(new Rgb(128, 128, 128), ColorEngine.Compute(s, 0, null, 50));
        Assert.Equal(new Rgb(0, 0, 0), ColorEngine.Compute(s, 0, 70, null));
    }

    [Fact]
    public void Breathing_goes_dim_and_full()
    {
        var s = new AppSettings { Mode = LightMode.Effect, Effect = EffectKind.Breathing, EffectColor1 = "#FF0000", EffectSpeed = 10 }; // 1 s period
        Assert.True(ColorEngine.Compute(s, 0, null, null).R < 20);
        Assert.Equal(255, ColorEngine.Compute(s, 0.5, null, null).R);
        Assert.True(ColorEngine.IsAnimated(s));
        Assert.False(ColorEngine.IsAnimated(Temp));
    }

    [Fact]
    public void Blink_and_rainbow()
    {
        var blink = new AppSettings { Mode = LightMode.Effect, Effect = EffectKind.Blink, EffectColor1 = "#00FF00", EffectSpeed = 10 };
        Assert.Equal(new Rgb(0, 255, 0), ColorEngine.Compute(blink, 0.25, null, null));
        Assert.Equal(new Rgb(0, 0, 0), ColorEngine.Compute(blink, 0.75, null, null));
        var rainbow = blink with { Effect = EffectKind.Rainbow };
        Assert.Equal(new Rgb(0, 255, 0), ColorEngine.Compute(rainbow, 1.0 / 3, null, null));
    }

    [Fact]
    public void Two_colour_crossfade()
    {
        var s = new AppSettings { Mode = LightMode.Effect, Effect = EffectKind.TwoColor, EffectColor1 = "#0000FF", EffectColor2 = "#FF0000", EffectSpeed = 10 };
        Assert.Equal(new Rgb(0, 0, 255), ColorEngine.Compute(s, 0, null, null));
        Assert.Equal(new Rgb(255, 0, 0), ColorEngine.Compute(s, 0.5, null, null));
    }

    [Fact]
    public void Speed_to_period() { Assert.Equal(10, ColorEngine.Period(1)); Assert.Equal(1, ColorEngine.Period(10)); Assert.Equal(1, ColorEngine.Period(99)); }
}

public class SettingsTests
{
    [Fact]
    public void Roundtrip_and_sanitize()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            new AppSettings { Mode = LightMode.Effect, Effect = EffectKind.Rainbow, Brightness = 40 }.Save(path);
            Assert.Contains("\"rainbow\"", File.ReadAllText(path));
            var s = AppSettings.Load(path);
            Assert.Equal(LightMode.Effect, s.Mode);
            Assert.Equal(40, s.Brightness);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Bad_values_are_repaired()
    {
        var s = new AppSettings { Brightness = 0, EffectSpeed = 50, TempFrom = 80, TempTo = 40, StaticColor = "bogus" }.Sanitized();
        Assert.Equal(5, s.Brightness);
        Assert.Equal(10, s.EffectSpeed);
        Assert.True(s.TempTo > s.TempFrom);
        Assert.Equal("#0040FF", s.StaticColor);
    }

    [Fact]
    public void Broken_file_gives_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{ not json");
        try { Assert.Equal(new AppSettings(), AppSettings.Load(path)); }
        finally { File.Delete(path); }
    }
}

public class LinkFileTests
{
    [Fact]
    public void Write_read_and_staleness()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "fc.txt");
        LinkFile.Write(path, 42.5f);
        Assert.Equal(42.5f, LinkFile.Read(path, TimeSpan.FromSeconds(10)));
        File.WriteAllText(path, "42 1000"); // 1970
        Assert.Null(LinkFile.Read(path, TimeSpan.FromSeconds(10)));
        Assert.Null(LinkFile.Read(path + ".missing", TimeSpan.FromSeconds(10)));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}
