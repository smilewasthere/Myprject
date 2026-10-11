using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using HanumanInstitute.LibMpv;
using Microsoft.Win32;

namespace MyPlayer;

public partial class MainWindow : Window
{
    private MpvContext? _mpv;
    private DispatcherTimer? _timer;
    private NativeHooks? _hooks;
    private string? _lastPath;
    private double? _duration;
    private bool _stopped;
    private bool _seeking;
    private bool _updatingUi;
    private bool _fullscreen;
    private bool _menuOpen;      // a menu/context menu is open — hooks stand down
    private WindowGeom? _savedGeom;
    private long? _contextMenuAt; // ticks of last hook-fired context menu (de-dup)

    public MainWindow()
    {
        InitializeComponent();
        // WPF sliders have no DragStarted/DragCompleted — use mouse events instead
        SeekSlider.PreviewMouseLeftButtonDown += (_, _) => _seeking = true;
    }

    // ---- lifecycle -------------------------------------------------------

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        RestoreWindowGeometry();
        try
        {
            _hooks = new NativeHooks();
            _hooks.IsMpvFocused = IsMpvFocused;
            _hooks.KeyPressed = OnHookKey;
            _hooks.RightButtonUp += p => Dispatcher.Invoke(() => ShowContextMenu(p));
            _hooks.LeftDoubleClick += _ => Dispatcher.Invoke(ToggleFullscreen);
            _hooks.Start(new WindowInteropHelper(this).Handle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Input hooks unavailable: " + ex.Message);
            _hooks = null;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => EnsureMpv();

    private void EnsureMpv()
    {
        if (_mpv is not null)
            return;

        ConfigManager.EnsureDefaults();

        try
        {
            _mpv = PlayerMpvContext.Create(VideoHost.Handle);
        }
        catch (DllNotFoundException)
        {
            MessageBox.Show(
                "libmpv-2.dll was not found.\n\n" +
                "Run this once from the repo folder (see README):\n" +
                "powershell -ExecutionPolicy Bypass -File scripts\\fetch-mpv.ps1",
                "MyPlayer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to initialize the mpv engine:\n\n" + ex.Message,
                "MyPlayer", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ExitFullscreen(silent: true);
        SaveWindowGeometry();
        _timer?.Stop();
        _hooks?.Dispose();
        _hooks = null;
        _mpv?.Dispose();
        _mpv = null;
    }

    // ---- periodic UI sync (250 ms) --------------------------------------

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_mpv is null)
            return;

        var duration = GetDouble("duration");
        var time = GetDouble("time-pos");
        _duration = duration;

        if (duration is > 0 && time is not null)
        {
            TimeText.Text = $"{FormatTime(time.Value)} / {FormatTime(duration.Value)}";
            if (!_seeking && !SeekSlider.IsMouseCaptureWithin)
            {
                _updatingUi = true;
                SeekSlider.Value = time.Value / duration.Value * 100.0;
                _updatingUi = false;
            }
            SeekSlider.IsEnabled = true;
        }
        else if (duration is null)
        {
            TimeText.Text = "00:00 / 00:00";
            SeekSlider.IsEnabled = false;
        }

        // Pause button glyph: show play when paused
        var paused = GetBool("pause");
        PlayButton.Content = paused == true ? "▶" : "II";

        // Volume slider + label (skip while user is dragging)
        var volume = GetDouble("volume");
        if (volume is not null && !VolumeSlider.IsMouseCaptureWithin)
        {
            _updatingUi = true;
            VolumeSlider.Value = Math.Clamp(volume.Value, 0, 300);
            VolumeLabel.Text = $"{Math.Round(volume.Value)}%";
            _updatingUi = false;
        }

        // Window title: media title first, VLC-style
        var title = GetString("media-title");
        Title = string.IsNullOrWhiteSpace(title) || title == "No file"
            ? "MyPlayer"
            : $"{title} - MyPlayer";
    }

    // ---- mpv helpers -----------------------------------------------------

    private double? GetDouble(string name)
    {
        try { return _mpv?.GetProperty<double>(name); }
        catch { return null; }
    }

    private bool? GetBool(string name)
    {
        try { return _mpv?.GetProperty<bool>(name); }
        catch { return null; }
    }

    private string? GetString(string name)
    {
        try { return _mpv?.GetProperty<string>(name); }
        catch { return null; }
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    // ---- commands --------------------------------------------------------

    private void Open_Click(object sender, RoutedEventArgs e) => OpenDialog();

    private void OpenDialog()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Media files|*.mp4;*.mkv;*.webm;*.avi;*.mov;*.wmv;*.flv;*.ts;*.m2ts;*.mpg;*.mpeg;*.m4v;*.ogv" +
                     "|Audio files|*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus" +
                     "|All files|*.*"
        };
        if (dlg.ShowDialog(this) == true)
            OpenPath(dlg.FileName);
    }

    private void OpenPath(string path)
    {
        if (_mpv is null)
            return;
        _lastPath = path;
        _stopped = false;
        ResetTrackBase(); // new file → re-probe track-list indexing
        _mpv.LoadFile(path, append: false, appendPlay: false, extraArgs: null!).Invoke();
        PlayButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        PrevButton.IsEnabled = true;
        NextButton.IsEnabled = true;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlay();

    private void TogglePlay()
    {
        if (_mpv is null)
            return;
        if (_stopped && _lastPath is not null)
        {
            OpenPath(_lastPath); // "stop" clears the playlist — reload the file
            return;
        }
        _mpv.RunCommandString("cycle pause");
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv is null)
            return;
        _mpv.RunCommandString("stop");
        _stopped = true;
        _duration = null;
        TimeText.Text = "00:00 / 00:00";
        SeekSlider.IsEnabled = false;
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
        => _mpv?.RunCommandString("cycle fullscreen");

    private void ExitFullscreen()
        => _mpv?.RunCommandString("set fullscreen no");

    // ---- seeking ---------------------------------------------------------

    private void SeekSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => CommitSeek();

    private void CommitSeek()
    {
        _seeking = false;
        if (_mpv is null || _duration is not double d || d <= 0)
            return;
        var seconds = SeekSlider.Value / 100.0 * d;
        _mpv.RunCommandString($"seek {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} absolute");
    }

    // ---- volume ----------------------------------------------------------

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        VolumeLabel.Text = $"{Math.Round(VolumeSlider.Value)}%";
        if (_updatingUi || _mpv is null)
            return;
        _mpv.SetPropertyDouble("volume", VolumeSlider.Value);
    }

    // ---- keyboard (shared table; covers WPF focus, hook covers mpv focus) --

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var focused = Keyboard.FocusedElement;

        // Let focused controls keep their own keys
        if (focused is System.Windows.Controls.Primitives.ButtonBase && key is Key.Space or Key.Enter)
            return;
        if (focused is System.Windows.Controls.Slider && key is Key.Left or Key.Right or Key.Up or Key.Down)
            return;

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (HandleShortcut(key, ctrl))
            e.Handled = true;
    }
    // ---- keyboard --------------------------------------------------------

