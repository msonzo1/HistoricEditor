using System.Windows.Controls;

namespace HistoricEditor;

// Messaggi di stato: il colore distingue esito positivo, errore e semplice informazione.
internal static class Feedback
{
    public static void Success(TextBlock target, string text) => Show(target, text, "GreenBrush");

    public static void Error(TextBlock target, string text) => Show(target, text, "DangerBrush");

    public static void Info(TextBlock target, string text) => Show(target, text, "QuietBrush");

    private static void Show(TextBlock target, string text, string brushKey)
    {
        target.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        target.Text = text;
    }
}
