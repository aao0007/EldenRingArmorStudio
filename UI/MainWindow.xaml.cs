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
    public partial class MainWindow : Window
    {
        private ArmorDatabase _db;
        private ArmorRecord _selectedRecord;

        public MainWindow()
        {
            InitializeComponent();

            // Aplicar tema guardado (oscuro por defecto)
            ThemeManager.LoadSavedOrDefault();
            UpdateThemeMenuLabel();

            _db = new ArmorDatabase("data/armor_db.sqlite");
            CheckAndSeedDatabase();

            if (ExplorerPanel != null)
            {
                ExplorerPanel.Initialize(_db);
                ExplorerPanel.ArmorClicked += (record) =>
                {
                    _selectedRecord = record;
                    InfoPanel.ShowRecord(record);
                };
                ExplorerPanel.ModelDoubleClicked += async (fileName) =>
                    await TryLoadModelFromFileNameAsync(fileName);
            }

            if (InfoPanel != null)
                InfoPanel.LoadModelRequested += async (fileName) =>
                    await TryLoadModelFromFileNameAsync(fileName);

            if (FileTree != null)
                FileTree.FileSelected += async (filePath) =>
                    await LoadModelWorkflowAsync(filePath);
        }

        // ── Tema ──────────────────────────────────────────────────────────────

        private void MenuToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Toggle();
            UpdateThemeMenuLabel();
        }

        private void UpdateThemeMenuLabel()
        {
            if (MenuToggleTheme == null) return;
            MenuToggleTheme.Header = ThemeManager.Current == AppTheme.Dark
                ? "☀  Cambiar a Tema Claro"
                : "🌙  Cambiar a Tema Oscuro";
        }

        // ── Base de datos ─────────────────────────────────────────────────────

        private void CheckAndSeedDatabase()
        {
            // Re-seed si la BD tiene rutas absolutas legacy (contienen ":\")
            bool needsReseed = false;
            if (_db.Count() > 0)
            {
                var sample = _db.SearchArmor("", null, false).Take(1).FirstOrDefault();
                if (sample != null &&
                    !string.IsNullOrWhiteSpace(sample.ThumbnailPath) &&
                    (sample.ThumbnailPath.Contains(":\\") || sample.ThumbnailPath.Contains(":/")))
                {
                    Serilog.Log.Warning("[MainWindow] Rutas absolutas legacy detectadas, re-seeding...");
                    needsReseed = true;
                }
            }

            if (_db.Count() == 0 || needsReseed)
            {
                string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "data", "EquipParamProtector.csv");

                if (File.Exists(csvPath))
                {
                    if (needsReseed)
                    {
                        File.Delete("data/armor_db.sqlite");
                        _db = new ArmorDatabase("data/armor_db.sqlite");
                    }
                    DatabaseSeeder.SeedFromCsv(csvPath, _db);
                }
                else
                {
                    MessageBox.Show(
                        "La base de datos está vacía y no se encontró 'EquipParamProtector.csv' en data/.",
                        "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // ── Menú Archivo ──────────────────────────────────────────────────────

        private void MenuConfigurarModelos_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona la carpeta donde tienes los modelos (.partsbnd.dcx)"
            };
            if (dialog.ShowDialog() == true)
            {
                AppConfig.Instance.Project.PartsLibraryPath = dialog.FolderName;
                FileTree?.Refresh();
                MessageBox.Show($"Directorio configurado:\n{dialog.FolderName}",
                    "Guardado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuRecargarCsv_Click(object sender, RoutedEventArgs e)
        {
            string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "data", "EquipParamProtector.csv");

            if (!File.Exists(csvPath))
            {
                MessageBox.Show($"No se encontró:\n{csvPath}", "Falta CSV",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                DatabaseSeeder.SeedFromCsv(csvPath, _db);
                ExplorerPanel?.Refresh();
                MessageBox.Show("CSV recargado correctamente.", "Éxito",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) =>
            Application.Current.Shutdown();

        // ── Carga 3D ──────────────────────────────────────────────────────────

        private async Task TryLoadModelFromFileNameAsync(string fileName)
        {
            string p1 = Path.Combine(AppConfig.Get("modengine2.root_path") ?? "", "mod", "parts", fileName);
            string p2 = Path.Combine(AppConfig.Get("project.parts_library_path") ?? "", fileName);
            string dcxPath = File.Exists(p1) ? p1 : File.Exists(p2) ? p2 : null;

            if (dcxPath == null)
            {
                MessageBox.Show($"No se encontró el modelo:\n{fileName}\n\nConfigura el directorio en Archivo.",
                    "No encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                string ext = Path.GetExtension(filePath).ToLower();
                string unpackedDir = "";

                if (ext == ".flver")
                {
                    unpackedDir = Path.Combine(tempDir, "direct_flver");
                    Directory.CreateDirectory(unpackedDir);
                    File.Copy(filePath, Path.Combine(unpackedDir, Path.GetFileName(filePath)));
                }
                else if (ext == ".dcx")
                {
                    string tmp = Path.Combine(tempDir, Path.GetFileName(filePath));
                    File.Copy(filePath, tmp);

                    string witchy = AppConfig.Get("tools.witchybnd_path");
                    if (string.IsNullOrEmpty(witchy) || !File.Exists(witchy))
                    {
                        MessageBox.Show("No se encontró WitchyBND.", "Falta herramienta",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var antes = Directory.GetDirectories(tempDir);
                    var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = witchy,
                            Arguments = $"-s \"{tmp}\"",
                            WorkingDirectory = tempDir,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    proc.Start();
                    try { await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
                    catch (TimeoutException) { proc.Kill(); return; }

                    var despues = Directory.GetDirectories(tempDir);
                    unpackedDir = despues.FirstOrDefault(d => !antes.Contains(d)) ?? "";

                    if (string.IsNullOrEmpty(unpackedDir))
                    {
                        string baseName = Path.GetFileNameWithoutExtension(tmp).Split('.')[0];
                        unpackedDir = Directory.GetDirectories(tempDir)
                            .FirstOrDefault(d => Path.GetFileName(d)
                                .StartsWith(baseName, StringComparison.OrdinalIgnoreCase)) ?? "";
                    }
                }

                if (!string.IsNullOrEmpty(unpackedDir) && Directory.Exists(unpackedDir))
                {
                    var model = FlverLoader.LoadFromDirectory(unpackedDir);
                    if (model != null) { FlverViewport.LoadModel(model); FlverViewport.ToolTip = null; }
                    else MessageBox.Show("FLVER sin geometría válida.", "Error 3D",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                    MessageBox.Show("No se encontró la carpeta extraída.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error:\n{ex.Message}", "Excepción",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Flags ─────────────────────────────────────────────────────────────

        private void MenuGenerarFlags_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRecord == null)
            {
                MessageBox.Show("Selecciona una armadura primero.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var flags = new List<int>();
            var preset = InvisibleFlags.Presets.FirstOrDefault(p => p.Key == "head_face_cover");
            if (preset != null) flags.AddRange(preset.Flags);

            string numId = new string(_selectedRecord.EquipModelId.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(numId)) numId = "10000";

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"flags_{_selectedRecord.EquipModelId}.csv",
                DefaultExt = ".csv",
                Filter = "CSV (*.csv)|*.csv"
            };
            if (dlg.ShowDialog() == true &&
                InvisibleFlags.GenerateSmithboxCsv(dlg.FileName,
                    new List<(string, IEnumerable<int>)> { (numId, flags) }))
                MessageBox.Show("CSV generado. Importa en Smithbox.", "OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Drag & Drop ───────────────────────────────────────────────────────

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0)
                {
                    string f = files[0];
                    if (f.EndsWith(".dcx", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".flver", StringComparison.OrdinalIgnoreCase))
                        await LoadModelWorkflowAsync(f);
                    else
                        MessageBox.Show("Arrastra un .dcx o .flver.", "No soportado",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error drag & drop:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}