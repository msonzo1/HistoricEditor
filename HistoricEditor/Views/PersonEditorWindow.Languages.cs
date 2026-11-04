using System.Windows;
using System.Windows.Controls;

namespace HistoricEditor;

// Selettore della lingua: nome, suffisso, soprannome e titolo principale in un'altra lingua.
// I campi della scheda mostrano e modificano i valori della lingua scelta; l'italiano resta la lingua della scheda.
public partial class PersonEditorWindow
{
    private sealed record LanguageOption(string Etichetta, LocalizedPersonName? Values)
    {
        // Il modello della ComboBox mostra l'elemento selezionato tramite ToString.
        public override string ToString() => Etichetta;
    }

    private sealed record AddableLanguage(string Code, string Name)
    {
        public string Etichetta => $"{Name} ({Code})";

        public override string ToString() => Etichetta;
    }

    private List<LocalizedPersonName> localizedNames = [];
    private (string Name, string Suffix, string Nickname, string MainTitle)? workingValues;
    private LocalizedPersonName? shownLanguage;
    private bool switchingLanguage;

    private void SetLocalizedNames(IEnumerable<LocalizedPersonName> names)
    {
        localizedNames = names.Select(n => n.Clone()).ToList();
        workingValues = null;
        shownLanguage = null;
        RebuildLanguageOptions(null);
    }

    private void RebuildLanguageOptions(LocalizedPersonName? select)
    {
        switchingLanguage = true;
        var options = new List<LanguageOption> { new("Italiano (lingua della scheda)", null) };
        options.AddRange(localizedNames.Select(n => new LanguageOption($"{n.LinguaNome} ({n.Lingua})", n)));
        LanguageSelector.ItemsSource = options;
        LanguageSelector.SelectedItem = options.FirstOrDefault(o => ReferenceEquals(o.Values, select)) ?? options[0];
        switchingLanguage = false;

        AddLanguageSelector.ItemsSource = LocalizedPersonName.MissingLanguages(localizedNames)
            .Select(l => new AddableLanguage(l.Code, l.Name))
            .ToList();
        AddLanguageSelector.SelectedIndex = -1;
        AddLanguagePanel.Visibility = AddLanguageSelector.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateLanguageNotice();
    }

    private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switchingLanguage || LanguageSelector.SelectedItem is not LanguageOption option)
        {
            return;
        }

        ShowLanguage(option.Values);
    }

    // Salva nella lingua mostrata i valori dei campi, poi mostra la lingua richiesta (null = italiano).
    private void ShowLanguage(LocalizedPersonName? values)
    {
        CommitShownLanguage();
        if (values is null)
        {
            RestoreWorkingValues();
            return;
        }

        workingValues ??= (NameInput.Text, SuffixInput.Text, NicknameInput.Text, MainTitleInput.Text);
        shownLanguage = values;
        NameInput.Text = values.Nome;
        SuffixInput.Text = values.Suffisso;
        NicknameInput.Text = values.Soprannome;
        MainTitleInput.Text = values.TitoloPrincipale;
        UpdateLanguageNotice();
    }

    private void CommitShownLanguage()
    {
        if (shownLanguage is { } values)
        {
            values.Nome = NameInput.Text.Trim();
            values.Suffisso = SuffixInput.Text.Trim();
            values.Soprannome = NicknameInput.Text.Trim();
            values.TitoloPrincipale = MainTitleInput.Text.Trim();
        }
    }

    // Riporta la scheda ai valori in italiano (usato prima di salvare o di un nuovo import).
    private void ShowWorkingLanguage()
    {
        CommitShownLanguage();
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

        shownLanguage = null;
        UpdateLanguageNotice();
    }

    private void UpdateLanguageNotice()
    {
        var other = shownLanguage is not null;
        LanguageNotice.Text = other ? $"Stai modificando i valori in {shownLanguage!.LinguaNome}." : string.Empty;
        LanguageNotice.Visibility = other ? Visibility.Visible : Visibility.Collapsed;
        RemoveLanguageButton.Visibility = other ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddLanguage_Click(object sender, RoutedEventArgs e)
    {
        if (AddLanguageSelector.SelectedItem is not AddableLanguage language)
        {
            Feedback.Info(EditorMessage, "Scegli prima la lingua da aggiungere.");
            AddLanguageSelector.Focus();
            return;
        }

        CommitShownLanguage();
        var entry = new LocalizedPersonName { Lingua = language.Code, LinguaNome = language.Name };
        localizedNames.Add(entry);
        RebuildLanguageOptions(null);
        switchingLanguage = true;
        LanguageSelector.SelectedItem = LanguageSelector.Items.OfType<LanguageOption>().First(o => ReferenceEquals(o.Values, entry));
        switchingLanguage = false;
        ShowLanguage(entry);
        NameInput.Focus();
    }

    private void RemoveLanguage_Click(object sender, RoutedEventArgs e)
    {
        if (shownLanguage is not { } values)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Rimuovere i valori in {values.LinguaNome}?",
            "Rimuovi lingua",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        shownLanguage = null;
        localizedNames.Remove(values);
        RestoreWorkingValues();
        RebuildLanguageOptions(null);
    }

    private string LocalizedNamesSnapshot()
    {
        CommitShownLanguage();
        return string.Join("\u001e", LocalizedPersonName.Normalize(localizedNames).Select(n =>
            $"{n.Lingua}|{n.Nome}|{n.Suffisso}|{n.Soprannome}|{n.TitoloPrincipale}"));
    }
}
