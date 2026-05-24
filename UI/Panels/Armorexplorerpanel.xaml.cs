using EldenRingArmorStudio.Core;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EldenRingArmorStudio.UI.Panels;

/// <summary>
/// Item con carga LAZY de imagen: solo decodifica el PNG cuando WPF
/// accede a Thumbnail por primera vez (el item entra en el viewport).
/// </summary>
public class ArmorGridItem : INotifyPropertyChanged
{
    private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string ProjectDir = Path.GetFullPath(
        Path.Combine(BaseDir, @"..\..\..\"));

    public ArmorRecord Record { get; init; } = null!;
    public string Label { get; init; } = "";

    // Ruta absoluta resuelta una sola vez en el constructor
    private readonly string _resolvedPath;

    // Placeholder estático compartido por todos los items sin imagen
    private static BitmapSource _sharedPlaceholder;

    private BitmapSource _thumbnail;
    private bool _loaded;

    public int IconIdM => Record?.IconIdM ?? 0;

    public BitmapSource Thumbnail
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                // Carga en background para no bloquear el scroll
                Task.Run(() => LoadImage()).ContinueWith(t =>
                {
                    if (t.Result != null)
                    {
                        _thumbnail = t.Result;
                        OnPropertyChanged();
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            return _thumbnail ?? GetPlaceholder(Record?.Category);
        }
    }

    public ArmorGridItem(string resolvedPath)
    {
        _resolvedPath = resolvedPath;
    }

    private BitmapSource LoadImage()
    {
        if (string.IsNullOrEmpty(_resolvedPath)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(_resolvedPath);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 64; // solo 64px en memoria, no el PNG completo
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Icons] Error cargando {Path}", _resolvedPath);
            return null;
        }
    }

    // Placeholder por categoría, creado una sola vez y compartido
    private static readonly Dictionary<string, BitmapSource> PlaceholderCache = new();
    private static readonly Dictionary<string, Color> CatColor = new()
    {
        {"Head", Color.FromRgb(90,74,173)}, {"Body", Color.FromRgb(42,106,74)},
        {"Arms", Color.FromRgb(122,74,26)}, {"Legs", Color.FromRgb(74,26,106)},
    };
    private static readonly Dictionary<string, string> CatEmoji = new()
    {
        {"Head","🪖"},{"Body","🥋"},{"Arms","🧤"},{"Legs","👢"}
    };

    private static BitmapSource GetPlaceholder(string category)
    {
        category ??= "Head";
        if (PlaceholderCache.TryGetValue(category, out var cached)) return cached;
        var ph = ArmorExplorerPanel.MakePlaceholder(category, 64);
        PlaceholderCache[category] = ph;
        return ph;
    }

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Panel inferior con grid virtualizado de miniaturas de armaduras.
/// </summary>
public partial class ArmorExplorerPanel : UserControl
{
    private ArmorDatabase _db;
    private readonly DispatcherTimer _searchTimer;
    private readonly ObservableCollection<ArmorGridItem> _items = new();

    private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string ProjectDir = Path.GetFullPath(
        Path.Combine(BaseDir, @"..\..\..\"));

    private static readonly Dictionary<string, string> CatEmoji = new()
    {
        {"Head","🪖"},{"Body","🥋"},{"Arms","🧤"},{"Legs","👢"},{"Todos","🗂"}
    };
    internal static readonly Dictionary<string, Color> CatColor = new()
    {
        {"Head",Color.FromRgb(90,74,173)}, {"Body",Color.FromRgb(42,106,74)},
        {"Arms",Color.FromRgb(122,74,26)}, {"Legs",Color.FromRgb(74,26,106)},
    };

    public event Action<ArmorRecord> ArmorClicked;
    public event Action<string> ModelDoubleClicked;

    public ArmorExplorerPanel()
    {
        InitializeComponent();

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); PerformSearch(); };

        ArmorGrid.ItemsSource = _items;

        TxtSearch.GotFocus += (_, _) => { if (TxtSearch.Text == (string)TxtSearch.Tag) TxtSearch.Text = ""; };
        TxtSearch.LostFocus += (_, _) => { if (string.IsNullOrEmpty(TxtSearch.Text)) TxtSearch.Text = (string)TxtSearch.Tag; };
        TxtSearch.Text = (string)TxtSearch.Tag;
        TxtSearch.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        TxtSearch.GotFocus += (_, _) => TxtSearch.Foreground = new SolidColorBrush(Colors.Black);
        TxtSearch.LostFocus += (_, _) =>
        {
            if (TxtSearch.Text == (string)TxtSearch.Tag)
                TxtSearch.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        };
    }

    public void Initialize(ArmorDatabase db)
    {
        _db = db;
        ComboCat.Items.Clear();
        foreach (var cat in new[] { "Todos", "Head", "Body", "Arms", "Legs" })
            ComboCat.Items.Add($"{CatEmoji.GetValueOrDefault(cat, "")} {cat}");
        ComboCat.SelectedIndex = 0;
        PerformSearch();
    }

    public void Refresh() => PerformSearch();

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtSearch.Text == (string)TxtSearch.Tag) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e) => PerformSearch();
    private void OnFilterChanged(object sender, RoutedEventArgs e) => PerformSearch();

    /// <summary>
    /// Resuelve la ruta absoluta desde una ruta relativa guardada en BD.
    /// Busca junto al ejecutable y luego en la raíz del proyecto fuente.
    /// </summary>
    private static string ResolveIconPath(string relativePath, string equipModelId)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        string p1 = Path.GetFullPath(Path.Combine(BaseDir, relativePath));
        if (File.Exists(p1)) return p1;

        string p2 = Path.GetFullPath(Path.Combine(ProjectDir, relativePath));
        if (File.Exists(p2)) return p2;

        Log.Warning("[Icons] No encontrada para {Id} | {Rel} | buscado en {P1} y {P2}",
            equipModelId, relativePath, p1, p2);
        return null;
    }

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

        var results = _db.SearchArmor(query, cat);

        _items.Clear();
        foreach (var r in results)
        {
            var name = !string.IsNullOrEmpty(r.NameEs) ? r.NameEs : r.NameEn;
            var midNum = new string(r.EquipModelId.Where(char.IsDigit).ToArray());
            var label = $"{(name.Length > 18 ? name[..18] : name)}\n#{midNum}";

            // Resolver ruta una sola vez aquí (barato: solo string ops, sin I/O)
            string resolvedPath = ResolveIconPath(r.ThumbnailPath, r.EquipModelId);

            // Fallback a IconIdM / IconIdF si ThumbnailPath vacío o legacy
            if (resolvedPath == null && r.IconIdM != 0)
                resolvedPath = ResolveIconPath(
                    Path.Combine("data", "icons", $"MENU_Knowledge_{r.IconIdM:D5}.png"),
                    r.EquipModelId);
            if (resolvedPath == null && r.IconIdF != 0)
                resolvedPath = ResolveIconPath(
                    Path.Combine("data", "icons", $"MENU_Knowledge_{r.IconIdF:D5}.png"),
                    r.EquipModelId);

            _items.Add(new ArmorGridItem(resolvedPath)
            {
                Record = r,
                Label = label
            });
        }

        TxtCount.Text = $"{results.Count:N0} resultados";
    }

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