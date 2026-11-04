using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HistoricEditor;

// Elenco delle edizioni di Wikipedia usate per il LED di concordanza, con i collegamenti alle pagine.
public partial class WikipediaConsensusWindow : Window
{
    private readonly IReadOnlyList<ConsensusEdition> editions;

    public WikipediaConsensusWindow(WikipediaConsensusResult result)
    {
        InitializeComponent();
        editions = result.Editions;
        SummaryText.Text = result.Summary;
        var agree = (Brush)FindResource("GreenBrush");
        var differ = (Brush)FindResource("DangerBrush");
        EditionsList.ItemsSource = editions.Select(e => new EditionRow(e, e.Agrees ? agree : differ)).ToList();
        CopyLinksButton.IsEnabled = editions.Any(e => e.Url.Length > 0);
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || PersonEditorWindow.SourceLinkUri(url) is not { } uri)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Feedback.Error(StatusText, "Impossibile aprire il collegamento nel browser.");
        }
    }

    private void CopyLinks_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine, editions.Where(x => x.Url.Length > 0).Select(x => $"{x.LanguageName}: {x.Url}"));
        try
        {
            Clipboard.SetText(text);
            Feedback.Success(StatusText, "Collegamenti copiati negli appunti.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Feedback.Error(StatusText, "Gli appunti non sono disponibili. Riprova.");
        }
    }

    private sealed record EditionRow(ConsensusEdition Edition, Brush MarkBrush)
    {
        public string Mark => Edition.Agrees ? "✓" : "✗";
        public Visibility LinkVisibility => Edition.Url.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string LinkName => $"Apri Wikipedia in {Edition.LanguageName} nel browser";
        public string AccessibleName => $"Wikipedia in {Edition.LanguageName}, {Edition.Title}: {(Edition.Agrees ? "concorda" : "diversa")}. Nascita {Edition.Birth}. Morte {Edition.Death}. {Edition.DifferencesText}";
    }
}
