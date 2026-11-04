using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HistoricEditor;

// Menu contestuale (clic destro, tasto Menu o Maiusc+F10) sui personaggi dell'elenco e dell'albero genealogico.
public partial class MainWindow
{
    private ContextMenu BuildPersonMenu(HistoricalPerson person)
    {
        var menu = new ContextMenu();
        System.Windows.Automation.AutomationProperties.SetName(menu, $"Azioni per {person.DisplayName}");

        void Add(string header, string glyph, Action action, string? accessibleName = null)
        {
            var item = new MenuItem
            {
                Header = header,
                Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), VerticalAlignment = VerticalAlignment.Center },
            };
            System.Windows.Automation.AutomationProperties.SetName(item, accessibleName ?? header.Replace("_", string.Empty).TrimEnd('…'));
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("_Visualizza scheda", "\uE890", () => ViewPerson(person));
        Add("_Apri scheda", "\uE70F", () => EditPerson(person));
        menu.Items.Add(new Separator());
        Add("_Famiglia…", "\uE716", () => EditFamily(person));
        Add("Aggiungi _coniuge…", "\uEB51", () => AddSpouse(person));
        Add("Aggiungi _figlio…", "\uE8FA", () => AddChild(person));
        Add("_Eventi…", "\uE787", () => OpenEvents(person.Id));
        Add("_Opere d'arte…", "\uE8B9", () => OpenArtworks(person.Id));
        Add("Mostra sulla _mappa…", "\uE707", () => OpenMap(person.Id));
        Add("Ca_riche e successioni…", "\uE728", () => OpenOffices(person.Id));
        Add("Linea del _tempo…", "\uE81C", () => OpenTimeline(person.Id));
        menu.Items.Add(new Separator());
        Add("_Elimina…", "\uE74D", () => DeletePerson(person), "Elimina personaggio");
        return menu;
    }

    // Il clic destro seleziona prima la persona sotto il puntatore, come in Esplora file.
    private void PeopleTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null and not TreeViewItem)
        {
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }

        if (source is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void PeopleTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (SelectedPerson is not { } person)
        {
            e.Handled = true;
            return;
        }

        PeopleTree.ContextMenu = BuildPersonMenu(person);
    }

    private void AttachPersonMenu(Button card, int personId)
    {
        // Menu provvisorio: senza un ContextMenu l'evento di apertura non viene generato.
        card.ContextMenu = new ContextMenu();
        card.ContextMenuOpening += (_, e) =>
        {
            if (people.FirstOrDefault(p => p.Id == personId) is { } person)
            {
                card.ContextMenu = BuildPersonMenu(person);
            }
            else
            {
                e.Handled = true;
            }
        };
    }
}
