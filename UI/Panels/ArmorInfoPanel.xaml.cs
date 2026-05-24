using EldenRingArmorStudio.Core;
using Microsoft.Win32;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EldenRingArmorStudio.UI.Panels;

/// <summary>
/// Panel izquierdo: ficha completa de la armadura seleccionada +
/// checkboxes de InvisibleFlags + generador de CSV para Smithbox.
/// </summary>
public partial class ArmorInfoPanel : UserControl
{
    // ── Estado ────────────────────────────────────────────────────────────────
    private ArmorRecord _current;
    private readonly Dictionary<string, CheckBox> _flagChecks = new();

    // ── Eventos ───────────────────────────────────────────────────────────────
    public event Action<string> LoadModelRequested;
    public event Action<List<string>> FlagsChanged;

    // ── Colores / emojis por categoría ────────────────────────────────────────
    private static readonly Dictionary<string, string> CatEmoji = new()
    {
        {"Head","🪖"}, {"Body","🥋"}, {"Arms","🧤"}, {"Legs","👢"}
    };
    private static readonly Dictionary<string, string> CatLabel = new()
    {
        {"Head","🪖 Cabeza"}, {"Body","🥋 Cuerpo"},
        {"Arms","🧤 Brazos"}, {"Legs","👢 Piernas"}
    };
    private static readonly Dictionary<string, Color> CatColor = new()
    {
        {"Head", Color.FromRgb(90,74,173)},  {"Body", Color.FromRgb(42,106,74)},
        {"Arms", Color.FromRgb(122,74,26)},  {"Legs", Color.FromRgb(74,26,106)},
    };

    public ArmorInfoPanel()
    {
        InitializeComponent();
        ShowEmpty();
    }

    // ── API pública ───────────────────────────────────────────────────────────

