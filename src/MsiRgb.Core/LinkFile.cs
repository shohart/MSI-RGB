using System;
using System.Globalization;
using System.IO;

namespace MsiRgb;

/// <summary>%LOCALAPPDATA%\MsiRgb\fancontrol.txt: "percent unixMillis". Shared with the app (same user).</summary>
internal static class LinkFile
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MsiRgb", "fancontrol.txt");

    public static void Write(string path, float percent)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, string.Create(CultureInfo.InvariantCulture,
            $"{percent:0.##} {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Returns the percent if the file was refreshed within maxAge, else null.</summary>
    public static float? Read(string path, TimeSpan maxAge)
    {
        try
        {
            var parts = File.ReadAllText(path).Split(' ');
            if (parts.Length != 2) return null;
            var pct = float.Parse(parts[0], CultureInfo.InvariantCulture);
            var at = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(parts[1], CultureInfo.InvariantCulture));
            return DateTimeOffset.UtcNow - at <= maxAge ? pct : null;
        }
        catch { return null; }
    }
}
