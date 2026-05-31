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

        // ── REEMPLAZA LoadModelWorkflowAsync en MainWindow.xaml.cs ───────────────────

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

                // ── FLVER directo ─────────────────────────────────────────────────────
                if (ext == ".flver")
                {
                    unpackedDir = Path.Combine(tempDir, "direct_flver");
                    Directory.CreateDirectory(unpackedDir);
                    File.Copy(filePath, Path.Combine(unpackedDir, Path.GetFileName(filePath)));
                    Log.Information("[Load] FLVER directo: {F}", filePath);
                }

                // ── DCX → WitchyBND ───────────────────────────────────────────────────
                else if (ext == ".dcx")
                {
                    string witchy = AppConfig.Instance.Tools.WitchyBndPath;

                    // Validar que WitchyBND existe
                    if (string.IsNullOrWhiteSpace(witchy) || !File.Exists(witchy))
                    {
                        string msg =
                            "No se encontró WitchyBND.\n\n" +
                            $"Ruta configurada:\n{(string.IsNullOrWhiteSpace(witchy) ? "(vacía)" : witchy)}\n\n" +
                            "Ve a ⚙ Configuración y selecciona WitchyBND.exe,\n" +
                            "o colócalo en:\n  tools/witchybnd/WitchyBND.exe";

                        SetStatus("⚠ WitchyBND no encontrado — configúralo en ⚙ Configuración");
                        MessageBox.Show(msg, "WitchyBND no encontrado",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Copiar DCX a temp y ejecutar WitchyBND
                    string tmp = Path.Combine(tempDir, Path.GetFileName(filePath));
                    File.Copy(filePath, tmp);

                    Log.Information("[Load] Ejecutando WitchyBND: {W} -s \"{F}\"", witchy, tmp);

                    var antes = Directory.GetDirectories(tempDir);
                    var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = witchy,
                            Arguments = $"-s \"{tmp}\"",
                            WorkingDirectory = tempDir,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                        }
                    };

                    proc.Start();
                    string stdout = "", stderr = "";
                    try
                    {
                        var outTask = proc.StandardOutput.ReadToEndAsync();
                        var errTask = proc.StandardError.ReadToEndAsync();
                        await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                        stdout = await outTask;
                        stderr = await errTask;
                    }
                    catch (TimeoutException)
                    {
                        proc.Kill();
                        SetStatus("⚠ WitchyBND tardó demasiado (timeout 20s)");
                        MessageBox.Show(
                            $"WitchyBND tardó más de 20 segundos y fue cancelado.\n\nArchivo: {Path.GetFileName(filePath)}",
                            "Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    Log.Information("[WitchyBND] Exit={Code} | out={Out} | err={Err}",
                        proc.ExitCode, stdout.Trim(), stderr.Trim());

                    // Buscar carpeta extraída
                    var despues = Directory.GetDirectories(tempDir);
                    unpackedDir = despues.FirstOrDefault(d => !antes.Contains(d)) ?? "";

                    // Fallback por nombre si no se detectó por diferencia
                    if (string.IsNullOrEmpty(unpackedDir))
                    {
                        string bn = Path.GetFileNameWithoutExtension(tmp).Split('.')[0];
                        unpackedDir = Array.Find(
                            Directory.GetDirectories(tempDir),
                            d => Path.GetFileName(d).StartsWith(bn,
                                     StringComparison.OrdinalIgnoreCase)) ?? "";
                    }

                    // Validar que se extrajo algo
                    if (string.IsNullOrEmpty(unpackedDir) || !Directory.Exists(unpackedDir))
                    {
                        string detalle =
                            $"WitchyBND no generó ninguna carpeta de salida.\n\n" +
                            $"Archivo: {Path.GetFileName(filePath)}\n" +
                            $"Código de salida: {proc.ExitCode}\n" +
                            (string.IsNullOrWhiteSpace(stderr) ? "" : $"Error: {stderr.Trim()}\n") +
                            (string.IsNullOrWhiteSpace(stdout) ? "" : $"Salida: {stdout.Trim()}\n") +
                            $"\nTemp dir: {tempDir}";

                        Log.Error("[Load] WitchyBND no generó carpeta. {D}", detalle);
                        SetStatus("⚠ WitchyBND no pudo extraer el archivo");
                        MessageBox.Show(detalle, "Error al extraer",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    Log.Information("[Load] Carpeta extraída: {D}", unpackedDir);

                    // Verificar que hay contenido útil
                    var flvers = Directory.GetFiles(unpackedDir, "*.flver",
                                     SearchOption.AllDirectories);
                    if (flvers.Length == 0)
                    {
                        string detalle =
                            $"WitchyBND extrajo la carpeta pero no contiene archivos .flver.\n\n" +
                            $"Carpeta: {unpackedDir}\n" +
                            $"Contenido:\n" +
                            string.Join("\n", Directory.GetFiles(unpackedDir,
                                "*", SearchOption.AllDirectories));

                        Log.Warning("[Load] Sin .flver en {D}", unpackedDir);
                        SetStatus("⚠ No se encontró .flver en el archivo extraído");
                        MessageBox.Show(detalle, "Sin FLVER",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else
                {
                    SetStatus($"⚠ Formato no soportado: {ext}");
                    MessageBox.Show($"Formato no soportado: {ext}\n\nSolo se admiten .flver y .partsbnd.dcx",
                        "Formato inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // ── Cargar FLVER ──────────────────────────────────────────────────────
                if (!string.IsNullOrEmpty(unpackedDir) && Directory.Exists(unpackedDir))
                {
                    SetStatus($"Parseando FLVER…");
                    var model = FlverLoader.LoadFromDirectory(unpackedDir);

                    if (model == null)
                    {
                        SetStatus("⚠ FlverLoader no pudo parsear el modelo");
                        MessageBox.Show(
                            $"FlverLoader devolvió null.\n\nRevisa los logs en data/logs/ para más detalle.",
                            "Error de parseo", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    if (model.Meshes.Count == 0)
                    {
                        SetStatus("⚠ El modelo no contiene geometría");
                        MessageBox.Show(
                            "El FLVER se parseó correctamente pero no tiene mallas con geometría.\n\n" +
                            "Puede ser un modelo vacío o un formato no compatible.",
                            "Sin geometría", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    FlverViewport.LoadModel(model);
                    SetStatus(
                        $"{Path.GetFileName(filePath)} — " +
                        $"{model.TotalVertices:N0} verts · " +
                        $"{model.TotalTriangles:N0} tris · " +
                        $"{model.Materials.Count} materiales");

                    Log.Information("[Load] OK: {V} verts, {T} tris, {M} materiales",
                        model.TotalVertices, model.TotalTriangles, model.Materials.Count);
                }
                else
                {
                    SetStatus("⚠ No se encontró la carpeta extraída");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"⚠ Error: {ex.Message}");
                Log.Error(ex, "LoadModelWorkflowAsync");
                MessageBox.Show(
                    $"Error inesperado al cargar el modelo:\n\n{ex.Message}\n\n" +
                    $"Revisa los logs en data/logs/ para más detalle.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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