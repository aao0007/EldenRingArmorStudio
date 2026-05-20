using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using EldenRingArmorStudio.Core;
using EldenRingArmorStudio.UI.Viewer3D;

namespace EldenRingArmorStudio.UI
{
    public partial class MainWindow : Window
    {
        private ArmorDatabase _db;
        // Asume que la ruta de tus mods está aquí (esto debería venir de tu AppConfig)
        private string _modPartsPath = @"C:\Ruta\A\Tu\ModEngine2\mod\parts";

        public MainWindow()
        {
            InitializeComponent();

            // Inicializar BD y cargar la lista
            _db = new ArmorDatabase("data/armor_db.sqlite");
            LoadArmorList();

            // Suscribir el evento de doble clic en la lista
            ArmorListBox.MouseDoubleClick += ArmorListBox_MouseDoubleClick;
        }

        private void LoadArmorList()
        {
            // Cargamos todos los registros de la BD a la ListBox
            var armors = _db.SearchArmor("");

            // Para que se vea bonito en la UI sin tener que hacer un DataTemplate en XAML:
            ArmorListBox.DisplayMemberPath = "NameEs";
            ArmorListBox.ItemsSource = armors;
        }

        private async void ArmorListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ArmorListBox.SelectedItem is ArmorPart selectedArmor)
            {
                string fileName = selectedArmor.FileName; // ej. "hd_m_1000.partsbnd.dcx"
                string dcxPath = Path.Combine(_modPartsPath, fileName);

                if (!File.Exists(dcxPath))
                {
                    MessageBox.Show($"No se encontró el archivo en:\n{dcxPath}", "Falta Archivo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Cambiar título de la pestaña del visor
                FlverViewport.ToolTip = $"Cargando {fileName}...";

                await LoadModelWorkflowAsync(dcxPath);
            }
        }

        private async Task LoadModelWorkflowAsync(string dcxPath)
        {
            try
            {
                // 1. Preparar carpeta temporal para la extracción
                string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "temp_extract");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                string tempDcxPath = Path.Combine(tempDir, Path.GetFileName(dcxPath));
                File.Copy(dcxPath, tempDcxPath);

                // 2. Extraer usando WitchyBND de forma asíncrona (no congela la interfaz)
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "tools/WitchyBND/WitchyBND.exe",
                        Arguments = $"-s \"{tempDcxPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                await process.WaitForExitAsync();

                // 3. Obtener la carpeta extraída (WitchyBND quita la extensión)
                string unpackedDir = tempDcxPath.Replace(".dcx", "");

                if (Directory.Exists(unpackedDir))
                {
                    // 4. Leer FLVER y Texturas (TPF/DDS) con SoulsFormats
                    FlverModel miModelo = FlverLoader.LoadFromDirectory(unpackedDir);

                    if (miModelo != null)
                    {
                        // 5. Cargar en el control OpenTK (Subir a VRAM y Renderizar)
                        FlverViewport.LoadModel(miModelo);
                    }
                    else
                    {
                        MessageBox.Show("Error parseando el archivo FLVER o no se encontró malla.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error al cargar el modelo 3D:\n{ex.Message}", "Excepción", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}