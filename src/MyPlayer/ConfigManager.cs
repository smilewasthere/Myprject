using System.IO;
using System.Reflection;

namespace MyPlayer;

/// <summary>
/// Manages the mpv configuration directory (%APPDATA%\MyPlayer).
/// On first run, writes the default mpv.conf / input.conf shipped inside the exe.
/// The files are plain mpv config files — edit them like any mpv setup.
/// </summary>
public static class ConfigManager
{
    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyPlayer");

    public static string MpvConfPath => Path.Combine(ConfigDir, "mpv.conf");
    public static string InputConfPath => Path.Combine(ConfigDir, "input.conf");

    /// <summary>Creates the config dir and writes default files if missing.</summary>
    public static void EnsureDefaults()
    {
        Directory.CreateDirectory(ConfigDir);
        ExtractResourceIfMissing("MyPlayer.Defaults.mpv.conf", MpvConfPath);
        ExtractResourceIfMissing("MyPlayer.Defaults.input.conf", InputConfPath);
    }

    private static void ExtractResourceIfMissing(string resourceName, string targetPath)
    {
        if (File.Exists(targetPath))
            return;

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var file = File.Create(targetPath);
        stream.CopyTo(file);
    }
}
