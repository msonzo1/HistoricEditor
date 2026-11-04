using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace HistoricEditor;

// Editor delle note formattate (grassetto, corsivo, sottolineato, titoli, elenchi). In sola lettura diventa
// il visualizzatore delle schede. Il testo è il XAML descritto in RichNotes; le vecchie note in testo semplice si aprono comunque.
public partial class RichNoteEditor : UserControl
{
    private const double HeadingSize = 17;
    private bool loading;

    public RichNoteEditor()
    {
        InitializeComponent();
        Box.Document = NewDocument();
    }

    public event EventHandler? Changed;

    public string AccessibleName
    {
        get => AutomationProperties.GetName(Box);
        set => AutomationProperties.SetName(Box, value);
    }

    public double EditorHeight
    {
        get => Box.Height;
        set => Box.Height = value;
    }

    public bool IsReadOnly
    {
        get => Box.IsReadOnly;
        set
        {
            Box.IsReadOnly = value;
            Box.IsDocumentEnabled = value;
            Toolbar.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            if (value)
            {
                Box.Background = Brushes.Transparent;
                Box.BorderThickness = new Thickness(0);
                Box.Padding = new Thickness(0);
                Box.IsReadOnlyCaretVisible = false;
                Box.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                SpellCheck.SetIsEnabled(Box, false);
            }
        }
    }

    public string Text
    {
        get => Serialize(Box.Document);
        set
        {
            loading = true;
            try
            {
                Box.Document = Load(value);
            }
            finally
            {
                loading = false;
            }
        }
    }

    public string PlainText => new TextRange(Box.Document.ContentStart, Box.Document.ContentEnd).Text.Trim();

    // Aggiunge testo semplice in fondo alla nota (es. note importate da un museo).
    public void AppendPlainText(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            Box.Document.Blocks.Add(new Paragraph(new Run(line)));
        }
    }

    public static FlowDocument Load(string? note)
    {
        var document = NewDocument();
        if (RichNotes.IsSafe(note))
        {
            try
            {
                var range = new TextRange(document.ContentStart, document.ContentEnd);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(RichNotes.Normalize(note)));
                range.Load(stream, DataFormats.Xaml);
                return document;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Xaml.XamlException or System.Windows.Markup.XamlParseException)
            {
                document = NewDocument();
                note = RichNotes.ToPlainText(note);
            }
        }
        else if (RichNotes.IsRich(note))
        {
            note = RichNotes.ToPlainText(note);
        }

        foreach (var line in (note ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            document.Blocks.Add(new Paragraph(new Run(line)));
        }

        return document;
    }

    public static string Serialize(FlowDocument document)
    {
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        if (range.Text.Trim().Length == 0)
        {
            return string.Empty;
        }

        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Xaml);
        return RichNotes.Normalize(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static FlowDocument NewDocument() => new() { PagePadding = new Thickness(0) };

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (ReferenceEquals(e.NewFocus, this))
        {
            Box.Focus();
        }
    }

    private void Box_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!loading)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Execute(RoutedUICommand command)
    {
        command.Execute(null, Box);
        Box.Focus();
    }

    private void Bold_Click(object sender, RoutedEventArgs e) => Execute(EditingCommands.ToggleBold);

    private void Italic_Click(object sender, RoutedEventArgs e) => Execute(EditingCommands.ToggleItalic);

    private void Underline_Click(object sender, RoutedEventArgs e) => Execute(EditingCommands.ToggleUnderline);

    private void Bullets_Click(object sender, RoutedEventArgs e) => Execute(EditingCommands.ToggleBullets);

    private void Numbering_Click(object sender, RoutedEventArgs e) => Execute(EditingCommands.ToggleNumbering);

    private void Heading_Click(object sender, RoutedEventArgs e) => ApplyToParagraphs(p =>
    {
        p.FontSize = HeadingSize;
        p.FontWeight = FontWeights.SemiBold;
    });

    private void Normal_Click(object sender, RoutedEventArgs e) => ApplyToParagraphs(p =>
    {
        p.ClearValue(TextElement.FontSizeProperty);
        p.ClearValue(TextElement.FontWeightProperty);
    });

    private void ApplyToParagraphs(Action<Paragraph> apply)
    {
        var seen = new HashSet<Paragraph>();
        var end = Box.Selection.End;
        for (var pointer = Box.Selection.Start; pointer is not null && pointer.CompareTo(end) <= 0; pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (pointer.Paragraph is { } paragraph && seen.Add(paragraph))
            {
                apply(paragraph);
            }
        }

        if (end.Paragraph is { } last && seen.Add(last))
        {
            apply(last);
        }

        Box.Focus();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
