using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace HistoricEditor;

// Note formattate: il testo è salvato come XAML di un FlowDocument (Section con Paragraph, List, Run…).
// Le note scritte prima della formattazione sono testo semplice e restano valide.
public static class RichNotes
{
    private const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    // Elementi ammessi: solo testo e formattazione, nessun controllo o oggetto arbitrario.
    private static readonly HashSet<string> AllowedElements = new(StringComparer.Ordinal)
    {
        "Section", "Paragraph", "Run", "Span", "Bold", "Italic", "Underline", "LineBreak", "List", "ListItem",
        "Hyperlink", "Table", "TableColumn", "TableRowGroup", "TableRow", "TableCell",
        "TextDecorationCollection", "TextDecoration", "SolidColorBrush",
    };

    public static bool IsRich(string? note)
        => note is not null && note.TrimStart().StartsWith("<Section", StringComparison.Ordinal);

    // Vero se il testo è XAML formattato e contiene solo elementi ammessi: solo allora può essere caricato.
    public static bool IsSafe(string? note)
    {
        if (!IsRich(note))
        {
            return false;
        }

        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(note!), settings);
            var document = XDocument.Load(reader);
            foreach (var element in document.Descendants())
            {
                if (element.Name.NamespaceName != PresentationNamespace)
                {
                    return false;
                }

                var local = element.Name.LocalName;
                var owner = local.Contains('.') ? local[..local.IndexOf('.')] : local;
                if (!AllowedElements.Contains(owner))
                {
                    return false;
                }

                foreach (var attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                    {
                        continue;
                    }

                    // Niente estensioni di markup ({Binding}, {x:Static}…) né attributi x: (x:Class, x:Code…).
                    if (attribute.Value.TrimStart().StartsWith('{')
                        || (attribute.Name.NamespaceName.Length > 0 && attribute.Name.NamespaceName != XNamespace.Xml.NamespaceName))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    // Testo semplice per ricerca, elenchi e lettori di schermo: un paragrafo per riga, i punti elenco come «• ».
    public static string ToPlainText(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return string.Empty;
        }

        if (!IsRich(note))
        {
            return note.Trim();
        }

        try
        {
            var root = XDocument.Parse(note).Root;
            if (root is null)
            {
                return string.Empty;
            }

            var lines = new List<string>();
            CollectBlocks(root, lines, listDepth: 0);
            return string.Join(Environment.NewLine, lines).Trim();
        }
        catch (XmlException)
        {
            return note.Trim();
        }
    }

    // XAML minimo equivalente a un testo semplice (un paragrafo per riga), per i test e le conversioni.
    public static string FromPlainText(string? text)
    {
        var section = new XElement(XName.Get("Section", PresentationNamespace));
        foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            section.Add(new XElement(XName.Get("Paragraph", PresentationNamespace), new XElement(XName.Get("Run", PresentationNamespace), line)));
        }

        return section.ToString(SaveOptions.DisableFormatting);
    }

    public static bool IsEmpty(string? note) => ToPlainText(note).Trim().Length == 0;

    // Toglie colori e carattere generale del documento salvato dall'editor: le note seguono il tema e il carattere
    // dell'applicazione. Restano grassetto, corsivo, sottolineato, elenchi e dimensione dei titoli.
    public static string Normalize(string? note)
    {
        if (!IsRich(note))
        {
            return note ?? string.Empty;
        }

        try
        {
            var document = XDocument.Parse(note!);
            if (document.Root is not { } root)
            {
                return note!;
            }

            foreach (var element in root.DescendantsAndSelf())
            {
                element.Attributes().Where(a => a.Name.LocalName is "Foreground" or "Background" && a.Name.NamespaceName.Length == 0).Remove();
            }

            root.Attributes().Where(a => a.Name.LocalName is "FontFamily" or "FontSize" or "FontStyle" or "FontWeight" or "FontStretch" or "lang" or "TextAlignment" or "FlowDirection").Remove();
            return root.ToString(SaveOptions.DisableFormatting);
        }
        catch (XmlException)
        {
            return note!;
        }
    }

    private static void CollectBlocks(XElement element, List<string> lines, int listDepth)
    {
        var local = element.Name.LocalName;
        if (local.Contains('.'))
        {
            return;
        }

        if (local == "List")
        {
            var marker = (string?)element.Attribute("MarkerStyle");
            var numbered = marker is "Decimal" or "LowerLatin" or "UpperLatin" or "LowerRoman" or "UpperRoman";
            var index = 1;
            foreach (var item in element.Elements().Where(e => e.Name.LocalName == "ListItem"))
            {
                var itemLines = new List<string>();
                CollectBlocks(item, itemLines, listDepth + 1);
                var prefix = new string(' ', listDepth * 2) + (numbered ? $"{index}. " : "• ");
                for (var i = 0; i < itemLines.Count; i++)
                {
                    lines.Add(i == 0 ? prefix + itemLines[i].TrimStart() : itemLines[i]);
                }

                index++;
            }

            return;
        }

        if (local == "Paragraph")
        {
            lines.Add(InlineText(element));
            return;
        }

        foreach (var child in element.Elements())
        {
            CollectBlocks(child, lines, listDepth);
        }
    }

    private static string InlineText(XElement element)
    {
        var builder = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;
                case XElement child when child.Name.LocalName == "LineBreak":
                    builder.Append(' ');
                    break;
                case XElement child when child.Name.LocalName.Contains('.'):
                    break;
                case XElement child:
                    builder.Append((string?)child.Attribute("Text") ?? string.Empty);
                    builder.Append(InlineText(child));
                    break;
            }
        }

        return builder.ToString();
    }
}
