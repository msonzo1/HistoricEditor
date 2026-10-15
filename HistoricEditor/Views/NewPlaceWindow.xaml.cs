using System.Windows;

namespace HistoricEditor;

public enum NewPlaceKind
{
    Current,
    Historic,
    Specific,
}

public partial class NewPlaceWindow : Window
{
    public NewPlaceWindow(string? contextPath, bool allowSpecific)
    {
        InitializeComponent();
        SpecificOption.Visibility = allowSpecific ? Visibility.Visible : Visibility.Collapsed;
        ContextText.Text = contextPath is null ? string.Empty : $"Si partirà dal luogo selezionato: {contextPath}";
    }

    public NewPlaceKind Kind { get; private set; }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        Kind = HistoricOption.IsChecked == true ? NewPlaceKind.Historic
            : SpecificOption.IsChecked == true ? NewPlaceKind.Specific
            : NewPlaceKind.Current;
        DialogResult = true;
    }
}

public sealed record CreatedPlace(int Id, bool IsSpecific);

public static class PlaceCreation
{
    // Unico punto di ingresso per creare luoghi: chiede il tipo e apre il form adatto, partendo dal nodo selezionato.
    public static CreatedPlace? Create(PeopleStore store, Window owner, PlaceNode? context, bool allowSpecific = true)
    {
        var chooser = new NewPlaceWindow(context?.Path, allowSpecific) { Owner = owner };
        if (chooser.ShowDialog() != true)
        {
            return null;
        }

        var container = context is { IsSpecific: true } ? context.Parent : context;
        switch (chooser.Kind)
        {
            case NewPlaceKind.Current:
            {
                var prefill = container is { IsHistoric: false } ? store.GetChainLevels(container.Id) : DefaultChain();
                var editor = new PlacesEditorWindow(prefill, store: store) { Owner = owner };
                if (editor.ShowDialog() != true)
                {
                    return null;
                }

                return store.EnsurePlace(editor.ResultLevels, editor.ResultLatitude, editor.ResultLongitude) is int id
                    ? new CreatedPlace(id, false)
                    : null;
            }

            case NewPlaceKind.Historic:
            {
                var editor = new HistoricPlaceEditorWindow(store, container is { IsHistoric: true } ? container.Id : null) { Owner = owner };
                return editor.ShowDialog() == true && editor.ResultId is int id ? new CreatedPlace(id, false) : null;
            }

            default:
            {
                var editor = new SpecificPlaceEditorWindow(store, null, container?.Id) { Owner = owner };
                return editor.ShowDialog() == true && editor.ResultId is int id ? new CreatedPlace(id, true) : null;
            }
        }
    }

    private static List<GeoLevel> DefaultChain()
        => [.. GeoSchemas.For("Italia").Select((tipo, index) => new GeoLevel(tipo, index == 0 ? "Italia" : string.Empty))];
}
