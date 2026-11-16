using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HistoricEditor;

// Onomastica romana nella scheda: dinastia e gens (modulo «Dinastia e gens») e nomi nel tempo, ciascuno con il suo modulo
// (parti in italiano e in latino, filiazione, tribù, periodo, motivo). Il nome principale dà le parti della persona.
public partial class PersonEditorWindow
{
    private readonly ObservableCollection<RomanNameRecord> romanNames = [];

    // Gens di nascita della persona (ed eventuale ramo): si sceglie solo nel modulo «Dinastia e gens». Quella letta
    // dall'import resta in memoria e si crea al salvataggio (2026-11-15).
    private GensChoice birthGens = GensChoice.None;

    // Gens d'adozione, facoltativa: anche questa solo nel modulo «Dinastia e gens».
    private GensChoice adoptiveGens = GensChoice.None;

    private IReadOnlyList<GensRomana> gentes = [];

    private bool romanForm;

    private void InitializeRomanInputs()
    {
        RomanNamesGrid.ItemsSource = romanNames;
        NamingInput.Items.Add(new ComboBoxItem { Tag = Onomastiche.Automatica });
        NamingInput.Items.Add(new ComboBoxItem { Tag = Onomastiche.Romana, Content = "Romana: praenomen, nomen, cognomen e gens" });
        NamingInput.Items.Add(new ComboBoxItem { Tag = Onomastiche.Standard, Content = "Standard: nome, cognome e famiglia" });
        NamingInput.SelectedIndex = 0;

        // Con l'onomastica automatica la scheda segue le date: si aggiorna mentre si scrivono.
        BirthInput.TextChanged += (_, _) => UpdateNamingMode();
        DeathInput.TextChanged += (_, _) => UpdateNamingMode();
        SurnameInput.TextChanged += (_, _) => UpdateNamingMode();
        SuffixInput.TextChanged += (_, _) => UpdateNamingMode();
        UpdateNamingMode();
        UpdateRomanNameButtons();
    }

    private string SelectedNaming => (NamingInput.SelectedItem as ComboBoxItem)?.Tag as string ?? Onomastiche.Automatica;