    /// <summary>
    /// One keyboard table used both by WPF (PreviewKeyDown) and by the
    /// low-level hook when focus sits inside the native mpv window.
    /// Returns true when the key was handled.
    /// </summary>
    private bool HandleShortcut(Key key, bool ctrl)
    {
        if (_mpv is null)
            return false;

        switch (key)
        {
            case Key.O when ctrl:
                Open_Click(this, new RoutedEventArgs());
                break;
            case Key.N when ctrl:
                // "Open Network Stream" arrives in Phase 5 — act as no-op for now.
                return false;
            case Key.Space:
                _mpv.RunCommandString("cycle pause");
                break;
            case Key.Left:
                _mpv.RunCommandString(_stopped ? "playlist-prev force" : "seek -5 relative");
                break;
            case Key.Right:
                _mpv.RunCommandString(_stopped ? "playlist-next force" : "seek 5 relative");
                break;
            case Key.Up when ctrl:
                _mpv.RunCommandString("add volume 5");
                break;
            case Key.Down when ctrl:
                _mpv.RunCommandString("add volume -5");
                break;
            case Key.Up:
                _mpv.RunCommandString("seek 60 relative");
                break;
            case Key.Down:
                _mpv.RunCommandString("seek -60 relative");
                break;
            case Key.M:
                _mpv.RunCommandString("cycle mute");
                break;
            case Key.Enter:
                _mpv.RunCommandString("playlist-next force");
                break;
            case Key.F when !ctrl:
                ToggleFullscreen();
                break;
            case Key.Escape:
                if (_fullscreen)
                    ExitFullscreen();
                break;
            default:
                return false;
        }
        return true;
    }

    /// <summary>Hook path: same table, invoked only when mpv has the focus.</summary>
    private bool OnHookKey(Key key, bool ctrl) => !_menuOpen && HandleShortcut(key, ctrl);

