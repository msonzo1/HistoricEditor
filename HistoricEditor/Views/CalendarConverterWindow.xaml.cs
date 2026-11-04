using System.Windows;

namespace HistoricEditor;

public partial class CalendarConverterWindow : Window
{
    public CalendarConverterWindow(string? date = null, string? calendar = null, string? style = null)
    {
        InitializeComponent();
        CalendarBox.ItemsSource = HistoricalCalendar.Calendari.Where(c => c.Codice.Length > 0).ToList();
        StyleBox.ItemsSource = HistoricalCalendar.Stili;
        CountryBox.ItemsSource = HistoricalCalendar.Adozioni;
        CalendarBox.SelectedItem = HistoricalCalendar.CalendarInfo(calendar == HistoricalCalendar.Gregoriano ? calendar : HistoricalCalendar.Giuliano);
        StyleBox.SelectedItem = HistoricalCalendar.StyleInfo(style);
        CountryBox.SelectedIndex = 0;
        DateBox.Text = (date ?? string.Empty).Split(';')[0].Trim();
        UpdateResults();
        Loaded += (_, _) =>
        {
            DateBox.Focus();
            DateBox.SelectAll();
        };
    }

    private void Input_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateResults();
        }
    }

    internal void UpdateResults()
    {
        var calendar = (CalendarBox.SelectedItem as CalendarOption)?.Codice ?? HistoricalCalendar.Giuliano;
        var style = (StyleBox.SelectedItem as YearStyleOption)?.Codice ?? HistoricalCalendar.StileComune;
        StyleDescription.Text = HistoricalCalendar.StyleInfo(style).Descrizione;
        ResultGrid.ItemsSource = null;
        CopyButton.IsEnabled = false;

        if (string.IsNullOrWhiteSpace(DateBox.Text))
        {
            MessageText.Text = "Scrivi una data, ad esempio «10 febbraio 1525».";
            return;
        }

        if (!HistoricalCalendar.TryParse(DateBox.Text, out var written))
        {
            MessageText.Text = "Data non riconosciuta: scrivi giorno, mese e anno, ad esempio «10 febbraio 1525» o «10/02/1525».";
            return;
        }

        if (!HistoricalCalendar.IsValid(written, calendar))
        {
            MessageText.Text = $"Questa data non esiste nel calendario {calendar}.";
            return;
        }

        ResultGrid.ItemsSource = HistoricalCalendar.ConvertAll(written, calendar, style);
        CopyButton.IsEnabled = true;
        MessageText.Text = string.Join(" ", Notes(written, calendar, style));
    }

    private IEnumerable<string> Notes(CalendarDate written, string calendar, string style)
    {
        if (!written.IsComplete)
        {
            yield return "Senza giorno e mese si può convertire solo l’anno: tra giuliano e gregoriano la data cambia di 10-13 giorni.";
        }

        if (HistoricalCalendar.StyleShift(written, style) is null)
        {
            yield return "Con questo stile, senza giorno e mese non si può stabilire l’anno moderno.";
        }

        var modern = HistoricalCalendar.ToModern(written, style);
        var gregorian = HistoricalCalendar.Convert(modern, calendar, HistoricalCalendar.Gregoriano);
        if (calendar == HistoricalCalendar.Gregoriano && written.IsComplete
            && HistoricalCalendar.ToDayNumber(modern, HistoricalCalendar.Gregoriano) < HistoricalCalendar.ToDayNumber(HistoricalCalendar.GregorianReform, HistoricalCalendar.Gregoriano))
        {
            yield return "La data è anteriore alla riforma del 15 ottobre 1582: nel gregoriano è una data prolettica.";
        }

        if (CountryBox.SelectedItem is GregorianAdoption adoption && (written.IsComplete || written.Year != adoption.Anno))
        {
            var inUse = HistoricalCalendar.UsesJulian(adoption, gregorian) ? HistoricalCalendar.Giuliano : HistoricalCalendar.Gregoriano;
            yield return $"{adoption.Paese}: il calendario gregoriano entrò in vigore il {HistoricalCalendar.Format(adoption.PrimoGiorno)}, quindi in questa data era in uso il {inUse}. {adoption.Note}";
            if (inUse != calendar)
            {
                yield return $"Attenzione: la data è indicata nel calendario {calendar}, ma lì era in uso il {inUse}.";
            }
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (ResultGrid.ItemsSource is IEnumerable<CalendarConversion> rows)
        {
            var text = string.Join(Environment.NewLine, rows.Select(r => r.Etichetta).Prepend($"Data: {DateBox.Text.Trim()}"));
            try
            {
                Clipboard.SetText(text);
                MessageText.Text = "Risultati copiati negli appunti.";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageText.Text = "Impossibile copiare negli appunti: riprova.";
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
