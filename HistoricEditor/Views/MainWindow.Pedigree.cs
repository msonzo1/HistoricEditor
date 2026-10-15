using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HistoricEditor;

// Vista ad albero genealogico: a sinistra coniugi e figli, poi la persona e i suoi antenati fino ai trisnonni.
// Le caselle vuote (trasparenti, con il +) permettono di collegare una persona esistente o di crearne una nuova.
public partial class MainWindow
{
    private const double CardWidth = 180;
    private const double CardHeight = 58;
    private const double RowHeight = 66;

    private void RenderPedigree()
    {
        PedigreeGrid.Children.Clear();
        PedigreeGrid.RowDefinitions.Clear();
        PedigreeGrid.ColumnDefinitions.Clear();

        var rootId = pedigreeRootId;
        var hasRoot = rootId is int id && people.Any(p => p.Id == id);
        PedigreeView.Visibility = hasRoot ? Visibility.Visible : Visibility.Collapsed;
        PedigreeHint.Visibility = hasRoot ? Visibility.Collapsed : Visibility.Visible;
        if (!hasRoot)
        {
            return;
        }

        var summaries = store.GetPersonSummaries().ToDictionary(p => p.Id);
        var byId = people.ToDictionary(p => p.Id);
        var generations = Pedigree.Build(
            rootId!.Value,
            pid => summaries.GetValueOrDefault(pid),
            pid => byId.TryGetValue(pid, out var p) ? (p.MotherId, p.FatherId) : (null, null));

        var rows = 1 << (Pedigree.Generations - 1);
        for (var r = 0; r < rows; r++)
        {
            PedigreeGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        }

        // Colonna 0: discendenti; colonne successive: una per generazione di antenati.
        PedigreeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardWidth + 36) });
        for (var g = 0; g < Pedigree.Generations; g++)
        {
            PedigreeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardWidth + 28) });
        }

        AddDescendants(rootId.Value, summaries, rows);
        AddConnectors(generations, rows);

        foreach (var generation in generations)
        {
            foreach (var slot in generation)
            {
                if (!slot.IsFilled && !slot.CanAdd)
                {
                    continue;
                }

                var span = rows >> slot.Generation;
                var card = CreateSlotCard(slot);
                Grid.SetColumn(card, slot.Generation + 1);
                Grid.SetRow(card, slot.Index * span);
                Grid.SetRowSpan(card, span);
                PedigreeGrid.Children.Add(card);
            }
        }
    }

    private void AddDescendants(int rootId, IReadOnlyDictionary<int, PersonSummary> summaries, int rows)
    {
        var family = store.GetFamily(rootId);
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 36, 0) };
        AutomationPropertiesHelper.SetName(panel, "Coniugi e figli");

        foreach (var union in family.Unioni)
        {
            if (union.Coniuge is { } spouse)
            {
                panel.Children.Add(CreatePersonCard(Header("Coniuge", union), spouse));
            }

            foreach (var child in union.Figli)
            {
                panel.Children.Add(Indent(CreatePersonCard(child.Sesso == Sessi.Femmina ? "Figlia" : "Figlio", child)));
            }
        }

        foreach (var child in family.AltriFigli)
        {
            panel.Children.Add(Indent(CreatePersonCard(child.Sesso == Sessi.Femmina ? "Figlia" : "Figlio", child)));
        }

        panel.Children.Add(CreateAddCard("Aggiungi coniuge", (_, _) => AddSpouse_Click(this, new RoutedEventArgs())));
        panel.Children.Add(CreateAddCard("Aggiungi figlio", (_, _) => AddChild_Click(this, new RoutedEventArgs())));

        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        Grid.SetColumn(scroll, 0);
        Grid.SetRow(scroll, 0);
        Grid.SetRowSpan(scroll, rows);
        PedigreeGrid.Children.Add(scroll);

        static string Header(string role, UnionView union) => union.Tipo.Length > 0 ? $"{role} · {union.Tipo}" : role;
        static FrameworkElement Indent(FrameworkElement card)
        {
            card.Margin = new Thickness(16, card.Margin.Top, 0, card.Margin.Bottom);
            return card;
        }
    }

    // Linee a gomito che collegano ogni casella piena al padre e alla madre nella colonna successiva.
    private void AddConnectors(IReadOnlyList<IReadOnlyList<PedigreeSlot>> generations, int rows)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var g = 0; g < generations.Count - 1; g++)
            {
                var parents = generations[g + 1];
                foreach (var child in generations[g].Where(s => s.IsFilled))
                {
                    var visible = parents
                        .Where(p => p.Index / 2 == child.Index && (p.IsFilled || p.CanAdd))
                        .ToList();
                    if (visible.Count == 0)
                    {
                        continue;
                    }

                    var childY = CenterY(g, child.Index, rows);
                    var startX = ColumnLeft(g + 1) + CardWidth;
                    var endX = ColumnLeft(g + 2);
                    var midX = (startX + endX) / 2;
                    context.BeginFigure(new Point(startX, childY), false, false);
                    context.LineTo(new Point(midX, childY), true, false);
                    foreach (var parent in visible)
                    {
                        var parentY = CenterY(g + 1, parent.Index, rows);
                        context.BeginFigure(new Point(midX, childY), false, false);
                        context.LineTo(new Point(midX, parentY), true, false);
                        context.LineTo(new Point(endX, parentY), true, false);
                    }
                }
            }
        }

        var path = new System.Windows.Shapes.Path
        {
            Data = geometry,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AppControlBorder");
        Grid.SetColumnSpan(path, PedigreeGrid.ColumnDefinitions.Count);
        Grid.SetRowSpan(path, rows);
        PedigreeGrid.Children.Insert(0, path);

        static double ColumnLeft(int column) => column == 0 ? 0 : CardWidth + 36 + (column - 1) * (CardWidth + 28);
        static double CenterY(int generation, int index, int rows)
        {
            var span = rows >> generation;
            return (index * span + span / 2.0) * RowHeight;
        }
    }

    private Button CreateSlotCard(PedigreeSlot slot)
    {
        if (slot.Person is { } person)
        {
            var card = CreatePersonCard(slot.Ruolo, person);
            if (slot.IsRoot)
            {
                card.Tag = "Root";
                card.ToolTip = "Apri la scheda";
            }

            return card;
        }

        return CreateAddCard($"Aggiungi {slot.Ruolo.ToLowerInvariant()}", (_, _) =>
        {
            if (slot.ChildId is int childId && FamilyActions.AddParent(this, store, childId, slot.IsMother))
            {
                ReloadAfterFamilyChange(pedigreeRootId ?? childId);
            }
        });
    }

    // Clic su una persona: diventa il centro dell'albero; clic sul centro: apre la scheda.
    private Button CreatePersonCard(string role, PersonSummary person)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = role.ToUpperInvariant(), FontFamily = new FontFamily("Consolas"), FontSize = 9, Foreground = (Brush)FindResource("QuietBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        content.Children.Add(new TextBlock { Text = person.Nome, FontFamily = new FontFamily("Georgia"), FontSize = 13, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        if (person.Anni.Length > 0)
        {
            content.Children.Add(new TextBlock { Text = person.Anni, FontFamily = new FontFamily("Consolas"), FontSize = 10, Foreground = (Brush)FindResource("QuietBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        }

        var card = NewCard(content, $"{role}: {person.Etichetta}");
        if (person.Sesso is Sessi.Maschio or Sessi.Femmina)
        {
            card.SetResourceReference(BackgroundProperty, person.Sesso == Sessi.Maschio ? "MaleCardBrush" : "FemaleCardBrush");
        }
        card.ToolTip = "Mostra il suo albero genealogico";
        card.Click += (_, _) =>
        {
            if (person.Id == pedigreeRootId)
            {
                EditPerson_Click(card, new RoutedEventArgs());
                return;
            }

            pedigreeRootId = person.Id;
            SelectInTree(person.Id);
            RenderPedigree();
            UpdateCommands();
        };
        return card;
    }

    private Button CreateAddCard(string text, RoutedEventHandler click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new TextBlock { Text = "\uE710", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        content.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        var card = NewCard(content, text);
        card.Tag = "Empty";
        card.ToolTip = "Scegli una persona esistente o creane una nuova";
        card.Click += click;
        return card;
    }

    private Button NewCard(object content, string accessibleName)
    {
        var card = new Button
        {
            Content = content,
            Width = CardWidth,
            Height = CardHeight,
            Margin = new Thickness(0, 4, 0, 4),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)FindResource("PedigreeCard"),
        };
        AutomationPropertiesHelper.SetName(card, accessibleName);
        return card;
    }

    private static class AutomationPropertiesHelper
    {
        public static void SetName(DependencyObject element, string name)
            => System.Windows.Automation.AutomationProperties.SetName(element, name);
    }
}
