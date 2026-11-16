using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace HistoricEditor;

// Pulsante con l'editor della data da affiancare a un campo data. Si sceglie il tipo (una data, una tra più date, tra due
// date, prima di, dopo), la precisione (giorno, mese, anno, secolo) e, se serve, «circa»; il risultato va nel campo nella
// forma compatta di UncertainDate («12/07/100 a.C.; 13/07/100 a.C.», «tra 1503 e 1519», «c. XV secolo»). L'editor si apre
// sulla data già scritta nel campo. Il calendario gestisce gli anni avanti Cristo e il calendario giuliano (giorni del mese
// e della settimana secondo il calendario scelto nel campo collegato, in mancanza giuliano prima del 15 ottobre 1582).
// Quando il campo perde il focus le date riconosciute vengono riscritte nella forma compatta.
public sealed class DatePickerButton : Button
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(TextBox), typeof(DatePickerButton), new PropertyMetadata(null, OnTargetChanged));

    public static readonly DependencyProperty CalendarSourceProperty = DependencyProperty.Register(
        nameof(CalendarSource), typeof(Selector), typeof(DatePickerButton));

    private enum Precision { Day, Month, Year, Century }

    private static readonly string[] WeekDays = ["lun", "mar", "mer", "gio", "ven", "sab", "dom"];

    private static readonly (UncertainDateKind Kind, string Label)[] Kinds =
    [
        (UncertainDateKind.Exact, "Una data"),
        (UncertainDateKind.Alternatives, "Una tra più date (o)"),
        (UncertainDateKind.Between, "Tra due date"),
        (UncertainDateKind.Before, "Prima di"),
        (UncertainDateKind.After, "Dopo"),
    ];

    private readonly Popup popup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
    private readonly ComboBox kindBox = new() { Width = 180, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox precisionBox = new() { Width = 100, Margin = new Thickness(0, 0, 8, 0) };
    private readonly CheckBox circaBox = new() { Content = "circa", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox monthBox = new() { Width = 110, Margin = new Thickness(4, 0, 4, 0) };
    private readonly TextBox yearBox = new() { Width = 64, Padding = new Thickness(4, 3, 4, 3), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox beforeChristBox = new() { Content = "a.C.", Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel navigation = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
    private readonly UniformGrid weekHeader = new() { Columns = 7, Margin = new Thickness(0, 0, 0, 2) };
    private readonly UniformGrid cellsGrid = new() { Columns = 7 };
    private readonly TextBlock calendarNote = new() { FontSize = 11, Margin = new Thickness(2, 6, 2, 0), TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel chosenPanel = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock resultText = new() { FontSize = 12, Margin = new Thickness(2, 6, 2, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button insertButton = new() { Content = "Inserisci", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(8, 0, 0, 0) };
    private readonly List<DatePoint> chosen = [];
    private bool updating;

    public DatePickerButton()
    {
        // Stesso aspetto degli altri pulsanti a icona accanto ai campi (2026-11-16): lo stile implicito dei Button non vale
        // per le sottoclassi, e prima il pulsante restava chiaro anche nel tema scuro.
        SetResourceReference(StyleProperty, "IconButton");
        Content = new TextBlock { Text = "\uE787", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14 };
        ToolTip = "Scegli la data: una data, più date, un intervallo, prima o dopo; giorno, mese, anno o secolo";
        AutomationProperties.SetName(this, "Scegli la data dal calendario");
        Focusable = true;
        popup.PlacementTarget = this;
        popup.Child = BuildPanel();
    }

    public TextBox? Target
    {
        get => (TextBox?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    // Selettore del calendario (giuliano/gregoriano) della data, se il modulo lo prevede.
    public Selector? CalendarSource
    {
        get => (Selector?)GetValue(CalendarSourceProperty);
        set => SetValue(CalendarSourceProperty, value);
    }

    // Per le prove: il pannello dell'editor.
    internal FrameworkElement Panel => (FrameworkElement)popup.Child;

    internal void SetPrecisionForTest(int index) => precisionBox.SelectedIndex = index;

    private UncertainDateKind Kind => Kinds[Math.Max(kindBox.SelectedIndex, 0)].Kind;

    private Precision CurrentPrecision => (Precision)Math.Max(precisionBox.SelectedIndex, 0);

    // Data scelta, già nel formato in uso: sostituisce la prima ipotesi del testo e conserva le altre.
    public static string ReplaceFirstHypothesis(string? text, string date)
    {
        var pieces = (text ?? string.Empty).Split(';').Select(p => p.Trim()).ToList();
        pieces[0] = date;
        return string.Join("; ", pieces.Where(p => p.Length > 0));
    }

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is TextBox oldBox)
        {
            oldBox.LostKeyboardFocus -= NormalizeTarget;
        }

        if (e.NewValue is TextBox newBox)
        {
            newBox.LostKeyboardFocus += NormalizeTarget;
        }
    }

    private static void NormalizeTarget(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { IsReadOnly: false } box && DateFormats.Normalize(box.Text) is var normalized && normalized != box.Text)
        {
            box.Text = normalized;
        }
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }

        Open(Target?.Text);
        popup.IsOpen = true;
        Dispatcher.BeginInvoke(() => ((UIElement?)cellsGrid.Children.OfType<Button>().FirstOrDefault(b => b.FontWeight == FontWeights.Bold) ?? kindBox).Focus(),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    // Apre l'editor sulla data del campo; senza una data riconosciuta, una data sola sul mese corrente.
    internal void Open(string? text)
    {
        chosen.Clear();
        updating = true;
        var date = UncertainDate.Parse(text);
        if (date is not null)
        {
            kindBox.SelectedIndex = Array.FindIndex(Kinds, k => k.Kind == date.Kind);
            circaBox.IsChecked = date.Circa;
            chosen.AddRange(date.Points);
            var first = date.Points[0];
            precisionBox.SelectedIndex = (int)PrecisionOf(first);
            ShowPoint(first);
        }
        else
        {
            kindBox.SelectedIndex = 0;
            circaBox.IsChecked = false;
            precisionBox.SelectedIndex = (int)Precision.Day;
            ShowPoint(new DatePoint(DateTime.Today.Year, DateTime.Today.Month));
        }

        updating = false;
        Refresh();
    }

    private static Precision PrecisionOf(DatePoint point)
        => point.IsCentury ? Precision.Century : point.Day is not null ? Precision.Day : point.Month is not null ? Precision.Month : Precision.Year;

    // Porta il calendario sul punto (anno, mese, a.C.).
    private void ShowPoint(DatePoint point)
    {
        var year = point.IsCentury ? (point.Century!.Value > 0 ? (point.Century.Value - 1) * 100 + 1 : point.Century.Value * 100) : point.Year!.Value;
        yearBox.Text = Math.Abs(year).ToString(CultureInfo.InvariantCulture);
        beforeChristBox.IsChecked = year < 0;
        monthBox.SelectedIndex = (point.Month ?? 1) - 1;
    }

    private Border BuildPanel()
    {
        foreach (var (_, label) in Kinds)
        {
            kindBox.Items.Add(label);
        }

        foreach (var label in new[] { "Giorno", "Mese", "Anno", "Secolo" })
        {
            precisionBox.Items.Add(label);
        }

        foreach (var month in HistoricalCalendar.MonthNames)
        {
            monthBox.Items.Add(month);
        }

        kindBox.SelectionChanged += (_, _) => KindChanged();
        precisionBox.SelectionChanged += (_, _) => Refresh();
        circaBox.Checked += (_, _) => Refresh();
        circaBox.Unchecked += (_, _) => Refresh();
        monthBox.SelectionChanged += (_, _) => Refresh();
        yearBox.TextChanged += (_, _) => Refresh();
        beforeChristBox.Checked += (_, _) => Refresh();
        beforeChristBox.Unchecked += (_, _) => Refresh();
        AutomationProperties.SetName(kindBox, "Tipo di data");
        AutomationProperties.SetName(precisionBox, "Precisione");
        AutomationProperties.SetName(circaBox, "Data approssimata (circa)");
        AutomationProperties.SetName(monthBox, "Mese");
        AutomationProperties.SetName(yearBox, "Anno");
        AutomationProperties.SetName(beforeChristBox, "Anno avanti Cristo");
        circaBox.ToolTip = "Data approssimata: «c. 1540»";

        var options = new StackPanel { Orientation = Orientation.Horizontal };
        options.Children.Add(kindBox);
        options.Children.Add(precisionBox);
        options.Children.Add(circaBox);

        foreach (var day in WeekDays)
        {
            var label = new TextBlock { Text = day, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "QuietBrush");
            weekHeader.Children.Add(label);
        }

        var cancel = new Button { Content = "Annulla", Padding = new Thickness(14, 4, 14, 4) };
        cancel.Click += (_, _) => Close();
        insertButton.Click += (_, _) => Insert();
        insertButton.SetResourceReference(StyleProperty, "PrimaryButton");
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(cancel);
        footer.Children.Add(insertButton);

        var stack = new StackPanel();
        stack.Children.Add(options);
        stack.Children.Add(navigation);
        stack.Children.Add(weekHeader);
        stack.Children.Add(cellsGrid);
        stack.Children.Add(calendarNote);
        stack.Children.Add(chosenPanel);
        stack.Children.Add(resultText);
        stack.Children.Add(footer);
        calendarNote.SetResourceReference(TextBlock.ForegroundProperty, "QuietBrush");

        var border = new Border
        {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Width = 380,
            Child = stack,
        };
        border.SetResourceReference(Border.BackgroundProperty, "AppBackground");
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "InkBrush");
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        return border;
    }

    private void Close()
    {
        popup.IsOpen = false;
        Focus();
    }

    // Cambiando tipo restano le date compatibili: una sola per «una data», «prima» e «dopo», due al massimo per «tra».
    private void KindChanged()
    {
        if (updating)
        {
            return;
        }

        var keep = Kind switch
        {
            UncertainDateKind.Alternatives => chosen.Count,
            UncertainDateKind.Between => 2,
            _ => 1,
        };
        if (chosen.Count > keep)
        {
            chosen.RemoveRange(keep, chosen.Count - keep);
        }

        Refresh();
    }

    private Button NavButton(string glyph, string name, Action action)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11 },
            Padding = new Thickness(6, 3, 6, 3),
            ToolTip = name,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    private bool TryReadYear(out int year)
    {
        year = 0;
        if (!int.TryParse(yearBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 9999)
        {
            return false;
        }

        year = beforeChristBox.IsChecked == true ? -value : value;
        return true;
    }

    private void SetYear(int year)
    {
        updating = true;
        yearBox.Text = Math.Abs(year).ToString(CultureInfo.InvariantCulture);
        beforeChristBox.IsChecked = year < 0;
        updating = false;
    }

    // Senza anno zero: dopo il 1 a.C. viene il 1 d.C.
    private static int AddYears(int year, int delta)
    {
        var result = year + delta;
        if (year < 0 && result >= 0)
        {
            result++;
        }
        else if (year > 0 && result <= 0)
        {
            result--;
        }

        return result;
    }

    private void MoveMonth(int delta)
    {
        if (!TryReadYear(out var year) || monthBox.SelectedIndex < 0)
        {
            return;
        }

        var month = monthBox.SelectedIndex + 1 + delta;
        if (month is < 1 or > 12)
        {
            month = month < 1 ? 12 : 1;
            year = AddYears(year, delta);
        }

        SetYear(year);
        updating = true;
        monthBox.SelectedIndex = month - 1;
        updating = false;
        Refresh();
    }

    private void MoveYears(int delta)
    {
        if (TryReadYear(out var year))
        {
            SetYear(AddYears(year, delta));
            Refresh();
        }
    }

    private bool IsJulian(int year, int month)
    {
        var code = (CalendarSource?.SelectedItem as CalendarOption)?.Codice ?? HistoricalCalendar.NonIndicato;
        return code switch
        {
            HistoricalCalendar.Giuliano => true,
            HistoricalCalendar.Gregoriano => false,
            _ => year < 1582 || (year == 1582 && month < 10),
        };
    }

    // Ordine dei punti, per l'intervallo e per evidenziarlo: anno, poi mese e giorno.
    private static (int, int, int) Order(DatePoint p) => (p.First, p.Month ?? 0, p.Day ?? 0);

    // Stato di una cella: scelta (in grassetto) o dentro l'intervallo «tra».
    private (bool Chosen, bool InRange) CellState(DatePoint cell)
    {
        if (chosen.Contains(cell))
        {
            return (true, false);
        }

        if (Kind == UncertainDateKind.Between && chosen.Count == 2)
        {
            var (from, to) = (Order(chosen[0]), Order(chosen[1]));
            var key = Order(cell);
            return (false, key.CompareTo(from) > 0 && key.CompareTo(to) < 0);
        }

        return (false, false);
    }

    private Button Cell(string text, DatePoint point, string name)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(0, 3, 0, 3),
            Margin = new Thickness(1),
            ToolTip = UncertainDate.ReadablePoint(point),
        };
        var (isChosen, inRange) = CellState(point);
        if (isChosen || inRange)
        {
            button.SetResourceReference(BackgroundProperty, "SelectionBrush");
            button.FontWeight = isChosen ? FontWeights.Bold : FontWeights.Normal;
        }

        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => Choose(point);
        return button;
    }

    // Precisione per cui è costruita la barra di navigazione: si ricostruisce solo quando cambia, perché togliere e
    // rimettere la casella dell'anno mentre si scrive le farebbe perdere il focus.
    private Precision? navigationFor;

    private void Refresh()
    {
        if (updating)
        {
            return;
        }

        cellsGrid.Children.Clear();
        calendarNote.Text = string.Empty;
        var precision = CurrentPrecision;
        weekHeader.Visibility = precision == Precision.Day ? Visibility.Visible : Visibility.Collapsed;
        if (navigationFor != precision)
        {
            navigationFor = precision;
            navigation.Children.Clear();
            switch (precision)
            {
                case Precision.Day:
                    navigation.Children.Add(NavButton("\uE76B", "Mese precedente", () => MoveMonth(-1)));
                    navigation.Children.Add(monthBox);
                    navigation.Children.Add(yearBox);
                    navigation.Children.Add(beforeChristBox);
                    navigation.Children.Add(NavButton("\uE76C", "Mese successivo", () => MoveMonth(1)));
                    break;
                case Precision.Month:
                    navigation.Children.Add(NavButton("\uE76B", "Anno precedente", () => MoveYears(-1)));
                    navigation.Children.Add(yearBox);
                    navigation.Children.Add(beforeChristBox);
                    navigation.Children.Add(NavButton("\uE76C", "Anno successivo", () => MoveYears(1)));
                    break;
                case Precision.Year:
                    navigation.Children.Add(NavButton("\uE76B", "Decennio precedente", () => MoveYears(-10)));
                    navigation.Children.Add(yearBox);
                    navigation.Children.Add(beforeChristBox);
                    navigation.Children.Add(NavButton("\uE76C", "Decennio successivo", () => MoveYears(10)));
                    break;
                default:
                    navigation.Children.Add(beforeChristBox);
                    break;
            }
        }

        switch (precision)
        {
            case Precision.Day:
                FillDays();
                break;
            case Precision.Month:
                FillMonths();
                break;
            case Precision.Year:
                FillYears();
                break;
            default:
                FillCenturies();
                break;
        }

        RefreshChosen();
    }

    private void FillDays()
    {
        cellsGrid.Columns = 7;
        if (!TryReadYear(out var year) || monthBox.SelectedIndex < 0)
        {
            calendarNote.Text = "Scrivere un anno tra 1 e 9999.";
            return;
        }

        var month = monthBox.SelectedIndex + 1;
        var julian = IsJulian(year, month);
        var calendar = julian ? HistoricalCalendar.Giuliano : HistoricalCalendar.Gregoriano;
        calendarNote.Text = $"Calendario {(julian ? "giuliano" : "gregoriano")}.";
        var firstDay = HistoricalCalendar.ToDayNumber(new CalendarDate(year, month, 1), calendar);
        // Giorno giuliano + 1 modulo 7: 0 = domenica; la settimana del calendario comincia dal lunedì.
        var offset = (int)(((firstDay + 1) % 7 + 6) % 7);
        for (var i = 0; i < offset; i++)
        {
            cellsGrid.Children.Add(new Border());
        }

        var days = HistoricalCalendar.DaysInMonth(year, month, julian);
        for (var day = 1; day <= days; day++)
        {
            var point = new DatePoint(year, month, day);
            cellsGrid.Children.Add(Cell(day.ToString(CultureInfo.InvariantCulture), point, UncertainDate.ReadablePoint(point)));
        }
    }

    private void FillMonths()
    {
        cellsGrid.Columns = 4;
        if (!TryReadYear(out var year))
        {
            calendarNote.Text = "Scrivere un anno tra 1 e 9999.";
            return;
        }

        for (var month = 1; month <= 12; month++)
        {
            var point = new DatePoint(year, month);
            cellsGrid.Children.Add(Cell(HistoricalCalendar.MonthNames[month - 1][..3], point, UncertainDate.ReadablePoint(point)));
        }
    }

    // Dieci anni dello stesso decennio (senza l'anno zero).
    private void FillYears()
    {
        cellsGrid.Columns = 5;
        if (!TryReadYear(out var year))
        {
            calendarNote.Text = "Scrivere un anno tra 1 e 9999.";
            return;
        }

        var sign = year < 0 ? -1 : 1;
        var decade = Math.Abs(year) / 10 * 10;
        for (var value = decade; value < decade + 10; value++)
        {
            if (value == 0)
            {
                continue;
            }

            var point = new DatePoint(sign * value);
            cellsGrid.Children.Add(Cell(value.ToString(CultureInfo.InvariantCulture), point, UncertainDate.ReadablePoint(point)));
        }
    }

    private void FillCenturies()
    {
        cellsGrid.Columns = 7;
        var sign = beforeChristBox.IsChecked == true ? -1 : 1;
        for (var century = 1; century <= 21; century++)
        {
            var point = new DatePoint(null, Century: sign * century);
            cellsGrid.Children.Add(Cell(UncertainDate.ToRoman(century), point, UncertainDate.ReadablePoint(point)));
        }
    }

    // Clic su una cella: una data, prima e dopo la sostituiscono e si inseriscono subito; più date si aggiungono o tolgono;
    // tra due date il primo clic è l'inizio, il secondo la fine, un terzo ricomincia.
    private void Choose(DatePoint point)
    {
        switch (Kind)
        {
            case UncertainDateKind.Alternatives:
                if (!chosen.Remove(point))
                {
                    chosen.Add(point);
                    chosen.Sort((a, b) => Order(a).CompareTo(Order(b)));
                }

                break;
            case UncertainDateKind.Between:
                if (chosen.Count >= 2)
                {
                    chosen.Clear();
                }

                chosen.Add(point);
                chosen.Sort((a, b) => Order(a).CompareTo(Order(b)));
                break;
            default:
                chosen.Clear();
                chosen.Add(point);
                Insert();
                return;
        }

        Refresh();
    }

    // Date scelte, ciascuna con «×» per toglierla, e anteprima del risultato.
    private void RefreshChosen()
    {
        chosenPanel.Children.Clear();
        foreach (var point in chosen.ToList())
        {
            var chip = new Button
            {
                Content = $"{UncertainDate.ReadablePoint(point)}  ×",
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 4),
                ToolTip = "Togli questa data",
            };
            AutomationProperties.SetName(chip, $"Togli {UncertainDate.ReadablePoint(point)}");
            chip.Click += (_, _) =>
            {
                chosen.Remove(point);
                Refresh();
            };
            chosenPanel.Children.Add(chip);
        }

        var result = Result();
        insertButton.IsEnabled = result is not null;
        resultText.Text = result is not null
            ? $"Risultato: {result.Readable()}"
            : Kind switch
            {
                UncertainDateKind.Alternatives => "Scegliere almeno due date.",
                UncertainDateKind.Between => chosen.Count == 0 ? "Scegliere la data iniziale." : "Scegliere la data finale.",
                _ => "Scegliere una data.",
            };
    }

    private UncertainDate? Result()
    {
        var circa = circaBox.IsChecked == true;
        return Kind switch
        {
            UncertainDateKind.Alternatives when chosen.Count >= 2 => new UncertainDate(UncertainDateKind.Alternatives, [.. chosen], circa),
            UncertainDateKind.Alternatives when chosen.Count == 1 => new UncertainDate(UncertainDateKind.Exact, [.. chosen], circa),
            UncertainDateKind.Between when chosen.Count == 2 => new UncertainDate(UncertainDateKind.Between, [.. chosen], circa),
            UncertainDateKind.Exact or UncertainDateKind.Before or UncertainDateKind.After when chosen.Count == 1 => new UncertainDate(Kind, [.. chosen], circa),
            _ => null,
        };
    }

    // Scrive nel campo la data nella forma compatta e chiude l'editor.
    private void Insert()
    {
        if (Target is null || Result() is not { } result)
        {
            return;
        }

        Target.Text = result.Compact();
        popup.IsOpen = false;
        Target.Focus();
        Target.CaretIndex = Target.Text.Length;
    }

    // Per le prove: risultato dell'editor dopo aver aperto il testo e scelto i punti indicati.
    internal string? ResultFor(string? text, UncertainDateKind kind, IEnumerable<DatePoint> points, bool circa = false)
    {
        Open(text);
        updating = true;
        kindBox.SelectedIndex = Array.FindIndex(Kinds, k => k.Kind == kind);
        circaBox.IsChecked = circa;
        updating = false;
        chosen.Clear();
        chosen.AddRange(points);
        return Result()?.Compact();
    }
}
