using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HanumanInstitute.LibMpv;

namespace MyPlayer;

/// <summary>
/// Live subtitle styling panel. Everything is applied to the running mpv
/// instance immediately via "set &lt;option&gt; &lt;value&gt;" — the same
/// mechanism mpv uses for mpv.conf, so what you set here is exactly what a
/// mpv.conf line would do. Colors use mpv's #AARRGGBB format.
/// </summary>
public partial class SubtitleSettings : Window
{
    // (name, #AARRGGBB) swatches offered per color field.
    private static readonly (string Label, string Argb)[] Palette =
    {
        ("White", "#FFFFFFFF"), ("Yellow", "#FFFFFF00"), ("Cyan", "#FF00FFFF"),
        ("Green", "#FF00FF00"), ("Lime", "#FF80FF80"), ("Black", "#FF000000"),
        ("Red", "#FFFF0000"), ("Magenta", "#FFFF00FF"), ("Blue", "#FF0000FF"),
        ("Orange", "#FFFFA500"), ("Gray", "#FF808080"), ("Pink", "#FFFF69B4"),
    };

    // Sliders + their numeric readouts, built in code (see BuildSliders).
    private Slider SizeSlider = null!, ScaleSlider = null!, PosSlider = null!,
        MarginYSlider = null!, BorderSizeSlider = null!, ShadowOffsetSlider = null!,
        DelaySlider = null!;
    private TextBlock SizeLabel = null!, ScaleLabel = null!, PosLabel = null!,
        MarginYLabel = null!, BorderSizeLabel = null!, ShadowOffsetLabel = null!,
        DelayLabel = null!;

    // Current color values as #AARRGGBB, so Reset can restore mpv defaults.
    private string _text = "#FFFFFFFF";
    private string _border = "#FF000000";
    private string _shadow = "#80000000";
    private string _back = "#80000000";

    private bool _suppress;
    private readonly MpvContext _mpv;

    public SubtitleSettings(MpvContext mpv)
    {
        InitializeComponent();
        _mpv = mpv;

        BuildSwatches();
        BuildSliders();
        WireEvents();
        LoadFromMpv();
    }

    private void BuildSwatches()
    {
        AddSwatches(TextColorSwatches, v => { _text = v; TextColorPreview.Background = Brush(v); Apply("sub-color", v); });
        AddSwatches(BorderColorSwatches, v => { _border = v; BorderColorPreview.Background = Brush(v); Apply("sub-border-color", v); });
        AddSwatches(ShadowColorSwatches, v => { _shadow = v; ShadowColorPreview.Background = Brush(v); Apply("sub-shadow-color", v); });
        AddSwatches(BackColorSwatches, v => { _back = v; BackColorPreview.Background = Brush(v); Apply("sub-back-color", v); });
    }

