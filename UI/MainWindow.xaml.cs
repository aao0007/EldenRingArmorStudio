using EldenRingArmorStudio.Core;
using EldenRingArmorStudio.UI.Viewer3D;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace EldenRingArmorStudio.UI
{
    /// <summary>
    /// Lógica de interacción para MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ArmorDatabase _db;
        private ArmorRecord _selectedRecord;

        public MainWindow()
        {
            InitializeComponent();

            // 1. Inicializar la conexión a la base de datos SQLite
            _db = new ArmorDatabase("data/armor_db.sqlite");

            // 2. Autogenerar la base de datos si está vacía
            CheckAndSeedDatabase();

            // 3. Conectar los eventos de tus paneles

            if (ExplorerPanel != null)
            {
                // Inyectamos la BD al explorador para que pueda buscar
                ExplorerPanel.Initialize(_db);

                ExplorerPanel.ArmorClicked += (record) =>
                {
                    _selectedRecord = record;
                    InfoPanel.ShowRecord(record);
                };

                ExplorerPanel.ModelDoubleClicked += async (fileName) =>
                {
                    await TryLoadModelFromFileNameAsync(fileName);
                };
            }

            if (InfoPanel != null)
            {
                InfoPanel.LoadModelRequested += async (fileName) =>
                {
                    await TryLoadModelFromFileNameAsync(fileName);
                };
            }

            if (FileTree != null)
            {
                FileTree.FileSelected += async (filePath) =>
                {
                    await LoadModelWorkflowAsync(filePath);
                };
            }
        }

        private void CheckAndSeedDatabase()
        {
            if (_db.Count() == 0)
            {
                string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "EquipParamProtector.csv");
                if (File.Exists(csvPath))
                {
                    DatabaseSeeder.SeedFromCsv(csvPath, _db);
                }
                else
                {
                    MessageBox.Show("La base de datos está vacía y no se encontró el archivo 'EquipParamProtector.csv' en la carpeta 'data/'.\n\nPor favor, asegúrate de que el CSV esté en la carpeta para cargar las armaduras.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // ── MENÚ: Configurar Modelos y Recargar CSV ──

        private void MenuConfigurarModelos_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona la carpeta donde tienes los modelos (.partsbnd.dcx)"
            };

            if (dialog.ShowDialog() == true)
            {
                string selectedDir = dialog.FolderName;

                // Guardamos la ruta en tu sistema de configuración
                AppConfig.Instance.Project.PartsLibraryPath = selectedDir;

                // Le decimos al panel lateral izquierdo que recargue su árbol de archivos
                if (FileTree != null)
                {
                    FileTree.Refresh();
                }

                MessageBox.Show($"Directorio de modelos configurado en:\n{selectedDir}", "Configuración guardada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuRecargarCsv_Click(object sender, RoutedEventArgs e)
        {
            string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "EquipParamProtector.csv");

            if (File.Exists(csvPath))
            {
                try
                {
                    DatabaseSeeder.SeedFromCsv(csvPath, _db);

                    // Recargamos las miniaturas del explorador
                    if (ExplorerPanel != null)
                    {
                        ExplorerPanel.Refresh();
                    }

                    MessageBox.Show("CSV leído correctamente desde /data y armaduras actualizadas.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al leer el CSV:\n{ex.Message}", "Error de Lectura", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show($"No se encontró el archivo en:\n{csvPath}\n\nAsegúrate de poner 'EquipParamProtector.csv' dentro de la carpeta 'data'.", "Falta CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── CARGA 3D ──

        private async Task TryLoadModelFromFileNameAsync(string fileName)
        {
            string modenginePath = AppConfig.Get("modengine2.root_path");
            string partsLibraryPath = AppConfig.Get("project.parts_library_path");

            string dcxPath = Path.Combine(modenginePath ?? "", "mod", "parts", fileName);
            if (!File.Exists(dcxPath))
            {
                dcxPath = Path.Combine(partsLibraryPath ?? "", fileName);
            }

            if (!File.Exists(dcxPath))
            {
                MessageBox.Show($"No se encontró el archivo de modelo:\n{fileName}\n\nAsegúrate de haber configurado el directorio de modelos en el menú Archivo.", "Archivo no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await LoadModelWorkflowAsync(dcxPath);
        }

        private async Task LoadModelWorkflowAsync(string filePath)
        {
            try
            {
                FlverViewport.ToolTip = $"Cargando {Path.GetFileName(filePath)}...";

                string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "temp_extract");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                string extension = Path.GetExtension(filePath).ToLower();
                string unpackedDir = "";

                if (extension == ".flver")
                {
                    unpackedDir = Path.Combine(tempDir, "direct_flver");
                    Directory.CreateDirectory(unpackedDir);

                    string targetFlverPath = Path.Combine(unpackedDir, Path.GetFileName(filePath));
                    File.Copy(filePath, targetFlverPath);
                }
                else if (extension == ".dcx")
                {
                    string tempDcxPath = Path.Combine(tempDir, Path.GetFileName(filePath));
                    File.Copy(filePath, tempDcxPath);

                    string witchyExe = AppConfig.Get("tools.witchybnd_path");
                    if (string.IsNullOrEmpty(witchyExe) || !File.Exists(witchyExe))
                    {
                        MessageBox.Show("No se encontró WitchyBND. Configura su ruta correctamente.", "Herramienta Faltante", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var carpetasAntes = Directory.GetDirectories(tempDir);

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = witchyExe,
                            Arguments = $"-s \"{tempDcxPath}\"",
                            WorkingDirectory = tempDir,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();

                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                    }
                    catch (TimeoutException)
                    {
                        process.Kill();
                        MessageBox.Show("WitchyBND tardó demasiado tiempo en responder (Timeout de 15s).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var carpetasDespues = Directory.GetDirectories(tempDir);
                    unpackedDir = carpetasDespues.FirstOrDefault(d => !carpetasAntes.Contains(d));

                    if (string.IsNullOrEmpty(unpackedDir))
                    {
                        string nombreSinDcx = Path.GetFileNameWithoutExtension(tempDcxPath);
                        string baseName = nombreSinDcx.Split('.')[0];

                        unpackedDir = Directory.GetDirectories(tempDir)
                            .FirstOrDefault(d => Path.GetFileName(d).StartsWith(baseName, StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (!string.IsNullOrEmpty(unpackedDir) && Directory.Exists(unpackedDir))
                {
                    FlverModel miModelo = FlverLoader.LoadFromDirectory(unpackedDir);

                    if (miModelo != null)
                    {
                        FlverViewport.LoadModel(miModelo);
                        FlverViewport.ToolTip = null;
                    }
                    else
                    {
                        MessageBox.Show("El archivo FLVER no contiene geometría válida.", "Error 3D", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("No se encontró la carpeta extraída.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en el renderizado:\n{ex.Message}", "Excepción", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── OTROS EVENTOS ──

        private void MenuGenerarFlags_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRecord != null)
            {
                var flagsActivos = new List<int>();

                var preset = InvisibleFlags.Presets.FirstOrDefault(p => p.Key == "head_face_cover");
                if (preset != null) flagsActivos.AddRange(preset.Flags);

                string numericId = new string(_selectedRecord.EquipModelId.Where(char.IsDigit).ToArray());
                if (string.IsNullOrEmpty(numericId)) numericId = "10000";

                var entradas = new List<(string ParamId, IEnumerable<int> Flags)> { (numericId, flagsActivos) };

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"flags_{_selectedRecord.EquipModelId}.csv",
                    DefaultExt = ".csv",
                    Filter = "Archivos CSV (*.csv)|*.csv"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    if (InvisibleFlags.GenerateSmithboxCsv(saveFileDialog.FileName, entradas))
                        MessageBox.Show("¡CSV generado con éxito! Importa este archivo en Smithbox.", "Guardado", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("Selecciona primero una armadura.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                    if (files != null && files.Length > 0)
                    {
                        string filePath = files[0];

                        if (filePath.EndsWith(".dcx", StringComparison.OrdinalIgnoreCase) ||
                            filePath.EndsWith(".flver", StringComparison.OrdinalIgnoreCase))
                        {
                            await LoadModelWorkflowAsync(filePath);
                        }
                        else
                        {
                            MessageBox.Show("Arrastra un modelo válido (.dcx o .flver).", "Archivo no soportado", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al procesar el archivo:\n{ex.Message}", "Error Drag & Drop", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}