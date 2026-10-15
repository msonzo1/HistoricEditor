using System.Windows;

namespace HistoricEditor;

// Fonti della scheda (URL generale e fonti delle date), modificate a parte rispetto ai dati principali.
public partial class SourcesWindow : Window
{
    public SourcesWindow(string sourceUrl, string birthSources, string deathSources)
    {
        InitializeComponent();
        SourceUrlInput.Text = sourceUrl;
        BirthSourcesInput.Text = birthSources;
        DeathSourcesInput.Text = deathSources;
        Loaded += (_, _) => SourceUrlInput.Focus();
    }

    public string SourceUrl => SourceUrlInput.Text.Trim();

    public string BirthSources => BirthSourcesInput.Text.Trim();

    public string DeathSources => DeathSourcesInput.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
