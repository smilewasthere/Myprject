using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MyPlayer;

/// <summary>
/// "Open Network Stream" dialog — paste a URL and play it. mpv's loadfile
/// command handles URLs natively, so this just collects the string and hands
/// it back to MainWindow, which routes it through the same OpenPath() used
/// for local files.
/// </summary>
public partial class OpenStream : Window
{
    /// <summary>The trimmed URL the user chose, or null if cancelled.</summary>
    public string? SelectedUrl { get; private set; }

    public OpenStream(IEnumerable<string>? recentUrls = null)
    {
        InitializeComponent();

        if (recentUrls is not null)
        {
            foreach (var url in recentUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
                UrlBox.Items.Add(url);
        }

        Loaded += (_, _) =>
        {
            UrlBox.Focus();
        };
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this, "Please enter a URL.", "Open Network Stream",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedUrl = url;
        DialogResult = true;
        Close();
    }
}
