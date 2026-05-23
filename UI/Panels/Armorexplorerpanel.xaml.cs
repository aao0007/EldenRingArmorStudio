using EldenRingArmorStudio.Core;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Linq;

namespace EldenRingArmorStudio.UI.Panels;

/// <summary>
/// Item de datos para el grid de miniaturas (binding).
/// </summary>
public class ArmorGridItem
{
    public ArmorRecord Record { get; init; } = null!;
    public string Label { get; init; } = "";
    public BitmapSource Thumbnail { get; init; } = null!;

    // 🚀 Añadimos esto para que el XAML lo encuentre al instante
    public int IconIdM => Record != null ? Record.IconId ?? 0 : 0;
}

/// <summary>
/// Panel inferior con grid de miniaturas de armaduras.
/// Un clic → ArmorClicked (para el panel de info).
/// Doble clic → ModelDoubleClicked (para cargar en visor).
/// </summary>
public partial class ArmorExplorerPanel : UserControl
{
    // ── Estado ────────────────────────────────────────────────────────────────
    private ArmorDatabase _db;
    private readonly DispatcherTimer _searchTimer;
    private readonly ObservableCollection<ArmorGridItem> _items = new();

    private static readonly Dictionary<string, string> CatEmoji = new()
    {
        {"Head","🪖"},{"Body","🥋"},{"Arms","🧤"},{"Legs","👢"},{"Todos","🗂"}
    };
    private static readonly Dictionary<string, Color> CatColor = new()
    {
        {"Head",Color.FromRgb(90,74,173)}, {"Body",Color.FromRgb(42,106,74)},
        {"Arms",Color.FromRgb(122,74,26)}, {"Legs",Color.FromRgb(74,26,106)},
    };

    // ── Eventos ───────────────────────────────────────────────────────────────
    public event Action<ArmorRecord> ArmorClicked;
    public event Action<string> ModelDoubleClicked;

    public ArmorExplorerPanel()
    {
        InitializeComponent();

        // Timer debounce búsqueda
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); PerformSearch(); };

        ArmorGrid.ItemsSource = _items;

        // Placeholder en TextBox
        TxtSearch.GotFocus += (_, _) => { if (TxtSearch.Text == (string)TxtSearch.Tag) TxtSearch.Text = ""; };
        TxtSearch.LostFocus += (_, _) => { if (string.IsNullOrEmpty(TxtSearch.Text)) TxtSearch.Text = (string)TxtSearch.Tag; };
        TxtSearch.Text = (string)TxtSearch.Tag;

        // Color del texto gris al perder el foco (Placeholder)
        TxtSearch.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        // Color del texto NEGRO al escribir
        TxtSearch.GotFocus += (_, _) => TxtSearch.Foreground = new SolidColorBrush(Colors.Black);
        TxtSearch.LostFocus += (_, _) => { if (TxtSearch.Text == (string)TxtSearch.Tag) TxtSearch.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)); };
    }

    // ── Inicialización ────────────────────────────────────────────────────────

    public void Initialize(ArmorDatabase db)
    {
        _db = db;

        // Categorías
        ComboCat.Items.Clear();
        foreach (var cat in new[] { "Todos", "Head", "Body", "Arms", "Legs" })
            ComboCat.Items.Add($"{CatEmoji.GetValueOrDefault(cat, "")} {cat}");
        ComboCat.SelectedIndex = 0;

        PerformSearch();
    }

    public void Refresh() => PerformSearch();

    // ── Búsqueda ──────────────────────────────────────────────────────────────

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtSearch.Text == (string)TxtSearch.Tag) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e) => PerformSearch();
    private void OnFilterChanged(object sender, RoutedEventArgs e) => PerformSearch();

    private void PerformSearch()
    {
        if (_db is null) return;

        var query = TxtSearch.Text.Trim();
        if (query == (string)TxtSearch.Tag) query = "";

        var catItem = ComboCat.SelectedItem?.ToString() ?? "";
        var cat = catItem.Contains("Head") ? "Head"
                : catItem.Contains("Body") ? "Body"
                : catItem.Contains("Arms") ? "Arms"
                : catItem.Contains("Legs") ? "Legs"
                : null;

        var altOnly = ChkAltered.IsChecked == true;

        // Ejecutar la búsqueda en la base de datos de SQLite
        var results = _db.Search(query, cat, altOnly);

        _items.Clear();
        foreach (var r in results)
        {
            // Priorizar el nombre en español si existe, si no usa el inglés
            var name = !string.IsNullOrEmpty(r.NameEs) ? r.NameEs : r.NameEn;
            var midNum = new string(r.EquipModelId.Where(char.IsDigit).ToArray());
            var label = $"{(name.Length > 18 ? name[..18] : name)}\n#{midNum}";

            BitmapSource thumb = null;

            // 🚀 RESOLUCIÓN DE RUTA INTELIGENTE PARA LAS IMÁGENES PNG
            if (!string.IsNullOrEmpty(r.ThumbnailPath))
            {
                // 1. Intentar buscar la imagen en la carpeta de ejecución (bin/Debug/net8.0-windows/data/icons...)
                string rutaAbsoluta = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, r.ThumbnailPath);

                // 2. Si no existe ahí, subir 3 niveles en el árbol de directorios hacia la raíz del código fuente (.sln)
                if (!File.Exists(rutaAbsoluta))
                {
                    string raizProyecto = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\"));
                    rutaAbsoluta = Path.Combine(raizProyecto, r.ThumbnailPath);
                }

                // 3. Si el archivo físico real existe en alguna de las dos rutas, lo cargamos de forma segura
                if (File.Exists(rutaAbsoluta))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(rutaAbsoluta);
                        bmp.CacheOption = BitmapCacheOption.OnLoad; // Forzar carga en memoria RAM inmediata
                        bmp.EndInit();
                        bmp.Freeze(); // Desvincular del hilo principal para máxima fluidez en la UI
                        thumb = bmp;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Error cargando imagen {r.ThumbnailPath}: {ex.Message}");
                        thumb = null;
                    }
                }
            }

            // Si la imagen no existía en el disco o falló su lectura, usamos el placeholder dinámico
            if (thumb == null)
            {
                thumb = MakePlaceholder(r.Category, 96);
            }

            // Añadir el item procesado a la colección observable vinculada al Grid del XAML
            _items.Add(new ArmorGridItem { Record = r, Label = label, Thumbnail = thumb });
        }

        TxtCount.Text = $"{results.Count:N0} resultados";
    }

    // ── Eventos grid ──────────────────────────────────────────────────────────

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArmorGrid.SelectedItem is ArmorGridItem item)
            ArmorClicked?.Invoke(item.Record);
    }

    private void OnDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ArmorGrid.SelectedItem is ArmorGridItem item
            && !string.IsNullOrWhiteSpace(item.Record.FileName))
            ModelDoubleClicked?.Invoke(item.Record.FileName);
    }

    // ── Placeholder ───────────────────────────────────────────────────────────

    internal static BitmapSource MakePlaceholder(string category, int size)
    {
        var col = CatColor.GetValueOrDefault(category, Color.FromRgb(42, 42, 58));
        var emoji = CatEmoji.GetValueOrDefault(category, "📦");

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(col), null, new Rect(0, 0, size, size));
            var ft = new FormattedText(emoji,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Emoji"),
                size / 2.8,
                Brushes.White,
                VisualTreeHelper.GetDpi(dv).PixelsPerDip);
            dc.DrawText(ft, new Point(size / 2.0 - ft.Width / 2, size / 2.0 - ft.Height / 2));
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}