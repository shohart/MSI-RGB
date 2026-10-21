using System;
using System.Linq;
using FanControl.MsiRgb;
using Xunit;

public class GradientTests
{
    private static readonly Gradient BlueToRed = new(new[]
    {
        (0f, new Rgb(0, 0, 255)),
        (100f, new Rgb(255, 0, 0)),
    });

    [Theory]
    [InlineData(-10f, 0, 0, 255)]
    [InlineData(0f, 0, 0, 255)]
    [InlineData(50f, 128, 0, 128)]
    [InlineData(100f, 255, 0, 0)]
    [InlineData(150f, 255, 0, 0)]
    [InlineData(float.NaN, 0, 0, 255)]
    public void Interpolates_and_clamps(float p, int r, int g, int b)
        => Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), BlueToRed.At(p));

    [Fact]
    public void Unordered_stops_are_sorted()
    {
        var g = new Gradient(new[] { (100f, new Rgb(255, 0, 0)), (0f, new Rgb(0, 0, 255)), (50f, new Rgb(0, 255, 0)) });
        Assert.Equal(new Rgb(0, 255, 0), g.At(50));
        Assert.Equal(new Rgb(0, 128, 128), g.At(25));
    }

    [Fact]
    public void Single_stop_is_constant() => Assert.Equal(new Rgb(1, 2, 3), new Gradient(new[] { (40f, new Rgb(1, 2, 3)) }).At(90));
}

public class ModelTests
{
    [Fact]
    public void Parses_hex_colour()
    {
        Assert.Equal(new Rgb(0x12, 0xAB, 0xEF), Rgb.Parse("#12abef"));
        Assert.Equal(new Rgb(0xFF, 0, 0), Rgb.Parse("FF0000"));
        Assert.Throws<FormatException>(() => Rgb.Parse("#FFF"));
    }

    [Fact]
    public void Zone_masks_match_msi_board_table()
    {
        Assert.Equal(new ZoneMask(0x01, 0, 0, 0), ZoneMask.Parse("JRGB"));
        Assert.Equal(new ZoneMask(0xFE, 0x01, 0, 0), ZoneMask.Parse("board"));
        Assert.Throws<FormatException>(() => ZoneMask.Parse("0x52"));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(50f, 128)]
    [InlineData(100f, 255)]
    [InlineData(120f, 255)]
    [InlineData(-5f, 0)]
    public void Percent_to_byte(float p, int expected) => Assert.Equal((byte)expected, Channel.ToByte(p));

    [Fact]
    public void Rgb_channel_uses_default_until_set()
    {
        var ch = new Channel(new ChannelConfig { Mode = "rgb", Default = "#102030" });
        Assert.Equal(new Rgb(0x10, 0x20, 0x30), ch.Target());
        ch.SetComponent(0, 100);
        Assert.Equal(new Rgb(0xFF, 0x20, 0x30), ch.Target());
        ch.SetComponent(0, null);
        Assert.Equal(new Rgb(0x10, 0x20, 0x30), ch.Target());
    }

    [Fact]
    public void Gradient_channel_maps_percent()
    {
        var ch = new Channel(new ChannelConfig());
        Assert.Equal(Rgb.Parse("#0040FF"), ch.Target());
        ch.SetGradient(100);
        Assert.Equal(Rgb.Parse("#FF0000"), ch.Target());
    }
}

public class ConfigTests
{
    [Fact]
    public void Default_config_roundtrips()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new PluginConfig(), PluginConfig.Json);
        var cfg = PluginConfig.Parse(json);
        Assert.Single(cfg.Channels);
        Assert.Equal(4, cfg.Channels[0].Stops.Count);
    }

    [Fact]
    public void Two_zone_config_with_comments()
    {
        var cfg = PluginConfig.Parse("""
        {
          // CPU drives the board, GPU the strip
          "channels": [
            { "id": "board", "name": "Board RGB", "zones": "board" },
            { "id": "strip", "name": "Strip RGB", "zones": "jrgb", "mode": "rgb" },
          ]
        }
        """);
        Assert.Equal(new[] { "board", "strip" }, cfg.Channels.Select(c => c.Id));
        Assert.True(cfg.Channels[1].IsRgb);
    }

    [Theory]
    [InlineData("""{ "channels": [] }""")]
    [InlineData("""{ "channels": [ { "id": "a" }, { "id": "a" } ] }""")]
    [InlineData("""{ "channels": [ { "zones": "spd" } ] }""")]
    [InlineData("""{ "channels": [ { "mode": "rainbow" } ] }""")]
    [InlineData("""{ "channels": [ { "stops": [ { "at": 0, "color": "red" } ] } ] }""")]
    public void Rejects_bad_config(string json) => Assert.ThrowsAny<FormatException>(() => PluginConfig.Parse(json));
}
