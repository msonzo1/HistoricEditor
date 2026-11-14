using System.ComponentModel;
using System.Windows;

namespace HistoricEditor;

// Forme di un nome romano in un'altra lingua: le stesse righe di «Parti del nome», con latino e italiano come riferimento
// e la colonna della lingua da compilare.
public partial class RomanNameLanguageWindow : Window
{
    private sealed class Row(RomanNamePart part, string lingua) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Tipo => part.Tipo;
        public string Latino => part.Latino;
        public string Italiano => part.Italiano;

        public string Testo
        {
            get => field;
            set
            {
                field = value ?? string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Testo)));
            }
        } = part.Translation(lingua);
    }

    private readonly List<Row> rows;

    // Forme scritte, una per riga, nello stesso ordine delle parti ricevute.
    public IReadOnlyList<string> Result { get; private set; } = [];

    public RomanNameLanguageWindow(IReadOnlyList<RomanNamePart> parts, string lingua, string linguaNome, string italianName)
    {
        InitializeComponent();
        Title = $"Nome in {linguaNome} – {italianName}";
        IntroText.Text = $"Forma di ciascuna parte del nome in {linguaNome}. Le righe sono quelle di «Parti del nome»: per aggiungerne o toglierne una, modificarle nel modulo del nome.";
        LanguageColumn.Header = char.ToUpper(linguaNome[0]) + linguaNome[1..];
        rows = [.. parts.Select(p => new Row(p, lingua))];
        foreach (var row in rows)
        {
            row.PropertyChanged += (_, _) => UpdatePreview();
        }

        RowsGrid.ItemsSource = rows;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var name = string.Join(' ', rows.Select(r => r.Testo.Trim()).Where(t => t.Length > 0));
        var missing = rows.Count(r => r.Testo.Trim().Length == 0);
        FullNamePreview.Text = name.Length == 0
            ? "Nessuna parte compilata."
            : missing > 0 ? $"Nome completo: {name}  ·  {Missing(missing, rows.Count)}" : $"Nome completo: {name}";
    }

    // «manca 1 parte su 5», «mancano 2 parti su 5».
    internal static string Missing(int missing, int total)
        => missing == 1 ? $"manca 1 parte su {total}" : $"mancano {missing} parti su {total}";

    private void CopyLatin_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in rows.Where(r => r.Testo.Trim().Length == 0))
        {
            row.Testo = row.Latino;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        RowsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, exitEditingMode: true);
        Result = [.. rows.Select(r => r.Testo.Trim())];
        DialogResult = true;
    }
}