    public void ShowRecord(ArmorRecord record)
    {
        _current = record;

        if (record == null)
        {
            ImgBanner.Source = null;
            TxtNameEs.Text = "Selecciona una armadura";
            TxtNameEn.Text = "Haz clic en el explorador inferior";
            TxtId.Text = "—";
            TxtCat.Text = "—";
            TxtSet.Text = "—";
            TxtAltered.Text = "—";
            TxtFile.Text = "—";
            BtnLoadModel.IsEnabled = false;
            return;
        }

        TxtNameEs.Text = string.IsNullOrWhiteSpace(record.NameEs) ? record.NameEn : record.NameEs;
        TxtNameEn.Text = record.NameEn;

        var midNum = new string(record.EquipModelId.Where(char.IsDigit).ToArray());
        TxtId.Text = $"{record.EquipModelId}  (#{midNum})";
        TxtCat.Text = CatLabel.GetValueOrDefault(record.Category, record.Category);
        TxtSet.Text = string.IsNullOrWhiteSpace(record.SetName) ? "—" : record.SetName;
        TxtAltered.Text = record.IsAltered ? "Sí ✓" : "No";
        TxtFile.Text = string.IsNullOrWhiteSpace(record.FileName) ? "—" : record.FileName;

        BtnLoadModel.IsEnabled = !string.IsNullOrWhiteSpace(record.FileName);

        RebuildFlagsUI(record.Category);

        // Resolver imagen con la misma lógica centralizada
        string rutaAbsoluta = ResolveIconPath(record.ThumbnailPath, record.EquipModelId);

        // Fallback a IconIdM / IconIdF si ThumbnailPath no funcionó (registros legacy)
        if (rutaAbsoluta == null && record.IconIdM != 0)
        {
            string rel = Path.Combine("data", "icons",
                $"MENU_Knowledge_{record.IconIdM.ToString("D5")}.png");
            rutaAbsoluta = ResolveIconPath(rel, record.EquipModelId);
        }
        if (rutaAbsoluta == null && record.IconIdF != 0)
        {
            string rel = Path.Combine("data", "icons",
                $"MENU_Knowledge_{record.IconIdF.ToString("D5")}.png");
            rutaAbsoluta = ResolveIconPath(rel, record.EquipModelId);
        }

        if (rutaAbsoluta != null)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(rutaAbsoluta);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                ImgBanner.Source = bmp;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Icons] Error cargando banner para {Id} desde {Path}",
                    record.EquipModelId, rutaAbsoluta);
                ImgBanner.Source = MakePlaceholder(record.Category);
            }
        }
        else
        {
            ImgBanner.Source = MakePlaceholder(record.Category);
        }
    }

    public List<string> GetActivePresetKeys() =>
        _flagChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

    // ── Resolución de ruta de icono ───────────────────────────────────────────

    /// <summary>
    /// Intenta resolver la ruta absoluta a partir de una ruta relativa guardada en BD.
    /// Busca primero junto al ejecutable, luego en la raíz del proyecto fuente.
    /// Devuelve null y emite un warning en el log si no la encuentra.
    /// </summary>
    private static string ResolveIconPath(string relativePath, string equipModelId)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        // 1. Junto al ejecutable
        string rutaEjecutable = Path.GetFullPath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath));

        if (File.Exists(rutaEjecutable))
            return rutaEjecutable;

        // 2. Raíz del proyecto fuente (3 niveles arriba)
        string raizProyecto = Path.GetFullPath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\"));
        string rutaProyecto = Path.GetFullPath(Path.Combine(raizProyecto, relativePath));

        if (File.Exists(rutaProyecto))
            return rutaProyecto;

        Log.Warning("[Icons] Imagen no encontrada para {Id} | Ruta relativa: {Rel} | Buscado en: {P1} y {P2}",
            equipModelId, relativePath, rutaEjecutable, rutaProyecto);

        return null;
    }

    // ── InvisibleFlags UI ─────────────────────────────────────────────────────

    private void RebuildFlagsUI(string category)
    {
        FlagsPanel.Children.Clear();
        _flagChecks.Clear();

        var presets = InvisibleFlags.ForCategory(category);
        if (presets.Count == 0)
        {
            FlagsPanel.Children.Add(new TextBlock
            {
                Text = "Sin presets para esta categoría.",
                Foreground = Brushes.DimGray,
                FontSize = 10
            });
            BtnCsv.IsEnabled = false;
            return;
        }

        FlagsPanel.Children.Add(new TextBlock
        {
            Text = "✅ Marca los flags a aplicar al duplicar.\nSe generará CSV para Smithbox.",
            Foreground = new SolidColorBrush(Colors.Black),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        foreach (var preset in presets)
        {
            var chk = new CheckBox
            {
                Content = preset.Label,
                ToolTip = preset.Description,
                Foreground = new SolidColorBrush(Colors.Black),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2),
            };
            chk.Checked += OnFlagChanged;
            chk.Unchecked += OnFlagChanged;
            _flagChecks[preset.Key] = chk;
            FlagsPanel.Children.Add(chk);
        }

        BtnCsv.IsEnabled = true;
    }

    private void OnFlagChanged(object sender, RoutedEventArgs e) =>
        FlagsChanged?.Invoke(GetActivePresetKeys());

    // ── Handlers ──────────────────────────────────────────────────────────────

    private void OnLoadModelClick(object sender, RoutedEventArgs e)
    {
        if (_current is not null && !string.IsNullOrWhiteSpace(_current.FileName))
            LoadModelRequested?.Invoke(_current.FileName);
    }

    private void OnGenerateCsvClick(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            MessageBox.Show("Selecciona una armadura primero.", "Sin selección",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var activeKeys = GetActivePresetKeys();
        if (activeKeys.Count == 0)
        {
            MessageBox.Show("Marca al menos un preset de flags.", "Sin flags",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var combinedFlags = new HashSet<int>();
        var labels = new List<string>();
        foreach (var key in activeKeys)
        {
            var preset = InvisibleFlags.Presets.FirstOrDefault(p => p.Key == key);
            if (preset is null) continue;
            foreach (var f in preset.Flags) combinedFlags.Add(f);
            labels.Add(preset.Label);
        }

        var dlg = new SaveFileDialog
        {
            Title = "Guardar CSV de flags",
            FileName = $"flags_{_current.EquipModelId}.csv",
            DefaultExt = ".csv",
            Filter = "CSV (*.csv)|*.csv",
            InitialDirectory = Path.GetFullPath("data"),
        };
        if (dlg.ShowDialog() != true) return;

        var gameCsv = Path.GetFullPath("data/EquipParamProtector.csv");
        var midNum = new string(_current.EquipModelId.Where(char.IsDigit).ToArray());
        var paramIds = File.Exists(gameCsv)
            ? InvisibleFlags.GetParamIdsForModelId(midNum, gameCsv)
            : new List<string>();

        if (paramIds.Count == 0)
        {
            var noGame = !File.Exists(gameCsv)
                ? "\n\nNo se encontró data/EquipParamProtector.csv\nExportarlo desde Smithbox."
                : $"\n\nNo se encontraron IDs para ModelId={midNum}.";
            MessageBox.Show("No se pudo determinar el param ID." + noGame,
                "Sin IDs", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entries = paramIds.Select(pid =>
            (pid, (IEnumerable<int>)combinedFlags.ToList()));

        bool ok = InvisibleFlags.GenerateSmithboxCsv(dlg.FileName, entries);

        if (ok)
        {
            var flagList = string.Join(", ", combinedFlags.OrderBy(x => x)
                .Select(n => $"SexVer{n:D2}"));
            var msg =
                $"✅ CSV guardado en:\n{dlg.FileName}\n\n" +
                $"Presets aplicados:\n{string.Join("\n", labels.Select(l => "  • " + l))}\n\n" +
                $"Flags: {flagList}\n\n" +
                $"IDs de param escritos: {paramIds.Count}\n" +
                string.Join("\n", paramIds.Take(10)) +
                (paramIds.Count > 10 ? "\n..." : "") +
                "\n\n📌 Importar en Smithbox:\n" +
                "Param Editor → EquipParamProtector → toolbar → Import CSV";
            MessageBox.Show(msg, "CSV generado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Error al generar el CSV.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Estado vacío ──────────────────────────────────────────────────────────

    private void ShowEmpty()
    {
        TxtNameEs.Text = "Selecciona una armadura";
        TxtNameEn.Text = "Haz clic en el explorador inferior";
        foreach (var t in new[] { TxtId, TxtCat, TxtSet, TxtAltered, TxtGender, TxtFile })
            t.Text = "—";
        BtnLoadModel.IsEnabled = false;
        BtnCsv.IsEnabled = false;
        ImgBanner.Source = MakePlaceholder("Head");
    }

    // ── Placeholder visual ────────────────────────────────────────────────────

    private static BitmapSource MakePlaceholder(string category)
    {
        var col = CatColor.GetValueOrDefault(category, Color.FromRgb(42, 42, 58));
        var emoji = CatEmoji.GetValueOrDefault(category, "📦");

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bg = new SolidColorBrush(col);
            dc.DrawRectangle(bg, null, new Rect(0, 0, 240, 120));
            var ft = new FormattedText(emoji,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Emoji"),
                48, Brushes.White,
                VisualTreeHelper.GetDpi(dv).PixelsPerDip);
            dc.DrawText(ft, new Point(240 / 2 - ft.Width / 2, 120 / 2 - ft.Height / 2));
        }
        var rtb = new RenderTargetBitmap(240, 120, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }
}