using System.Windows;
using System.Windows.Controls;

namespace HistoricEditor;

// Calendario (giuliano/gregoriano) delle date di nascita e di morte. Lo stile dell'anno non si sceglie nel modulo:
// le date usano l'anno comune (1° gennaio); le schede salvate in passato con un altro stile lo conservano
// finché la data non passa dal convertitore, che la riporta con l'anno moderno.
public partial class PersonEditorWindow
{
    private string birthStyle = HistoricalCalendar.StileComune;
    private string deathStyle = HistoricalCalendar.StileComune;

    private void InitializeCalendarInputs()
    {
        foreach (var box in new[] { BirthCalendarInput, DeathCalendarInput })
        {
            box.ItemsSource = HistoricalCalendar.Calendari;
            box.SelectedIndex = 0;
        }
    }

    private static string CalendarOf(ComboBox box) => (box.SelectedItem as CalendarOption)?.Codice ?? HistoricalCalendar.NonIndicato;

    private void SetDateCalendars(string? birthCalendar, string? birthStile, string? deathCalendar, string? deathStile)
    {
        birthStyle = birthStile ?? HistoricalCalendar.StileComune;
        deathStyle = deathStile ?? HistoricalCalendar.StileComune;
        BirthCalendarInput.SelectedItem = HistoricalCalendar.CalendarInfo(birthCalendar);
        DeathCalendarInput.SelectedItem = HistoricalCalendar.CalendarInfo(deathCalendar);
        UpdateCalendarNotes();
    }

    // Le date Wikidata al giorno o al mese registrate nel calendario giuliano fanno proporre quel calendario.
    internal static string ImportedCalendar(IEnumerable<HistoricalDateEvidence> evidence)
        => ImportedPersonCreator.ImportedCalendar(evidence);

    private void FillDateCalendars(HistoricalPerson person)
    {
        person.BirthCalendario = CalendarOf(BirthCalendarInput);
        person.BirthStile = birthStyle;
        person.DeathCalendario = CalendarOf(DeathCalendarInput);
        person.DeathStile = deathStyle;
    }

    private string DateCalendarsSnapshot()
        => $"{CalendarOf(BirthCalendarInput)}|{birthStyle}|{CalendarOf(DeathCalendarInput)}|{deathStyle}";

    private void DateCalendar_Changed(object sender, RoutedEventArgs e) => UpdateCalendarNotes();

    private void BirthConverter_Click(object sender, RoutedEventArgs e)
    {
        if (PickConvertedDate(BirthInput, BirthCalendarInput, birthStyle))
        {
            birthStyle = HistoricalCalendar.StileComune;
            UpdateCalendarNotes();
        }
    }

    private void DeathConverter_Click(object sender, RoutedEventArgs e)
    {
        if (PickConvertedDate(DeathInput, DeathCalendarInput, deathStyle))
        {
            deathStyle = HistoricalCalendar.StileComune;
            UpdateCalendarNotes();
        }
    }

    // Apre il convertitore sulla data e, se l'utente la conferma, la riporta con l'anno moderno e il calendario scelto.
    private bool PickConvertedDate(TextBox date, ComboBox calendar, string style)
    {
        var current = CalendarOf(calendar);
        var converter = new CalendarConverterWindow(date.Text, current.Length > 0 ? current : HistoricalCalendar.Giuliano, style, pickDate: true) { Owner = this };
        if (converter.ShowDialog() != true)
        {
            return false;
        }

        date.Text = converter.ResultDate;
        calendar.SelectedItem = HistoricalCalendar.CalendarInfo(converter.ResultCalendar);
        date.Focus();
        return true;
    }

    private void UpdateCalendarNotes()
    {
        // Gli eventi arrivano anche durante InitializeComponent, quando non tutti i controlli esistono ancora.
        if (BirthCalendarNote is null || DeathCalendarNote is null || BirthCalendarInput is null || DeathCalendarInput is null)
        {
            return;
        }

        UpdateCalendarNote(BirthInput, BirthCalendarInput, birthStyle, BirthCalendarNote);
        UpdateCalendarNote(DeathInput, DeathCalendarInput, deathStyle, DeathCalendarNote);
    }

    private static void UpdateCalendarNote(TextBox date, ComboBox calendar, string style, TextBlock note)
    {
        var description = HistoricalCalendar.Describe(date.Text, CalendarOf(calendar), style);
        if (style.Length > 0 && description.Length > 0)
        {
            description = $"Data scritta in {HistoricalCalendar.Suffix(null, style)}. {description}";
        }

        note.Text = description;
        note.Visibility = description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetHelpText(date, description);
    }
}