    private bool IsMpvFocused()
    {
        try
        {
            var focused = GetFocus();
            if (focused == IntPtr.Zero)
                return false;
            var mpvHwnd = VideoHost.Handle;
            var mpvChild = FindWindowEx(mpvHwnd, IntPtr.Zero, null, null);
            return focused == mpvHwnd || focused == mpvChild;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? cls, string? title);

    // ---- drag & drop -----------------------------------------------------

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            OpenPath(files[0]);
    }

    private sealed record TrackInfo(int Index, int Id, string Type, string Label, bool Selected);

    // mpv documents node-array paths as track-list/N/... but never pins down
    // the base; resolve it once per file load by probing.
    private int _trackBase = -1;

    private void ResetTrackBase() => _trackBase = -1;

    /// <summary>
    /// Reads the current file's tracks straight from mpv's `track-list` node.
    /// </summary>
    private List<TrackInfo> ReadTracks(string wantedType)
    {
        var result = new List<TrackInfo>();
        if (_mpv is null)
            return result;

        if (_trackBase < 0)
        {
            _trackBase = ProbeTrackField(0, "type") ? 0 : 1;
            if (!ProbeTrackField(_trackBase, "type"))
                return result; // no tracks at all
        }

        var count = ParseTrackInt("track-list/count");
        var limit = count > 0 ? _trackBase + count : _trackBase + 1024;

        for (var i = _trackBase; i < limit; i++)
        {
            string? type = ProbeTrackField(i, "type") ? SafeTrackString($"track-list/{i}/type") : null;
            if (string.IsNullOrEmpty(type))
            {
                if (count <= 0)
                    break; // probing mode: first gap ends the list
                continue;
            }
            if (!string.Equals(type, wantedType, StringComparison.OrdinalIgnoreCase))
                continue;

            var id = ParseTrackInt($"track-list/{i}/id");
            var title = SafeTrackString($"track-list/{i}/title");
            var lang = SafeTrackString($"track-list/{i}/lang");
            var codec = SafeTrackString($"track-list/{i}/codec");
            var selected = ParseTrackFlag($"track-list/{i}/selected");
            var external = ParseTrackFlag($"track-list/{i}/external");
            var @default = ParseTrackFlag($"track-list/{i}/default");
            var forced = ParseTrackFlag($"track-list/{i}/forced");

            var label = $"[{id}]";
            if (!string.IsNullOrEmpty(title))
                label += " " + title;
            if (!string.IsNullOrEmpty(lang))
                label += $" ({lang})";
            if (!string.IsNullOrEmpty(codec) && string.IsNullOrEmpty(title))
                label += $" [{codec}]";
            var flags = new List<string>();
            if (external)
                flags.Add("external");
            if (@default)
                flags.Add("default");
            if (forced)
                flags.Add("forced");
            if (flags.Count > 0)
                label += " — " + string.Join(", ", flags);

            result.Add(new TrackInfo(i, id, type, label, selected));
        }
        return result;
    }

    private bool ProbeTrackField(int index, string field)
    {
        try
        {
            return !string.IsNullOrEmpty(_mpv?.GetPropertyString($"track-list/{index}/{field}"));
        }
        catch
        {
            return false;
        }
    }

    private int ParseTrackInt(string path)
    {
        try
        {
            var raw = _mpv?.GetPropertyString(path);
            return int.TryParse(raw, out var v) ? v : -1;
        }
        catch
        {
            return -1;
        }
    }

