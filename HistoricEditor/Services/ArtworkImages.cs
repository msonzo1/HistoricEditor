using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HistoricEditor;

// Immagini delle opere: l'originale non viene mai modificato; nell'archivio si salva una copia JPEG
// ridimensionata (lato massimo MaxSide) e una miniatura, che si apre subito nelle finestre.
public static partial class ArtworkImages
{
    public const int MaxSide = 2000;
    public const int ThumbnailSide = 480;
    public const int Quality = 85;

    public const string FileFilter = "Immagini|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.jfif|Tutti i file|*.*";

    [GeneratedRegex("^[0-9a-f]{32}(\\.thumb)?\\.jpg$", RegexOptions.IgnoreCase)]
    private static partial Regex OwnFileName();

    public static string FullPath(string directory, string name) => Path.Combine(directory, name);

    public static string ThumbnailPath(string directory, string name)
        => Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + ".thumb.jpg");

    // Dimensioni che stanno in un quadrato di lato max mantenendo le proporzioni; le immagini piccole non si ingrandiscono.
    public static (int Width, int Height) Fit(int width, int height, int max)
    {
        if (width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        if (width <= max && height <= max)
        {
            return (width, height);
        }

        var scale = (double)max / Math.Max(width, height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    // Comprime l'immagine nella cartella e restituisce il nome del nuovo file.
    public static string Import(string sourcePath, string directory)
    {
        BitmapSource image;
        try
        {
            image = Decode(sourcePath, MaxSide);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException("Il file scelto non è un'immagine leggibile (formati: JPEG, PNG, BMP, GIF, TIFF, WebP se supportato da Windows).", ex);
        }

        Directory.CreateDirectory(directory);
        var name = $"{Guid.NewGuid():N}.jpg";
        var full = FullPath(directory, name);
        var thumb = ThumbnailPath(directory, name);
        try
        {
            var flattened = Flatten(image, MaxSide);
            Write(flattened, full);
            Write(Flatten(flattened, ThumbnailSide), thumb);
        }
        catch
        {
            Delete(directory, name);
            throw;
        }

        return name;
    }

    public static void Delete(string directory, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !OwnFileName().IsMatch(name))
        {
            return;
        }

        foreach (var path in new[] { FullPath(directory, name), ThumbnailPath(directory, name) })
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File in uso: verrà rimosso dalla pulizia successiva.
            }
        }
    }

    // Elimina le immagini non più usate da nessuna opera (es. scelte e poi non salvate). Tocca solo i file creati da Import.
    public static int RemoveOrphans(string directory, IEnumerable<string> used)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var keep = new HashSet<string>(used.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => Path.GetFileNameWithoutExtension(u.Trim())), StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.jpg"))
        {
            var file = Path.GetFileName(path);
            if (!OwnFileName().IsMatch(file) || keep.Contains(file[..32]))
            {
                continue;
            }

            try
            {
                File.Delete(path);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    // Carica la miniatura (o, se manca, l'immagine intera ridotta) senza tenere il file aperto.
    public static BitmapSource? LoadThumbnail(string directory, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var thumb = ThumbnailPath(directory, name);
        var path = File.Exists(thumb) ? thumb : FullPath(directory, name);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Decode(path, ThumbnailSide);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    // Decodifica già ridotta (più veloce e con meno memoria), ruotata secondo l'orientamento EXIF delle foto.
    private static BitmapSource Decode(string path, int max)
    {
        int width, height;
        var rotation = Rotation.Rotate0;
        using (var stream = File.OpenRead(path))
        {
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            width = frame.PixelWidth;
            height = frame.PixelHeight;
            rotation = ReadRotation(frame);
        }

        var target = Fit(width, height, max);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(Path.GetFullPath(path));
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
        if (target.Width < width)
        {
            bitmap.DecodePixelWidth = target.Width;
        }

        bitmap.Rotation = rotation;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static Rotation ReadRotation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("System.Photo.Orientation")
                && metadata.GetQuery("System.Photo.Orientation") is ushort orientation)
            {
                return orientation switch
                {
                    3 => Rotation.Rotate180,
                    6 => Rotation.Rotate90,
                    8 => Rotation.Rotate270,
                    _ => Rotation.Rotate0,
                };
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
        }

        return Rotation.Rotate0;
    }

    // Ridimensiona e appoggia l'immagine su sfondo bianco: il JPEG non ha trasparenza.
    private static BitmapSource Flatten(BitmapSource source, int max)
    {
        var (width, height) = Fit(source.PixelWidth, source.PixelHeight, max);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, width, height);
            context.DrawRectangle(Brushes.White, null, rect);
            context.DrawImage(source, rect);
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgr24, null, 0);
        converted.Freeze();
        return converted;
    }

    private static void Write(BitmapSource image, string path)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = Quality };
        encoder.Frames.Add(BitmapFrame.Create(image));
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            encoder.Save(stream);
        }

        File.Move(temp, path, overwrite: true);
    }
}
