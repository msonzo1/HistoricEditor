using System.Windows;
using System.Windows.Controls;

namespace HistoricEditor;

// Selettore della lingua: mostra nome, suffisso, soprannome e titolo principale importati in un'altra lingua.
// La scheda resta in italiano: i valori delle altre lingue sono di sola consultazione.
public partial class PersonEditorWindow
{
    private sealed record LanguageOption(string Etichetta, LocalizedPersonName? Values)
    {
        // Il modello della ComboBox mostra l'elemento selezionato tramite ToString.
        public override string ToString() => Etichetta;
    }

    private List<LocalizedPersonName> localizedNames = [];
    private (string Name, string Suffix, string Nickname, string MainTitle)? workingValues;
    private bool switchingLanguage;

    private void SetLocalizedNames(IEnumerable<LocalizedPersonName> names)
    {
        localizedNames = names.Select(n => n.Clone()).ToList();
        workingValues = null;

        switchingLanguage = true;
        var options = new List<LanguageOption> { new("Italiano (scheda modificabile)", null) };
        options.AddRange(localizedNames.Select(n => new LanguageOption($"{n.LinguaNome} ({n.Lingua})", n)));
        LanguageSelector.ItemsSource = options;
        LanguageSelector.SelectedIndex = 0;
        switchingLanguage = false;

        LanguagePanel.Visibility = localizedNames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetNameFieldsReadOnly(false);
    }

    private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switchingLanguage || LanguageSelector.SelectedItem is not LanguageOption option)
        {
            return;
        }

        if (option.Values is not { } values)
        {
            RestoreWorkingValues();
            return;
        }

        workingValues ??= (NameInput.Text, SuffixInput.Text, NicknameInput.Text, MainTitleInput.Text);
        NameInput.Text = values.Nome;
        SuffixInput.Text = values.Suffisso;
        NicknameInput.Text = values.Soprannome;
        MainTitleInput.Text = values.TitoloPrincipale;
        SetNameFieldsReadOnly(true);
    }

    // Riporta la scheda ai valori modificabili (usato prima di salvare o di un nuovo import).
    private void ShowWorkingLanguage()
    {
        if (LanguageSelector.SelectedIndex > 0)
        {
            switchingLanguage = true;
            LanguageSelector.SelectedIndex = 0;
            switchingLanguage = false;
        }

        RestoreWorkingValues();
    }

    private void RestoreWorkingValues()
    {
        if (workingValues is { } values)
        {
            NameInput.Text = values.Name;
            SuffixInput.Text = values.Suffix;
            NicknameInput.Text = values.Nickname;
            MainTitleInput.Text = values.MainTitle;
            workingValues = null;
        }

        SetNameFieldsReadOnly(false);
    }

    private void SetNameFieldsReadOnly(bool readOnly)
    {
        NameInput.IsReadOnly = readOnly;
        SuffixInput.IsReadOnly = readOnly;
        NicknameInput.IsReadOnly = readOnly;
        MainTitleInput.IsReadOnly = readOnly;
        LanguageNotice.Visibility = readOnly ? Visibility.Visible : Visibility.Collapsed;
    }
}