    private void AddSwatches(WrapPanel host, Action<string> onPick)
    {
        foreach (var (label, argb) in Palette)
        {
            var swatch = new System.Windows.Controls.Border
            {
                Width = 20,
                Height = 18,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(2),
                Background = Brush(argb),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                BorderThickness = new Thickness(1),
                ToolTip = label,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            swatch.MouseLeftButtonUp += (_, _) => onPick(argb);
            host.Children.Add(swatch);
        }
    }

    private void BuildSliders()
    {
        (SizeSlider, SizeLabel) = AddRow(SizeSection, "Size:", 1, 200, 1);
        (BorderSizeSlider, BorderSizeLabel) = AddRow(ScaleSection, "Outline size:", 0, 8, 0.5);
        (ShadowOffsetSlider, ShadowOffsetLabel) = AddRow(ScaleSection, "Shadow offset:", 0, 10, 0.5);
        (ScaleSlider, ScaleLabel) = AddRow(ScaleSection, "Scale:", 0.1, 5, 0.05);
        (PosSlider, PosLabel) = AddRow(PositionSection, "Vertical pos:", 0, 150, 1);
        (MarginYSlider, MarginYLabel) = AddRow(PositionSection, "Bottom margin:", 0, 200, 1);
        (DelaySlider, DelayLabel) = AddRow(TimingSection, "Subtitle delay:", -10, 10, 0.1);
    }

    private (Slider, TextBlock) AddRow(StackPanel host, string label,
        double min, double max, double tick)
    {
        var lbl = new Label
        {
            Content = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            Padding = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Margin = new Thickness(0, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            IsSnapToTickEnabled = true,
            TickFrequency = tick,
        };
        var val = new TextBlock
        {
            Width = 44,
            TextAlignment = TextAlignment.Right,
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(val, 2);
        grid.Children.Add(lbl);
        grid.Children.Add(slider);
        grid.Children.Add(val);
        host.Children.Add(grid);

        return (slider, val);
    }

    private void WireEvents()
    {
        FontCombo.LostFocus += (_, _) => { if (!_suppress) Apply("sub-font", FontCombo.Text ?? "sans-serif"); };
        FontCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress) return;
            if (FontCombo.SelectedItem is ComboBoxItem ci && ci.Content is string s)
                Apply("sub-font", s);
        };
        BoldCheck.Checked += (_, _) => { if (!_suppress) Apply("sub-bold", "yes"); };
        BoldCheck.Unchecked += (_, _) => { if (!_suppress) Apply("sub-bold", "no"); };
        ItalicCheck.Checked += (_, _) => { if (!_suppress) Apply("sub-italic", "yes"); };
        ItalicCheck.Unchecked += (_, _) => { if (!_suppress) Apply("sub-italic", "no"); };

        SizeSlider.ValueChanged += (_, e) => { SizeLabel.Text = F0(e.NewValue); if (!_suppress) Apply("sub-font-size", e.NewValue); };
        ScaleSlider.ValueChanged += (_, e) => { ScaleLabel.Text = F1(e.NewValue); if (!_suppress) Apply("sub-scale", e.NewValue); };
        PosSlider.ValueChanged += (_, e) => { PosLabel.Text = F0(e.NewValue); if (!_suppress) Apply("sub-pos", e.NewValue); };
        MarginYSlider.ValueChanged += (_, e) => { MarginYLabel.Text = F0(e.NewValue); if (!_suppress) Apply("sub-margin-y", e.NewValue); };
        BorderSizeSlider.ValueChanged += (_, e) => { BorderSizeLabel.Text = F1(e.NewValue); if (!_suppress) Apply("sub-border-size", e.NewValue); };
        ShadowOffsetSlider.ValueChanged += (_, e) => { ShadowOffsetLabel.Text = F1(e.NewValue); if (!_suppress) Apply("sub-shadow-offset", e.NewValue); };
        DelaySlider.ValueChanged += (_, e) => { DelayLabel.Text = F1(e.NewValue) + " s"; if (!_suppress) Apply("sub-delay", e.NewValue); };

        AssOverrideCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress) return;
            if (AssOverrideCombo.SelectedItem is ComboBoxItem ci && ci.Tag is string tag)
                Apply("sub-ass-override", tag);
        };
    }

    private void Apply(string option, double value)
        => _mpv.RunCommandString($"set {option} {value.ToString(CultureInfo.InvariantCulture)}");

    private void Apply(string option, string value)
        => _mpv.RunCommandString($"set {option} {value}");

    private static Brush Brush(string argb)
    {
        var c = (Color)ColorConverter.ConvertFromString(argb)!;
        return new SolidColorBrush(c);
    }

    private void LoadFromMpv()
    {
        _suppress = true;
        try
        {
            FontCombo.Text = Get("sub-font", "sans-serif");
            BoldCheck.IsChecked = Get("sub-bold", "no") == "yes";
            ItalicCheck.IsChecked = Get("sub-italic", "no") == "yes";

            SizeSlider.Value = GetD("sub-font-size", 55);
            ScaleSlider.Value = GetD("sub-scale", 1);
            PosSlider.Value = GetD("sub-pos", 100);
            MarginYSlider.Value = GetD("sub-margin-y", 22);
            BorderSizeSlider.Value = GetD("sub-border-size", 3);
            ShadowOffsetSlider.Value = GetD("sub-shadow-offset", 0);
            DelaySlider.Value = GetD("sub-delay", 0);

            _text = GetColor("sub-color", "#FFFFFFFF");
            _border = GetColor("sub-border-color", "#FF000000");
            _shadow = GetColor("sub-shadow-color", "#80000000");
            _back = GetColor("sub-back-color", "#80000000");
            TextColorPreview.Background = Brush(_text);
            BorderColorPreview.Background = Brush(_border);
            ShadowColorPreview.Background = Brush(_shadow);
            BackColorPreview.Background = Brush(_back);

            var ov = Get("sub-ass-override", "yes");
            foreach (ComboBoxItem ci in AssOverrideCombo.Items)
                if (ci.Tag is string t && t == ov)
                    AssOverrideCombo.SelectedItem = ci;
            if (AssOverrideCombo.SelectedItem is null)
                AssOverrideCombo.SelectedIndex = 0;

            RefreshLabels();
        }
        finally
        {
            _suppress = false;
        }
    }

    private void RefreshLabels()
    {
        SizeLabel.Text = F0(SizeSlider.Value);
        ScaleLabel.Text = F1(ScaleSlider.Value);
        PosLabel.Text = F0(PosSlider.Value);
        MarginYLabel.Text = F0(MarginYSlider.Value);
        BorderSizeLabel.Text = F1(BorderSizeSlider.Value);
        ShadowOffsetLabel.Text = F1(ShadowOffsetSlider.Value);
        DelayLabel.Text = F1(DelaySlider.Value) + " s";
    }

    private string Get(string option, string fallback)
    {
        try { return _mpv.GetPropertyString(option) ?? fallback; }
        catch { return fallback; }
    }

    private double GetD(string option, double fallback)
    {
        try { return _mpv.GetProperty<double>(option); }
        catch { return fallback; }
    }

    private string GetColor(string option, string fallback)
    {
        var v = Get(option, fallback);
        return v.StartsWith('#') && v.Length == 9 ? v.ToUpperInvariant() : fallback;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        Apply("sub-font", "sans-serif");
        Apply("sub-bold", "no");
        Apply("sub-italic", "no");
        Apply("sub-font-size", 55);
        Apply("sub-scale", 1);
        Apply("sub-pos", 100);
        Apply("sub-margin-y", 22);
        Apply("sub-color", "#FFFFFFFF");
        Apply("sub-border-color", "#FF000000");
        Apply("sub-border-size", 3);
        Apply("sub-shadow-color", "#80000000");
        Apply("sub-shadow-offset", 0);
        Apply("sub-back-color", "#80000000");
        Apply("sub-ass-override", "yes");
        // sub-delay intentionally left alone — it is a per-file sync value.
        LoadFromMpv();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string F0(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
    private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
}


