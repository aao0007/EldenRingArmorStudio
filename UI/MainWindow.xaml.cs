using EldenRingArmorStudio.Core;
using EldenRingArmorStudio.UI.Dialogs;
using EldenRingArmorStudio.UI.Viewer3D;
using Serilog;
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

            ThemeManager.LoadSavedOrDefault();
            UpdateThemeMenuLabel();

            _db = new ArmorDatabase("data/armor_db.sqlite");
            CheckAndSeedDatabase();
            WireUpPanels();
        }

        // ── Conexión entre paneles ────────────────────────────────────────────

        private void WireUpPanels()
        {
            // Explorador inferior
            if (ExplorerPanel != null)
            {
                ExplorerPanel.Initialize(_db);

                ExplorerPanel.ArmorClicked += record =>
                {
                    _selectedRecord = record;
                    InfoPanel?.ShowRecord(record);
                    DupPanel?.SetSourceFile(null, record);
                };

                ExplorerPanel.ModelDoubleClicked += async fn =>
                    await TryLoadModelFromFileNameAsync(fn);
            }

            // Árbol de archivos
            if (FileTree != null)
            {
                FileTree.FileSelected += async filePath =>
                {
                    var record = FindRecordByFileName(filePath);
                    _selectedRecord = record;
                    InfoPanel?.ShowRecord(record);
                    DupPanel?.SetSourceFile(filePath, record);
                    SetStatus($"Seleccionado: {Path.GetFileName(filePath)}");
                    await LoadModelWorkflowAsync(filePath);
                };
            }

            // Duplicador → InfoPanel se actualiza al clicar cada ID
            if (DupPanel != null)
            {
                DupPanel.Initialize(_db);

                DupPanel.RecordSelected += record =>
                    InfoPanel?.ShowRecord(record);

                DupPanel.DuplicateCompleted += destDir =>
                {
                    FileTree?.Refresh();
                    SetStatus($"Duplicado completado → {destDir}");
                };
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ArmorRecord FindRecordByFileName(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            string fn = Path.GetFileName(filePath);
            return _db.SearchArmor("", null, false)
                      .FirstOrDefault(r => string.Equals(
                          r.FileName, fn, StringComparison.OrdinalIgnoreCase));
        }

        private void SetStatus(string msg) => TxtStatusBar.Text = msg;

        // ── BD ────────────────────────────────────────────────────────────────

        private void CheckAndSeedDatabase()
        {
            bool needsReseed = false;
            if (_db.Count() > 0)
            {
                var s = _db.SearchArmor("", null, false).Take(1).FirstOrDefault();
                if (s != null && !string.IsNullOrWhiteSpace(s.ThumbnailPath) &&
                    (s.ThumbnailPath.Contains(":\\") || s.ThumbnailPath.Contains(":/")))
                {
                    Log.Warning("[MainWindow] Rutas legacy, re-seeding...");
                    needsReseed = true;
                }
            }

            if (_db.Count() == 0 || needsReseed)
            {
                string csv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "data", "EquipParamProtector.csv");
                if (File.Exists(csv))
                {
                    if (needsReseed)
                    {
                        File.Delete("data/armor_db.sqlite");
                        _db = new ArmorDatabase("data/armor_db.sqlite");
                    }
                    DatabaseSeeder.SeedFromCsv(csv, _db);
                    SetStatus("Base de datos cargada.");
                }
                else
                {
                    MessageBox.Show(
                        "No se encontró 'EquipParamProtector.csv' en data/.",
                        "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // ── Menús ─────────────────────────────────────────────────────────────

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                // Refrescar paneles que dependen de rutas
                FileTree?.Refresh();
                DupPanel?.RefreshPackFolders();
                UpdateThemeMenuLabel();
                SetStatus("Configuración guardada.");
            }
        }

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

        private void MenuRecargarCsv_Click(object sender, RoutedEventArgs e)
        {
            string csv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "data", "EquipParamProtector.csv");
            if (!File.Exists(csv))
            {
                MessageBox.Show($"No se encontró:\n{csv}", "Falta CSV",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                DatabaseSeeder.SeedFromCsv(csv, _db);
                ExplorerPanel?.Refresh();
                SetStatus("CSV recargado.");
                MessageBox.Show("CSV recargado.", "OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuVaciarBd_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("¿Vaciar la base de datos?", "Confirmar",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            try
            {
                File.Delete("data/armor_db.sqlite");
                _db = new ArmorDatabase("data/armor_db.sqlite");
                ExplorerPanel?.Refresh();
                SetStatus("Base de datos vaciada.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuGenerarFlags_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRecord == null)
            {
                MessageBox.Show("Selecciona primero una armadura.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var flags = new List<int>();
            var preset = InvisibleFlags.Presets
                .FirstOrDefault(p => p.Key == "head_face_cover");
            if (preset != null) flags.AddRange(preset.Flags);

            string numId = new string(_selectedRecord.EquipModelId
                .Where(char.IsDigit).ToArray());

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

        private void Exit_Click(object sender, RoutedEventArgs e) =>
            Application.Current.Shutdown();

        // ── Carga 3D ──────────────────────────────────────────────────────────

        private async Task TryLoadModelFromFileNameAsync(string fileName)
        {
            string modRoot = AppConfig.Instance.ModEngine2.RootPath;
            string[] candidates =
            {
                Path.Combine(AppConfig.Instance.Project.PartsLibraryPath ?? "", fileName),
                string.IsNullOrEmpty(modRoot) ? "" : Path.Combine(modRoot, "mod", "parts", fileName)
            };

            string path = candidates.FirstOrDefault(File.Exists);
            if (path == null) { SetStatus($"No encontrado: {fileName}"); return; }
            await LoadModelWorkflowAsync(path);
        }

        private async Task LoadModelWorkflowAsync(string filePath)
        {
            try
            {
                SetStatus($"Cargando {Path.GetFileName(filePath)}…");

                string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "data", "temp_extract");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                string ext = Path.GetExtension(filePath).ToLower();
                string unpackedDir = "";

                if (ext == ".flver")
                {
                    unpackedDir = Path.Combine(tempDir, "direct_flver");
                    Directory.CreateDirectory(unpackedDir);
                    File.Copy(filePath, Path.Combine(unpackedDir,
                        Path.GetFileName(filePath)));
                }
                else if (ext == ".dcx")
                {
                    string tmp = Path.Combine(tempDir, Path.GetFileName(filePath));
                    File.Copy(filePath, tmp);

                    string witchy = AppConfig.Instance.Tools.WitchyBndPath;
                    if (string.IsNullOrEmpty(witchy) || !File.Exists(witchy))
                    {
                        SetStatus("WitchyBND no encontrado. Configúralo en ⚙ Configuración.");
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
                        string bn = Path.GetFileNameWithoutExtension(tmp).Split('.')[0];
                        unpackedDir = Directory.GetDirectories(tempDir)
                            .FirstOrDefault(d => Path.GetFileName(d)
                                .StartsWith(bn, StringComparison.OrdinalIgnoreCase)) ?? "";
                    }
                }

                if (!string.IsNullOrEmpty(unpackedDir) && Directory.Exists(unpackedDir))
                {
                    var model = FlverLoader.LoadFromDirectory(unpackedDir);
                    if (model != null)
                    {
                        FlverViewport.LoadModel(model);
                        SetStatus($"{Path.GetFileName(filePath)} — " +
                                  $"{model.TotalVertices:N0} verts · " +
                                  $"{model.TotalTriangles:N0} tris");
                    }
                    else SetStatus("FLVER sin geometría.");
                }
                else SetStatus("No se encontró la carpeta extraída.");
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
                Log.Error(ex, "LoadModelWorkflowAsync");
            }
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
                    {
                        var rec = FindRecordByFileName(f);
                        InfoPanel?.ShowRecord(rec);
                        DupPanel?.SetSourceFile(f, rec);
                        await LoadModelWorkflowAsync(f);
                    }
                }
            }
            catch (Exception ex) { Log.Error(ex, "Window_Drop"); }
        }
    }
}