    private string SafeTrackString(string path)
    {
        try
        {
            return _mpv?.GetPropertyString(path) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool ParseTrackFlag(string path)
    {
        try
        {
            var raw = (_mpv?.GetPropertyString(path) ?? string.Empty).Trim().ToLowerInvariant();
            return raw is "yes" or "true" or "1";
        }
        catch
        {
            return false;
        }
    }

    // Rebuild the Audio/Subtitle submenus whenever they are about to open —
    // covers both the menu bar and (indirectly) the context menu.
    private void TrackMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (_mpv is null)
            return;
        RebuildTrackSubmenu(AudioTrackMenu, "audio", SelectAudioTrack);
        RebuildTrackSubmenu(SubtitleTrackMenu, "sub", SelectSubtitleTrack);
    }

    private void RebuildTrackSubmenu(
        System.Windows.Controls.MenuItem host, string type, Action<int> select)
    {
        host.Items.Clear();
        var tracks = ReadTracks(type);
        if (tracks.Count == 0)
        {
            host.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = "(no tracks)",
                IsEnabled = false
            });
            return;
        }
        foreach (var t in tracks)
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = t.Label,
                IsCheckable = true,
                IsChecked = t.Selected,
                Tag = t.Id
            };
            item.Click += (_, _) => select(t.Id);
            host.Items.Add(item);
        }
        if (type == "sub")
        {
            var anySelected = tracks.Any(t => t.Selected);
            host.Items.Add(new System.Windows.Controls.Separator());
            var disable = new System.Windows.Controls.MenuItem
            {
                Header = "Disable subtitles",
                IsCheckable = true,
                IsChecked = !anySelected
            };
            disable.Click += (_, _) => select(-1);
            host.Items.Add(disable);
        }
    }

    private void SelectAudioTrack(int id)
    {
        _mpv?.RunCommandString(id >= 0 ? $"set aid {id}" : "set aid no");
    }

    private void SelectSubtitleTrack(int id)
    {
        _mpv?.RunCommandString(id >= 0 ? $"set sid {id}" : "set sid no");
    }

    // ---- right-click context menu (VLC-style, over the video too) ---------

    /// <summary>
    /// Builds + shows the track/delay/speed/fullscreen menu at the cursor.
    /// Called from the mouse hook (works over the native video), not from
    /// WPF events (which never fire there).
    /// </summary>
    private void ShowContextMenu(Point screenPoint)
    {
        if (_menuOpen)
            return;
        if (DateTime.UtcNow.Ticks - (_contextMenuAt ?? 0) < TimeSpan.TicksPerSecond / 4)
            return; // de-dup rapid twin events
        _contextMenuAt = DateTime.UtcNow.Ticks;
        _menuOpen = true;

        try
        {
            var menu = new System.Windows.Controls.ContextMenu
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint,
                HorizontalOffset = screenPoint.X,
                VerticalOffset = screenPoint.Y
            };

            var play = new System.Windows.Controls.MenuItem { Header = "Play / Pause" };
            play.Click += (_, _) => _mpv?.RunCommandString("cycle pause");
            menu.Items.Add(play);

            var stop = new System.Windows.Controls.MenuItem { Header = "Stop" };
            stop.Click += (_, _) => Stop_Click(this, new RoutedEventArgs());
            menu.Items.Add(stop);
            menu.Items.Add(new System.Windows.Controls.Separator());

            var audio = new System.Windows.Controls.MenuItem { Header = "Audio Track" };
            RebuildTrackSubmenu(audio, "audio", SelectAudioTrack);
            menu.Items.Add(audio);

            var sub = new System.Windows.Controls.MenuItem { Header = "Subtitle Track" };
            RebuildTrackSubmenu(sub, "sub", SelectSubtitleTrack);
            menu.Items.Add(sub);
            menu.Items.Add(new System.Windows.Controls.Separator());

            var subDelay = new System.Windows.Controls.MenuItem { Header = "Subtitle Delay" };
            AddClickItem(subDelay, "+0.1s", () => _mpv?.RunCommandString("add sub-delay 0.1"));
            AddClickItem(subDelay, "−0.1s", () => _mpv?.RunCommandString("add sub-delay -0.1"));
            AddClickItem(subDelay, "Reset", () => _mpv?.RunCommandString("set sub-delay 0"));
            menu.Items.Add(subDelay);

            var audioDelay = new System.Windows.Controls.MenuItem { Header = "Audio Delay" };
            AddClickItem(audioDelay, "+0.1s", () => _mpv?.RunCommandString("add audio-delay 0.1"));
            AddClickItem(audioDelay, "−0.1s", () => _mpv?.RunCommandString("add audio-delay -0.1"));
            menu.Items.Add(audioDelay);

            var speed = new System.Windows.Controls.MenuItem { Header = "Speed" };
            foreach (var (label, value) in SpeedPresets())
                AddClickItem(speed, label, () => _mpv?.RunCommandString($"set speed {value}"));
            menu.Items.Add(speed);
            menu.Items.Add(new System.Windows.Controls.Separator());

            var fs = new System.Windows.Controls.MenuItem { Header = "Fullscreen" };
            fs.Click += (_, _) => ToggleFullscreen();
            menu.Items.Add(fs);

            menu.Closed += (_, _) => _menuOpen = false;
            menu.IsOpen = true;
        }
        catch
        {
            _menuOpen = false;
        }
    }

    private static IEnumerable<(string Label, string Value)> SpeedPresets()
    {
        yield return ("0.5×", "0.5");
        yield return ("0.75×", "0.75");
        yield return ("Normal (reset)", "1");
        yield return ("1.25×", "1.25");
        yield return ("1.5×", "1.5");
        yield return ("2×", "2");
    }

    private static void AddClickItem(System.Windows.Controls.MenuItem host, string header, Action action)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += (_, _) => action();
        host.Items.Add(item);
    }

    // ---- fullscreen (VLC-style: borderless, chrome hidden) -----------------

    private void ToggleFullscreen()
    {
        if (_fullscreen)
            ExitFullscreen();
        else
            EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        if (_fullscreen)
            return;
        _fullscreen = true;
        _savedGeom = new WindowGeom(Left, Top, Width, Height, WindowState == WindowState.Maximized);
        MainMenu.Visibility = Visibility.Collapsed;
        Toolbar.Visibility = Visibility.Collapsed;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        WindowState = WindowState.Maximized;
    }

    private void ExitFullscreen(bool silent = false)
    {
        if (!_fullscreen)
            return;
        _fullscreen = false;
        Topmost = false;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        MainMenu.Visibility = Visibility.Visible;
        Toolbar.Visibility = Visibility.Visible;
        if (!silent && _savedGeom is { } g)
        {
            WindowState = WindowState.Normal;
            Left = g.Left;
            Top = g.Top;
            Width = g.Width;
            Height = g.Height;
            if (g.Maximized)
                WindowState = WindowState.Maximized;
        }
        _savedGeom = null;
    }

    // ---- menus: shared VLC-style actions ----------------------------------

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        _mpv?.RunCommandString("playlist-prev force");
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _mpv?.RunCommandString("playlist-next force");
    }

    private void SpeedItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string speed)
            _mpv?.RunCommandString($"set speed {speed}");
    }

    private void AudioDelayPlus_Click(object sender, RoutedEventArgs e)
        => _mpv?.RunCommandString("add audio-delay 0.1");

    private void AudioDelayMinus_Click(object sender, RoutedEventArgs e)
        => _mpv?.RunCommandString("add audio-delay -0.1");

    private void SubDelayPlus_Click(object sender, RoutedEventArgs e)
        => _mpv?.RunCommandString("add sub-delay 0.1");

    private void SubDelayMinus_Click(object sender, RoutedEventArgs e)
        => _mpv?.RunCommandString("add sub-delay -0.1");

    private void SubDelayReset_Click(object sender, RoutedEventArgs e)
        => _mpv?.RunCommandString("set sub-delay 0");

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0";
        MessageBox.Show(
            $"MyPlayer {version}\n\nA personal mpv-powered video player with a VLC-style interface.\n\nVideo engine: libmpv (mpv {$"v{GetMpvVersion()}"})\nConfig: %APPDATA%\\MyPlayer (plain mpv.conf + input.conf)",
            "About MyPlayer", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string GetMpvVersion()
    {
        try
        {
            return _mpv?.GetPropertyString("mpv-version") ?? "?";
        }
        catch
        {
            return "?";
        }
    }

    private sealed record WindowGeom(double Left, double Top, double Width, double Height, bool Maximized);

    private string GeomPath => Path.Combine(ConfigManager.ConfigDir, "window.json");

    private void SaveWindowGeometry()
    {
        try
        {
            Directory.CreateDirectory(ConfigManager.ConfigDir);
            var maximized = WindowState == WindowState.Maximized;
            var r = maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            var geom = new WindowGeom(r.Left, r.Top, r.Width, r.Height, maximized);
            File.WriteAllText(GeomPath, JsonSerializer.Serialize(geom));
        }
        catch { /* geometry is best-effort */ }
    }

    private void RestoreWindowGeometry()
    {
        try
        {
            if (!File.Exists(GeomPath))
                return;
            var g = JsonSerializer.Deserialize<WindowGeom>(File.ReadAllText(GeomPath));
            if (g is null || g.Width < 200 || g.Height < 150)
                return;

            // Ignore if it would open mostly off-screen (monitor layout changed)
            var vsLeft = SystemParameters.VirtualScreenLeft;
            var vsTop = SystemParameters.VirtualScreenTop;
            var vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
            var vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
            if (g.Left + g.Width < vsLeft + 50 || g.Left > vsRight - 50 ||
                g.Top + g.Height < vsTop + 50 || g.Top > vsBottom - 50)
                return;

            Left = g.Left;
            Top = g.Top;
            Width = g.Width;
            Height = g.Height;
            if (g.Maximized)
                WindowState = WindowState.Maximized;
        }
        catch { /* geometry is best-effort */ }
    }
}