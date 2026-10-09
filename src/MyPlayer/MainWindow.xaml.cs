using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using HanumanInstitute.LibMpv;
using Microsoft.Win32;

namespace MyPlayer;

public partial class MainWindow : Window
{
    private MpvContext? _mpv;
    private DispatcherTimer? _timer;
    private string? _lastPath;
    private double? _duration;
    private bool _stopped;
    private bool _seeking;
    private bool _updatingUi;

    public MainWindow()
    {
        InitializeComponent();
        // WPF sliders have no DragStarted/DragCompleted — use mouse events instead
        SeekSlider.PreviewMouseLeftButtonDown += (_, _) => _seeking = true;
    }

    // ---- lifecycle -------------------------------------------------------

    private void Window_SourceInitialized(object? sender, EventArgs e) => RestoreWindowGeometry();

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
        SaveWindowGeometry();
        _timer?.Stop();
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
        _mpv.LoadFile(path, append: false, appendPlay: false, extraArgs: null!).Invoke();
        PlayButton.IsEnabled = true;
        StopButton.IsEnabled = true;
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

    // ---- keyboard (VLC-style; when the video area has focus, input.conf
    //      handles the same keys — this covers when the WPF side has focus) --

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_mpv is null)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var focused = Keyboard.FocusedElement;

        // Let focused controls keep their own keys
        if (focused is System.Windows.Controls.Primitives.ButtonBase && key is Key.Space or Key.Enter)
            return;
        if (focused is System.Windows.Controls.Slider && key is Key.Left or Key.Right or Key.Up or Key.Down)
            return;

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (key)
        {
            case Key.O when ctrl:
                OpenDialog();
                break;
            case Key.Space:
                TogglePlay();
                break;
            case Key.Left:
                _mpv.RunCommandString("seek -5 relative");
                break;
            case Key.Right:
                _mpv.RunCommandString("seek 5 relative");
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
                _mpv.RunCommandString("cycle fullscreen");
                break;
            case Key.Escape:
                _mpv.RunCommandString("set fullscreen no");
                break;
            default:
                return;
        }
        e.Handled = true;
    }

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

    // ---- window geometry memory ------------------------------------------

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