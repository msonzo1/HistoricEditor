using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace HistoricEditor;

// Piccolo pulsante con un calendario da affiancare a un campo data. Il calendario gestisce anche gli anni avanti Cristo
// e il calendario giuliano (giorni del mese e della settimana secondo il calendario scelto nel campo collegato,
// in mancanza giuliano prima del 15 ottobre 1582). La data scelta prende il posto della prima ipotesi del campo,
// nel formato in uso; quando il campo perde il focus le date riconosciute vengono riscritte nello stesso formato.
public sealed class DatePickerButton : Button
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(TextBox), typeof(DatePickerButton), new PropertyMetadata(null, OnTargetChanged));

    public static readonly DependencyProperty CalendarSourceProperty = DependencyProperty.Register(
        nameof(CalendarSource), typeof(Selector), typeof(DatePickerButton));

    private static readonly string[] WeekDays = ["lun", "mar", "mer", "gio", "ven", "sab", "dom"];

    private readonly Popup popup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
    private readonly ComboBox monthBox = new() { Width = 110, Margin = new Thickness(4, 0, 4, 0) };
    private readonly TextBox yearBox = new() { Width = 64, Padding = new Thickness(4, 3, 4, 3), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox beforeChristBox = new() { Content = "a.C.", Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly UniformGrid daysGrid = new() { Columns = 7, Rows = 6 };
    private readonly TextBlock calendarNote = new() { FontSize = 11, Margin = new Thickness(2, 6, 2, 0), TextWrapping = TextWrapping.Wrap };
    private bool updating;
    private int? selectedDay;

    public DatePickerButton()
    {
        Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14 };
        Padding = new Thickness(7, 4, 7, 4);
        Margin = new Thickness(6, 0, 0, 0);
        ToolTip = "Scegli la data dal calendario";
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

        var start = HistoricalCalendar.TryParse(Target?.Text, out var date) && date.Year != 0
            ? date
            : new CalendarDate(DateTime.Today.Year, DateTime.Today.Month, null);
        selectedDay = date.Day;
        updating = true;
        yearBox.Text = Math.Abs(start.Year).ToString(CultureInfo.InvariantCulture);
        beforeChristBox.IsChecked = start.Year < 0;
        monthBox.SelectedIndex = (start.Month ?? 1) - 1;
        updating = false;
        Refresh();
        popup.IsOpen = true;
        Dispatcher.BeginInvoke(() =>
        {
            var focus = daysGrid.Children.OfType<Button>().FirstOrDefault(b => b.Tag is int day && day == selectedDay) ?? (UIElement)yearBox;
            focus.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private Border BuildPanel()
    {
        foreach (var month in HistoricalCalendar.MonthNames)
        {
            monthBox.Items.Add(month);
        }

        monthBox.SelectionChanged += (_, _) => Refresh();
        yearBox.TextChanged += (_, _) => Refresh();
        beforeChristBox.Checked += (_, _) => Refresh();
        beforeChristBox.Unchecked += (_, _) => Refresh();
        AutomationProperties.SetName(monthBox, "Mese");
        AutomationProperties.SetName(yearBox, "Anno");
        AutomationProperties.SetName(beforeChristBox, "Anno avanti Cristo");

        var previous = NavButton("", "Mese precedente", -1);
        var next = NavButton("", "Mese successivo", 1);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(previous);
        header.Children.Add(monthBox);
        header.Children.Add(yearBox);
        header.Children.Add(beforeChristBox);
        header.Children.Add(next);

        var weekHeader = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var day in WeekDays)
        {
            var label = new TextBlock { Text = day, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "QuietBrush");
            weekHeader.Children.Add(label);
        }

        var monthOnly = new Button { Content = "Solo mese e anno", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
        monthOnly.Click += (_, _) => Pick(null, monthOnly: true);
        var yearOnly = new Button { Content = "Solo anno", Padding = new Thickness(8, 3, 8, 3) };
        yearOnly.Click += (_, _) => Pick(null, monthOnly: false);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(monthOnly);
        footer.Children.Add(yearOnly);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(weekHeader);
        stack.Children.Add(daysGrid);
        stack.Children.Add(calendarNote);
        stack.Children.Add(footer);
        calendarNote.SetResourceReference(TextBlock.ForegroundProperty, "QuietBrush");

        var border = new Border
        {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Width = 330,
            Child = stack,
        };
        border.SetResourceReference(Border.BackgroundProperty, "AppBackground");
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "InkBrush");
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                Focus();
                e.Handled = true;
            }
        };
        return border;
    }

    private Button NavButton(string glyph, string name, int delta)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11 },
            Padding = new Thickness(6, 3, 6, 3),
            ToolTip = name,
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => MoveMonth(delta);
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
            // Senza anno zero: dopo il 1 a.C. viene il 1 d.C.
            year = year + delta is 0 ? delta : year + delta;
        }

        updating = true;
        yearBox.Text = Math.Abs(year).ToString(CultureInfo.InvariantCulture);
        beforeChristBox.IsChecked = year < 0;
        monthBox.SelectedIndex = month - 1;
        updating = false;
        Refresh();
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

    private void Refresh()
    {
        if (updating)
        {
            return;
        }

        daysGrid.Children.Clear();
        if (!TryReadYear(out var year) || monthBox.SelectedIndex < 0)
        {
            calendarNote.Text = "Scrivi un anno tra 1 e 9999.";
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
            daysGrid.Children.Add(new Border());
        }

        var days = HistoricalCalendar.DaysInMonth(year, month, julian);
        for (var day = 1; day <= days; day++)
        {
            var text = DateFormats.Format(year, month, day);
            var button = new Button
            {
                Content = day.ToString(CultureInfo.InvariantCulture),
                Tag = day,
                Padding = new Thickness(0, 3, 0, 3),
                Margin = new Thickness(1),
                ToolTip = text,
            };
            if (day == selectedDay)
            {
                button.FontWeight = FontWeights.Bold;
                button.SetResourceReference(BackgroundProperty, "SelectionBrush");
            }

            AutomationProperties.SetName(button, $"{day} {HistoricalCalendar.MonthNames[month - 1]} {HistoricalYear.Format(year)}");
            var chosen = day;
            button.Click += (_, _) => Pick(chosen, monthOnly: true);
            daysGrid.Children.Add(button);
        }
    }

    private void Pick(int? day, bool monthOnly)
    {
        if (Target is null || !TryReadYear(out var year))
        {
            return;
        }

        int? month = monthOnly || day is not null ? monthBox.SelectedIndex + 1 : null;
        Target.Text = ReplaceFirstHypothesis(Target.Text, DateFormats.Format(year, month, day));
        popup.IsOpen = false;
        Target.Focus();
        Target.CaretIndex = Target.Text.Length;
    }
}