    private void SelectNaming(string? onomastica)
        => NamingInput.SelectedItem = NamingInput.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == (onomastica ?? string.Empty))
            ?? NamingInput.Items[0];

    private RomanNameRecord? PrincipalName => romanNames.FirstOrDefault(n => n.Principale);

    private bool FormHasRomanParts => romanNames.Any(n => n.HasParts);

    private bool IsRomanForm => RomanNaming.IsRoman(SelectedNaming, BirthInput.Text, DeathInput.Text, FormHasRomanParts);

    private void NamingInput_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateNamingMode();

    // Mostra il riquadro del nome romano o i campi standard e aggiorna il nome latino nell'intestazione.
    private void UpdateNamingMode()
    {
        if (NamingInput.Items.Count == 0)
        {
            return;
        }

        var era = RomanNaming.IsRomanEra(BirthInput.Text, DeathInput.Text);
        var automatic = (ComboBoxItem)NamingInput.Items[0];
        automatic.Content = FormHasRomanParts || era
            ? "Automatica: romana " + (FormHasRomanParts ? "(nome romano indicato)" : "(per le date)")
            : "Automatica: standard";

        var roman = IsRomanForm;
        var changed = roman != romanForm;
        romanForm = roman;
        RomanPanel.Visibility = roman ? Visibility.Visible : Visibility.Collapsed;

        // Cognome e suffisso restano visibili anche nella scheda romana se contengono già un valore, per non nasconderlo.
        SurnamePanel.Visibility = !roman || SurnameInput.Text.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SuffixPanel.Visibility = !roman || SuffixInput.Text.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Nella scheda romana la dinastia si vede (e si modifica con il suo modulo) nel riquadro del nome romano.
        FamilyPanel.Visibility = roman ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(FamilyPanel, SurnamePanel.Visibility == Visibility.Visible ? 1 : 0);
        SurnameLabel.Content = roman ? "Cognome (scheda standard)" : "Cognome";
        NameLabel.Content = roman ? "Nome italiano *" : "Nome *";
        NameInput.ToolTip = roman ? "Come compare in italiano, ad esempio «Gaio Giulio Cesare»: è il nome mostrato negli elenchi" : null;

        var latin = PrincipalName?.LatinName ?? string.Empty;
        HeaderLatin.Text = roman ? latin : string.Empty;
        HeaderLatin.Visibility = HeaderLatin.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (changed)
        {
            UpdateDynastyPath();
        }
    }

    // Dinastia e gens mostrate nel riquadro del nome romano come percorso («gens Flavia › Flavii Sabini»);
    // sotto, solo se servono, le famiglie non ancora registrate. La gens di nascita mancante si segnala con il colore
    // di avviso; la riga della gens d'adozione compare solo se è indicata.
    private void UpdateRomanFamily()
    {
        // Una gens senza nome latino si segnala sotto, con il colore di avviso (2026-11-14).
        static void Show(TextBlock value, TextBlock note, (string Path, string Note) summary, string missingLatin = "")
        {
            value.Text = summary.Path.Length > 0 ? summary.Path : "Non indicata";
            value.Opacity = summary.Path.Length > 0 ? 1 : 0.6;
            value.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            note.Text = string.Join("\n", new[] { summary.Note, missingLatin }.Where(s => s.Length > 0));
            note.SetResourceReference(TextBlock.ForegroundProperty, missingLatin.Length > 0 ? "DangerBrush" : "QuietBrush");
            note.Visibility = note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Gens e ramo con il latino, e la nota: nuova (da creare al salvataggio) o forme da completare.
        void ShowGens(TextBlock value, TextBlock note, GensChoice choice)
        {
            var (path, text, missing) = GensChoices.Describe(store, gentes, choice);
            Show(value, note, (path, missing ? string.Empty : text), missing ? text : string.Empty);
        }

        Show(RomanDynastyText, RomanDynastyPath, DynastyDescriptions.Summarize(dynasties, FamilyInput.Text, gens: false));
        ShowGens(RomanGensText, RomanGensPath, birthGens);
        if (birthGens.IsEmpty)
        {
            RomanGensText.Opacity = 1;
            RomanGensText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            RomanGensText.Text = "Non indicata: quasi sempre nota, da completare con «Dinastia e gens…»";
        }

        ShowGens(RomanAdoptiveGensText, RomanAdoptiveGensPath, adoptiveGens);
        var adoptive = adoptiveGens.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        RomanAdoptiveGensLabel.Visibility = adoptive;
        RomanAdoptiveGensPanel.Visibility = adoptive;
    }

    private void SetGens(GensChoice gens, GensChoice adoptive)
    {
        birthGens = gens;
        adoptiveGens = adoptive;
        UpdateRomanFamily();
    }

    private void EditDynastyGens_Click(object sender, RoutedEventArgs e)
    {
        var window = new DynastyGensWindow(store, FamilyInput.Text, birthGens, adoptiveGens) { Owner = this };
        if (window.ShowDialog() == true)
        {
            FamilyInput.Text = window.ResultFamily;
            SetGens(window.ResultGens, window.ResultAdoptiveGens);
        }

        // Nel modulo si possono anche modificare o rinominare le dinastie registrate.
        LoadDynastyChoices();
    }

    private void FillRomanName(HistoricalPerson person)
    {
        person.Onomastica = SelectedNaming;
        RomanNaming.SetNames(person, romanNames);
    }

    private void LoadRomanName(HistoricalPerson person)
    {
        SelectNaming(person.Onomastica);
        SetRomanNames(RomanNaming.NamesOf(person));
        UpdateNamingMode();
    }

    // Dall'import arrivano le parti del nome principale; filiazione, tribù e gli altri nomi non sono su Wikidata e restano
    // quelli della scheda. Una parte che Wikidata non indica non cancella quella scritta a mano (aggiornamento di una scheda).
    private void PrefillRomanName(WikipediaImportResult import)
    {
        if (import.NomiNelTempo.Count > 0)
        {
            MergeImportedRomanNames(import.NomiNelTempo);
            return;
        }

        var principal = PrincipalName;
        var created = principal is null;
        principal = principal?.Clone() ?? new RomanNameRecord { Principale = true };
        principal.FieldsFromParts();
        var before = (principal.Praenomen, principal.Nomen, principal.Cognomen, principal.Agnomen, principal.Prenome, principal.Gentilizio, principal.CognomeRomano, principal.Agnome);

        static string Pick(string imported, string current) => imported.Trim().Length > 0 ? imported.Trim() : current;
        principal.Praenomen = Pick(import.Praenomen, principal.Praenomen);
        principal.Nomen = Pick(import.Nomen, principal.Nomen);
        principal.Cognomen = Pick(import.Cognomen, principal.Cognomen);
        principal.Agnomen = Pick(import.Agnomen, principal.Agnomen);
        principal.NomeLatino = Pick(import.NomeLatino, principal.NomeLatino);
        principal.Prenome = Pick(import.Prenome, principal.Prenome);
        principal.Gentilizio = Pick(import.Gentilizio, principal.Gentilizio);
        principal.CognomeRomano = Pick(import.CognomeRomano, principal.CognomeRomano);
        principal.Agnome = Pick(import.Agnome, principal.Agnome);
        // Le righe si ricompongono solo se l'import ha cambiato qualcosa: i titoli e l'ordine scritti a mano restano.
        if (before != (principal.Praenomen, principal.Nomen, principal.Cognomen, principal.Agnomen, principal.Prenome, principal.Gentilizio, principal.CognomeRomano, principal.Agnome))
        {
            principal.PartsFromFields();
        }

        // Le forme nelle altre lingue lette da Wikidata vanno nelle righe del principale (solo le lingue indicate).
        foreach (var localized in import.LocalizedNames.Where(l => RomanNaming.HasRomanParts(l.Prenome, l.Gentilizio, l.CognomeRomano, l.Agnome)))
        {
            principal.TranslationsFrom(localized.Lingua, localized.Prenome, localized.Gentilizio, localized.CognomeRomano, localized.Agnome);
        }

        if (created)
        {
            if (principal.HasParts)
            {
                romanNames.Add(principal);
            }
        }
        else
        {
            ReplaceRomanName(PrincipalName!, principal);
        }

        UpdateNamingMode();
    }

    // Nomi nel tempo dall'introduzione di Wikipedia: si aggiungono quelli che mancano (confronto sul nome latino) e il
    // principale diventa quello importato; i nomi scritti a mano restano. Un solo nome senza periodo né motivo (quello
    // ricavato dalle parti di Wikidata a un import precedente) viene sostituito.
    private void MergeImportedRomanNames(IReadOnlyList<RomanNameRecord> imported)
    {
        if (romanNames.Count == 0 || (romanNames.Count == 1 && romanNames[0].Period.Length == 0 && romanNames[0].Motivo.Trim().Length == 0))
        {
            SetRomanNames(imported);
            UpdateNamingMode();
            return;
        }

        RomanNameRecord? Same(RomanNameRecord name)
            => romanNames.FirstOrDefault(n => string.Equals(n.LatinName, name.LatinName, StringComparison.OrdinalIgnoreCase));

        foreach (var name in imported)
        {
            var existing = Same(name);
            if (existing is null)
            {
                romanNames.Add(name.Clone());
            }
            else if (name.Principale)
            {
                ReplaceRomanName(existing, name.Clone());
            }
        }

        if (imported.FirstOrDefault(n => n.Principale) is { } principal && Same(principal) is { } target)
        {
            SetPrincipal(target);
        }

        UpdateRomanNameButtons();
        UpdateNamingMode();
    }

    private void SetRomanNames(IEnumerable<RomanNameRecord> source)
    {
        romanNames.Clear();
        foreach (var name in source)
        {
            romanNames.Add(name.Clone());
        }

        UpdateRomanNameButtons();
    }

    private void ReplaceRomanName(RomanNameRecord old, RomanNameRecord updated)
    {
        var index = romanNames.IndexOf(old);
        if (index >= 0)
        {
            romanNames[index] = updated;
            RomanNamesGrid.SelectedItem = updated;
        }
    }

    private string RomanSnapshot()
        => string.Join("\u001e", romanNames.Select(n => string.Join("|",
            n.Principale, string.Join(";", n.OrderedParts.Select(p => $"{p.Tipo}:{p.Latino.Trim()}:{p.Italiano.Trim()}:{string.Join(",", p.Traduzioni.Select(t => $"{t.Lingua}={t.Testo.Trim()}"))}")),
            n.Filiazione, n.Tribu, n.NomeLatino, n.Dal, n.Al, n.Motivo)))
            + "\u001d" + SelectedNaming + "\u001d" + birthGens.Key + "\u001d" + adoptiveGens.Key;

    private RomanNameRecord? SelectedRomanName => RomanNamesGrid.SelectedItem as RomanNameRecord;

    private void RomanNamesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRomanNameButtons();

    private void UpdateRomanNameButtons()
    {
        var selected = SelectedRomanName;
        EditRomanNameButton.IsEnabled = selected is not null;
        RemoveRomanNameButton.IsEnabled = selected is not null;
        PrincipalRomanNameButton.IsEnabled = selected is { Principale: false };
    }

    private void RomanNamesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedRomanName is { } name && e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(RomanNamesGrid, source) is DataGridRow)
        {
            EditRomanName(name);
        }
    }

    private void EditRomanName_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRomanName is { } name)
        {
            EditRomanName(name);
        }
    }

    private void AddRomanName_Click(object sender, RoutedEventArgs e)
    {
        // Il primo nome è il principale.
        var name = new RomanNameRecord { Principale = romanNames.Count == 0 };
        if (ShowRomanNameEditor(name) is { } result)
        {
            romanNames.Add(result);
            RomanNamesGrid.SelectedItem = result;
            RomanNamesGrid.ScrollIntoView(result);
            UpdateNamingMode();
        }
    }

    private void EditRomanName(RomanNameRecord name)
    {
        if (ShowRomanNameEditor(name) is { } result)
        {
            ReplaceRomanName(name, result);
            UpdateNamingMode();
        }
    }

    // Apre il modulo del nome; per il nome principale anche le parti nelle altre lingue della scheda.
    private RomanNameRecord? ShowRomanNameEditor(RomanNameRecord name)
    {
        CommitShownLanguage();
        var father = editingPerson?.FatherId is int fatherId ? store.GetPerson(fatherId) : null;
        var window = new RomanNameEditorWindow(name, father, localizedNames.Select(l => (l.Lingua, l.LinguaNome))) { Owner = this };
        if (window.ShowDialog() != true)
        {
            return null;
        }

        // Una lingua aggiunta nel nome principale si aggiunge anche alla scheda (le sue parti si scrivono al salvataggio).
        var result = window.Result;
        if (name.Principale)
        {
            var added = false;
            foreach (var code in result.Languages.Where(c => localizedNames.All(l => l.Lingua != c)))
            {
                localizedNames.Add(new LocalizedPersonName
                {
                    Lingua = code,
                    LinguaNome = LocalizedPersonName.EditableLanguages.FirstOrDefault(l => l.Code == code).Name ?? code,
                });
                added = true;
            }

            if (added)
            {
                RebuildLanguageOptions(shownLanguage);
            }
        }

        return result;
    }

    private void RemoveRomanName_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRomanName is not { } name)
        {
            return;
        }

        romanNames.Remove(name);
        // Senza il principale diventa principale l'ultimo nome rimasto.
        if (name.Principale && romanNames.Count > 0)
        {
            SetPrincipal(romanNames[^1]);
        }

        UpdateRomanNameButtons();
        UpdateNamingMode();
    }

    private void SetPrincipalRomanName_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRomanName is { } name)
        {
            SetPrincipal(name);
            UpdateNamingMode();
        }
    }

    private void SetPrincipal(RomanNameRecord principal)
    {
        foreach (var name in romanNames.ToList())
        {
            if (name.Principale != ReferenceEquals(name, principal))
            {
                var updated = name.Clone();
                updated.Principale = ReferenceEquals(name, principal);
                ReplaceRomanName(name, updated);
            }
        }

        UpdateRomanNameButtons();
    }
}
