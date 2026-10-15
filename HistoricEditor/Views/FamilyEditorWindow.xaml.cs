using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace HistoricEditor;

public partial class FamilyEditorWindow : Window
{
    private readonly PeopleStore store;
    private readonly int personId;
    private readonly string personName;
    private bool loading;

    public bool Changed { get; private set; }

    public FamilyEditorWindow(PeopleStore store, int personId, string personName)
    {
        InitializeComponent();
        this.store = store;
        this.personId = personId;
        this.personName = personName;
        Title = $"Famiglia di {personName}";
        HeaderText.Text = Title;
        Refresh();
    }

    private void Refresh()
    {
        loading = true;
        var sesso = store.GetSex(personId);
        MaleRadio.IsChecked = sesso == Sessi.Maschio;
        FemaleRadio.IsChecked = sesso == Sessi.Femmina;
        UnknownRadio.IsChecked = sesso is not (Sessi.Maschio or Sessi.Femmina);
        loading = false;

        var family = store.GetFamily(personId);
        FatherText.Text = family.Padre?.Etichetta ?? "Non indicato";
        MotherText.Text = family.Madre?.Etichetta ?? "Non indicata";
        UnionsList.ItemsSource = family.Unioni;
        NoUnionsText.Visibility = family.Unioni.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OtherChildrenList.ItemsSource = family.AltriFigli;
    }

    private void Run(Action action, string? success = null)
    {
        try
        {
            action();
            Changed = true;
            FamilyMessage.Text = success ?? string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            FamilyMessage.Text = ex.Message;
        }
        catch (DbUpdateException)
        {
            FamilyMessage.Text = "Impossibile salvare la modifica.";
        }

        Refresh();
    }

    private void Sex_Checked(object sender, RoutedEventArgs e)
    {
        if (!loading && sender is RadioButton { Tag: string sesso })
        {
            Run(() => store.SetSex(personId, sesso));
        }
    }

    private void ChooseFather_Click(object sender, RoutedEventArgs e) => ChooseParent(mother: false);

    private void ChooseMother_Click(object sender, RoutedEventArgs e) => ChooseParent(mother: true);

    private void ChooseParent(bool mother)
    {
        var picker = new PersonPickerWindow(store, mother ? "Scegli la madre" : "Scegli il padre", personId, mother ? Sessi.Femmina : Sessi.Maschio) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedId is int id)
        {
            Run(() => store.SetParent(personId, id, mother));
        }
    }

    private void RemoveFather_Click(object sender, RoutedEventArgs e) => Run(() => store.SetParent(personId, null, mother: false));

    private void RemoveMother_Click(object sender, RoutedEventArgs e) => Run(() => store.SetParent(personId, null, mother: true));

    private void AddSpouse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new PersonPickerWindow(store, "Scegli il coniuge", personId, Sessi.Opposto(store.GetSex(personId))) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedId is int spouseId)
        {
            EditUnion(new Unione { Persona1Id = personId, Persona2Id = spouseId });
        }
    }

    private void RegisterUnion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is UnionView { Coniuge: { } coniuge })
        {
            EditUnion(new Unione { Persona1Id = personId, Persona2Id = coniuge.Id });
        }
    }

    private void EditUnion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is UnionView { UnionId: int id } && store.GetUnion(id) is { } unione)
        {
            EditUnion(unione);
        }
    }

    private void EditUnion(Unione unione)
    {
        var spouseId = unione.Persona1Id == personId ? unione.Persona2Id : unione.Persona1Id;
        var spouse = store.GetPersonSummaries().FirstOrDefault(p => p.Id == spouseId)?.Nome ?? "coniuge";
        var editor = new UnionEditorWindow(store, unione, $"Unione tra {personName} e {spouse}") { Owner = this };
        if (editor.ShowDialog() == true)
        {
            Changed = true;
            Refresh();
        }
    }

    private void RemoveUnion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not UnionView { UnionId: int id } view)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Rimuovere l’unione con {view.Coniuge?.Nome ?? "il coniuge"}? I figli restano collegati a entrambi i genitori.",
            "Rimuovi unione",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            Run(() => store.DeleteUnion(id));
        }
    }

    private void AddChildToUnion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is UnionView view)
        {
            AddChild(view.Coniuge?.Id);
        }
    }

    private void AddChild_Click(object sender, RoutedEventArgs e) => AddChild(null);

    private void AddChild(int? otherParentId)
    {
        var sesso = store.GetSex(personId);
        if (sesso is not (Sessi.Maschio or Sessi.Femmina))
        {
            FamilyMessage.Text = "Indica prima il sesso, per sapere se è padre o madre.";
            return;
        }

        var picker = new PersonPickerWindow(store, "Scegli il figlio o la figlia", personId, string.Empty) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedId is not int childId)
        {
            return;
        }

        // Avvisa prima di sostituire un genitore già registrato.
        var (motherId, fatherId) = store.GetParents(childId);
        var ownSlot = sesso == Sessi.Femmina ? motherId : fatherId;
        var otherSlot = sesso == Sessi.Femmina ? fatherId : motherId;
        var replaces = (ownSlot is int own && own != personId) || (otherParentId is int other && otherSlot is int existing && existing != other);
        if (replaces && MessageBox.Show(
                this,
                "Questa persona ha già un altro genitore registrato. Sostituirlo?",
                "Aggiungi figlio",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        Run(() => store.AssignChild(personId, childId, otherParentId));
    }

    private void RemoveChild_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PersonSummary child)
        {
            Run(() => store.RemoveChild(personId, child.Id));
        }
    }
}
