using System.Windows;

namespace HistoricEditor;

// Fonti della scheda (URL generale e fonti delle date), modificate a parte rispetto ai dati principali.
public partial class SourcesWindow : Window
{
    public SourcesWindow(string sourceUrl, string birthSources, string deathSources, string wikiTreeId = "")
    {
        InitializeComponent();
        SourceUrlInput.Text = sourceUrl;
        BirthSourcesInput.Text = birthSources;
        DeathSourcesInput.Text = deathSources;
        WikiTreeInput.Text = wikiTreeId;
        Loaded += (_, _) => SourceUrlInput.Focus();
    }

    public string SourceUrl => SourceUrlInput.Text.Trim();

    public string BirthSources => BirthSourcesInput.Text.Trim();

    public string DeathSources => DeathSourcesInput.Text.Trim();

    public string WikiTreeId => WikiTreeLinks.Normalize(WikiTreeInput.Text);

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (WikiTreeInput.Text.Trim().Length > 0 && !WikiTreeLinks.IsValid(WikiTreeId))
        {
            WikiTreeError.Text = "Profilo WikiTree non valido: indica un identificativo come Habsburg-105 o l'indirizzo del profilo.";
            WikiTreeError.Visibility = Visibility.Visible;
            WikiTreeInput.Focus();
            return;
        }

        DialogResult = true;
    }
}
