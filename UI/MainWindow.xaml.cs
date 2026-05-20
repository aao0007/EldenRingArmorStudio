using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using EldenRingArmorStudio.Core;
using EldenRingArmorStudio.UI.Viewer3D;

namespace EldenRingArmorStudio.UI
{
    /// <summary>
    /// Lógica de interacción para MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ArmorDatabase _db;

        public MainWindow()
        {
            InitializeComponent();

            // 1. Inicializar la conexión a la base de datos SQLite
            _db = new ArmorDatabase("data/armor_db.sqlite");

            // 2. COMPROBACIÓN NUEVA: Autogenerar la base de datos si está vacía
            if (_db.Count() == 0)
            {
                string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "EquipParamProtector.csv");
                if (File.Exists(csvPath))
                {
                    DatabaseSeeder.SeedFromCsv(csvPath, _db);
                }
                else
                {
                    MessageBox.Show("La base de datos está vacía y no se encontró el archivo 'EquipParamProtector.csv' en la carpeta 'data/'.\n\nPor favor, copia el CSV a la carpeta para que la aplicación pueda cargar las armaduras.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // 3. Cargar la lista de armaduras
            LoadArmorList();

            // 4. Vincular el evento de doble clic a la lista de la interfaz
            ArmorListBox.MouseDoubleClick += ArmorListBox_MouseDoubleClick;
        }

        private void LoadArmorList()
        {
            try
            {
                // Cargar todas las piezas de armadura registradas en la base de datos
                var armors = _db.SearchArmor("");

                // Configurar la lista para mostrar el nombre en español
                ArmorListBox.DisplayMemberPath = "NameEs";
                ArmorListBox.ItemsSource = armors;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo cargar la base de datos local SQLite:\n{ex.Message}\n\nAsegúrate de tener tu archivo 'armor_db.sqlite' en la carpeta 'data/'.", "Error de Base de Datos", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void ArmorListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ArmorListBox.SelectedItem is ArmorPart selectedArmor)
            {
                string fileName = selectedArmor.FileName;

                // Obtener las rutas desde el gestor de configuración
                string modenginePath = AppConfig.Get("modengine2.root_path");
                string partsLibraryPath = AppConfig.Get("project.parts_library_path");

                // Buscar el archivo .dcx primero en el directorio de mods activo, y si no en la biblioteca general
                string dcxPath = Path.Combine(modenginePath, "mod", "parts", fileName);
                if (!File.Exists(dcxPath))
                {
                    dcxPath = Path.Combine(partsLibraryPath, fileName);
                }

                if (!File.Exists(dcxPath))
                {
                    MessageBox.Show($"No se encontró el archivo de modelo:\n{fileName}\n\nSe buscó en las carpetas de ModEngine2 y en tu Biblioteca de piezas del proyecto. Asegúrate de colocar el archivo .partsbnd.dcx.", "Archivo no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Iniciar flujo de descompresión y carga en el visor 3D
                await LoadModelWorkflowAsync(dcxPath);
            }
        }

        private async Task LoadModelWorkflowAsync(string filePath)
        {
            try
            {
                // 1. Crear directorio temporal limpio
                string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "temp_extract");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                string extension = Path.GetExtension(filePath).ToLower();
                string unpackedDir = "";

                // CASO A: El archivo ya es un FLVER directo (No necesita WitchyBND)
                if (extension == ".flver")
                {
                    // Simplemente creamos una subcarpeta temporal y copiamos el flver dentro
                    // para que FlverLoader.LoadFromDirectory funcione igual que antes
                    unpackedDir = Path.Combine(tempDir, "direct_flver");
                    Directory.CreateDirectory(unpackedDir);

                    string targetFlverPath = Path.Combine(unpackedDir, Path.GetFileName(filePath));
                    File.Copy(filePath, targetFlverPath);
                }
                // CASO B: Es un archivo comprimido de FromSoftware (.dcx)
                else if (extension == ".dcx")
                {
                    string tempDcxPath = Path.Combine(tempDir, Path.GetFileName(filePath));
                    File.Copy(filePath, tempDcxPath);

                    string witchyExe = AppConfig.Get("tools.witchybnd_path");
                    if (!File.Exists(witchyExe))
                    {
                        MessageBox.Show($"No se encontró el descompresor en la ruta:\n{witchyExe}", "Herramienta Faltante", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Guardamos el estado de las carpetas antes de ejecutar WitchyBND
                    var carpetasAntes = Directory.GetDirectories(tempDir);

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = witchyExe,
                            Arguments = $"-s \"{tempDcxPath}\"",
                            WorkingDirectory = tempDir, // OBLIGA a WitchyBND a crear la carpeta en data/temp_extract
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
                        MessageBox.Show("WitchyBND tardó demasiado tiempo en responder (Timeout de 15s).", "Error de Tiempo de Espera", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // En lugar de adivinar el nombre (si usa guiones o puntos), 
                    // buscamos qué carpeta nueva apareció en 'temp_extract' tras la ejecución
                    var carpetasDespues = Directory.GetDirectories(tempDir);
                    unpackedDir = carpetasDespues.FirstOrDefault(d => !carpetasAntes.Contains(d));

                    // Si por algún motivo la lógica anterior falla, usamos una búsqueda por aproximación
                    if (string.IsNullOrEmpty(unpackedDir))
                    {
                        string nombreSinDcx = Path.GetFileNameWithoutExtension(tempDcxPath); // am_m_1360.partsbnd
                        string baseName = nombreSinDcx.Split('.')[0]; // am_m_1360

                        unpackedDir = Directory.GetDirectories(tempDir)
                            .FirstOrDefault(d => Path.GetFileName(d).StartsWith(baseName, StringComparison.OrdinalIgnoreCase));
                    }
                }

                // 3. Comprobación y carga común para ambos casos
                if (Directory.Exists(unpackedDir))
                {
                    // Leer el FLVER y extraer texturas DDS usando SoulsFormats
                    FlverModel miModelo = FlverLoader.LoadFromDirectory(unpackedDir);

                    if (miModelo != null)
                    {
                        // Enviar al lienzo OpenGL
                        FlverViewport.LoadModel(miModelo);
                    }
                    else
                    {
                        MessageBox.Show("El archivo FLVER no contiene geometría válida o falló el parseo con SoulsFormats.", "Error de Lectura 3D", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("No se encontró la carpeta con el modelo a cargar.", "Error de Carpeta", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en el flujo de renderizado:\n{ex.Message}", "Excepción", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Eventos del Menú Superior de tu XAML ──

        private void MenuGenerarFlags_Click(object sender, RoutedEventArgs e)
        {
            if (ArmorListBox.SelectedItem is ArmorPart selectedArmor)
            {
                var flagsActivos = new List<int>();

                // Aplica por defecto el preset de ocultar cara para el ejemplo
                var preset = InvisibleFlags.Presets.FirstOrDefault(p => p.Key == "head_face_cover");
                if (preset != null) flagsActivos.AddRange(preset.Flags);

                // Limpiar el ID del modelo para Smithbox ("HD_M_1200" -> "1200")
                string numericId = new string(selectedArmor.EquipModelId.Where(char.IsDigit).ToArray());
                if (string.IsNullOrEmpty(numericId)) numericId = "10000";

                var entradas = new List<(string ParamId, IEnumerable<int> Flags)> { (numericId, flagsActivos) };

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"flags_{selectedArmor.EquipModelId}.csv",
                    DefaultExt = ".csv",
                    Filter = "Archivos CSV (*.csv)|*.csv"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    if (InvisibleFlags.GenerateSmithboxCsv(saveFileDialog.FileName, entradas))
                        MessageBox.Show("¡CSV generado con éxito! Importa este archivo en Smithbox > EquipParamProtector.", "CSV Guardado", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("Por favor, selecciona primero una pieza de armadura de la lista izquierda.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                            FlverViewport.ToolTip = $"Cargando {Path.GetFileName(filePath)}...";
                            await LoadModelWorkflowAsync(filePath);
                        }
                        else
                        {
                            MessageBox.Show("Por favor, arrastra un archivo de modelo válido (.dcx o .flver).", "Archivo no soportado", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error crítico al procesar el archivo arrastrado:\n{ex.Message}", "Error Drag & Drop", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}