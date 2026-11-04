using System.Windows;
using System.Windows.Controls;

namespace HistoricEditor;

// Calendario (giuliano/gregoriano) e stile di inizio dell'anno delle date di nascita e di morte.
public partial class PersonEditorWindow
{
    private void InitializeCalendarInputs()
    {
        foreach (var box in new[] { BirthCalendarInput, DeathCalendarInput })
        {
            box.ItemsSource = HistoricalCalendar.Calendari;
            box.SelectedIndex = 0;
        }

        foreach (var box in new[] { BirthStyleInput, DeathStyleInput })
        {
            box.ItemsSource = HistoricalCalendar.Stili;
            box.SelectedIndex = 0;
        }
    }

    private static string CalendarOf(ComboBox box) => (box.SelectedItem as CalendarOption)?.Codice ?? HistoricalCalendar.NonIndicato;

    private static string StyleOf(ComboBox box) => (box.SelectedItem as YearStyleOption)?.Codice ?? HistoricalCalendar.StileComune;

    private void SetDateCalendars(string? birthCalendar, string? birthStyle, string? deathCalendar, string? deathStyle)
    {
        BirthCalendarInput.SelectedItem = HistoricalCalendar.CalendarInfo(birthCalendar);
        BirthStyleInput.SelectedItem = HistoricalCalendar.StyleInfo(birthStyle);
        DeathCalendarInput.SelectedItem = HistoricalCalendar.CalendarInfo(deathCalendar);
        DeathStyleInput.SelectedItem = HistoricalCalendar.StyleInfo(deathStyle);
        UpdateCalendarNotes();
    }

    // Le date Wikidata al giorno o al mese registrate nel calendario giuliano fanno proporre quel calendario.
    internal static string ImportedCalendar(IEnumerable<HistoricalDateEvidence> evidence)
        => ImportedPersonCreator.ImportedCalendar(evidence);

    private void FillDateCalendars(HistoricalPerson person)
    {
        person.BirthCalendario = CalendarOf(BirthCalendarInput);
        person.BirthStile = StyleOf(BirthStyleInput);
        person.DeathCalendario = CalendarOf(DeathCalendarInput);
        person.DeathStile = StyleOf(DeathStyleInput);
    }

    private string DateCalendarsSnapshot()
        => $"{CalendarOf(BirthCalendarInput)}|{StyleOf(BirthStyleInput)}|{CalendarOf(DeathCalendarInput)}|{StyleOf(DeathStyleInput)}";

    private void DateCalendar_Changed(object sender, RoutedEventArgs e) => UpdateCalendarNotes();

    private void UpdateCalendarNotes()
    {
        // Gli eventi arrivano anche durante InitializeComponent, quando non tutti i controlli esistono ancora.
        if (BirthCalendarNote is null || DeathCalendarNote is null || BirthStyleInput is null || DeathStyleInput is null)
        {
            return;
        }

        UpdateCalendarNote(BirthInput, BirthCalendarInput, BirthStyleInput, BirthCalendarNote);
        UpdateCalendarNote(DeathInput, DeathCalendarInput, DeathStyleInput, DeathCalendarNote);
    }

    private static void UpdateCalendarNote(TextBox date, ComboBox calendar, ComboBox style, TextBlock note)
    {
        var description = HistoricalCalendar.Describe(date.Text, CalendarOf(calendar), StyleOf(style));
        note.Text = description;
        note.Visibility = description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var styleDescription = HistoricalCalendar.StyleInfo(StyleOf(style)).Descrizione;
        style.ToolTip = styleDescription;
        System.Windows.Automation.AutomationProperties.SetHelpText(style, styleDescription);
        System.Windows.Automation.AutomationProperties.SetHelpText(date, description);
    }
}
