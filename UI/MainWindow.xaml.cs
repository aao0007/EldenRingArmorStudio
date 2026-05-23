using EldenRingArmorStudio.Core;
using Serilog;
using SoulsFormats;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using EldenRingArmorStudio.UI.Viewer3D;
namespace EldenRingArmorStudio.UI
{
    public partial class MainWindow : Window
    {
        private ArmorDatabase? _db;
        private string _configuredPartsDir = "";
        private const string IconsDir = "data/icons";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _db = new ArmorDatabase();
                Log.Information("Base de datos SQLite cargada. Registros: {Count}", _db.Count());

                // Ruta por defecto inicial
                _configuredPartsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "parts");
                TxtPartsDirectory.Text = _configuredPartsDir;

                CargarBaseDeDatos("");
                CargarListaModelosFisicos();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error crítico durante la inicialización de la ventana principal");
            }
        }

        private void BtnBrowseDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona la carpeta 'parts' de Elden Ring",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                _configuredPartsDir = dialog.FolderName;
                TxtPartsDirectory.Text = _configuredPartsDir;
                CargarListaModelosFisicos();
                Log.Information("Directorio de modelos configurado en: {Path}", _configuredPartsDir);
            }
        }

        private void CargarListaModelosFisicos()
        {
            ModelFilesListBox.Items.Clear();

            if (!Directory.Exists(_configuredPartsDir))
            {
                ModelFilesListBox.Items.Add("Directorio no válido o ausente");
                return;
            }

            try
            {
                var archivos = Directory.GetFiles(_configuredPartsDir, "*.partsbnd.dcx")
                                        .Select(Path.GetFileName);

                foreach (var archivo in archivos)
                {
                    if (archivo != null) ModelFilesListBox.Items.Add(archivo);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error leyendo archivos en el directorio configurado");
            }
        }

        private void CargarBaseDeDatos(string query)
        {
            if (_db is null) return;

            var items = _db.Search(query).Select(r => new ArmorItem
            {
                EquipModelId = r.EquipModelId,
                NameEn = r.NameEn,
                NameEs = r.NameEs,
                Category = r.Category,
                SetName = r.SetName,
                IsAltered = r.IsAltered,
                IconId = r.IconId,
                Weight = r.Weight,
                DefensePhys = r.DefensePhys,
                DefenseMagic = r.DefenseMagic,
                DefenseFire = r.DefenseFire,
                DefenseLightning = r.DefenseLightning,
                Poise = r.Poise
            }).ToList();

            ArmorGrid.ItemsSource = items;
        }

        private void TxtSearchDb_TextChanged(object sender, TextChangedEventArgs e)
        {
            CargarBaseDeDatos(TxtSearchDb.Text.Trim());
        }

        /// <summary>
        /// Método dedicado para desempaquetar el archivo BND4 y renderizar el FLVER interno.
        /// </summary>
        private void CargarModelo3D(string rutaCompleta)
        {
            // Extrae el modelo FLVER desde el contenedor .partsbnd.dcx
            FlverModel? modeloProcesado = FlverLoader.LoadFromBnd(rutaCompleta);

            if (modeloProcesado != null)
            {
                // Llama a TU visor original pasándole el modelo
                Viewport3DViewer.LoadModel(modeloProcesado);
                Log.Information("Modelo 3D cargado exitosamente en el visor.");
            }
        }
        private void BtnImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecciona el archivo EquipParamProtector.csv",
                Filter = "Archivos CSV (*.csv)|*.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                // Llamamos al Seeder que arreglamos antes y llenamos la DB SQLite
                DatabaseSeeder.SeedFromCsv(dialog.FileName, _db!);

                // Recargamos el panel inferior para que aparezcan las armaduras mágicamente
                CargarBaseDeDatos("");

                MessageBox.Show("Base de datos importada correctamente.", "Elden Ring Armor Studio");
            }
        }

        // EVENTO DataGrid (Base de datos)
        private void ArmorGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ArmorGrid.SelectedItem is not ArmorItem item) return;

            TxtSelectedModelName.Text = item.DisplayName;
            string nombreArchivoBnd = $"{item.EquipModelId.ToLower()}.partsbnd.dcx";
            TxtSelectedModelFile.Text = nombreArchivoBnd;

            TxtSelectedModelStats.Text = $"• Categoría: {item.Category}\n" +
                                         $"• Set: {(string.IsNullOrEmpty(item.SetName) ? "Ninguno" : item.SetName)}\n" +
                                         $"• Peso: {item.Weight ?? 0:F1}\n" +
                                         $"• Absorción Física: {item.DefensePhys ?? 0:P1}\n" +
                                         $"• Estabilidad (Poise): {item.Poise ?? 0}";

            string rutaCompletaModelo = Path.Combine(_configuredPartsDir, nombreArchivoBnd);
            TxtSelectedModelPath.Text = rutaCompletaModelo;

            ImgSelectedIcon.Source = item.IconId.HasValue ? CargarIcono(item.IconId.Value) : null;

            // Extraemos y cargamos el FLVER
            CargarModelo3D(rutaCompletaModelo);
        }

        // EVENTO ListBox (Directorio crudo)
        private void ModelFilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelFilesListBox.SelectedItem is not string fileName) return;

            TxtSelectedModelName.Text = "Archivo de Parámetros Suelto";
            TxtSelectedModelFile.Text = fileName;
            string rutaCompleta = Path.Combine(_configuredPartsDir, fileName);
            TxtSelectedModelPath.Text = rutaCompleta;
            TxtSelectedModelStats.Text = "No asociado a un registro de la base de datos.";
            ImgSelectedIcon.Source = null;

            // Extraemos y cargamos el FLVER
            CargarModelo3D(rutaCompleta);
        }

        private static BitmapImage? CargarIcono(int iconId)
        {
            var path = Path.GetFullPath(Path.Combine(IconsDir, $"MENU_Knowledge_{iconId}.png"));
            if (!File.Exists(path)) return null;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 128;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }

    public class ArmorItem
    {
        public string EquipModelId { get; init; } = "";
        public string NameEn { get; init; } = "";
        public string NameEs { get; init; } = "";
        public string Category { get; init; } = "";
        public string SetName { get; init; } = "";
        public bool IsAltered { get; init; }
        public int? IconId { get; init; }

        public double? Weight { get; init; }
        public double? DefensePhys { get; init; }
        public double? DefenseMagic { get; init; }
        public double? DefenseFire { get; init; }
        public double? DefenseLightning { get; init; }
        public double? Poise { get; init; }

        public string DisplayName => !string.IsNullOrWhiteSpace(NameEs) ? NameEs : NameEn;
    }
}