using System.Globalization;
using HanumanInstitute.LibMpv;

namespace MyPlayer;

/// <summary>
/// Our mpv context: embeds video into a foreign window handle and loads the
/// user's mpv.conf / input.conf from %APPDATA%\MyPlayer (same setup as mpv).
/// </summary>
public sealed class PlayerMpvContext : MpvContext
{
    // The base constructor calls OnPreInitialize() BEFORE this class's fields
    // are assigned (standard C# base-first construction), so the window handle
    // is handed over through a thread-static slot consumed during construction.
    [ThreadStatic] private static IntPtr _pendingWid;

    /// <summary>Creates the context, embedding mpv output into <paramref name="wid"/>.</summary>
    public static PlayerMpvContext Create(IntPtr wid)
    {
        _pendingWid = wid;
        try
        {
            return new PlayerMpvContext();
        }
        finally
        {
            _pendingWid = IntPtr.Zero;
        }
    }

    private PlayerMpvContext()
    {
    }

    /// <summary>
    /// Runs between mpv_create() and mpv_initialize() — the only point where
    /// pre-init options like wid / config / config-dir may be set.
    /// </summary>
    protected override void OnPreInitialize()
    {
        // On Windows the handle must be truncated to 32 bits (same as mpv's
        // official Qt embedding example).
        long wid = (long)(uint)_pendingWid.ToInt64();
        SetOptionString("wid", wid.ToString(CultureInfo.InvariantCulture));

        // Load mpv.conf + input.conf + scripts\ from our config dir — this is
        // what makes the "same setup as mpv" promise real.
        SetOptionString("config-dir", ConfigManager.ConfigDir);
        SetOptionString("config", "yes");

        // Keys come from the user's input.conf only; keys it doesn't bind fall
        // through to MyPlayer's own handling (fullscreen etc.).
        SetOptionString("input-default-bindings", "no");
    }
}
