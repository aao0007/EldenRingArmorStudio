using EldenRingArmorStudio.Core;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace EldenRingArmorStudio.UI.Panels
{
    public partial class QuickInfoPanel : UserControl
    {
        private ArmorRecord _current;
        private string _currentFile;

        public event Action<string> CsvLoadRequested;

        public QuickInfoPanel()
        {
            InitializeComponent();
            ShowEmpty();
        }

        // ── API pública ───────────────────────────────────────────────────────

        public void ShowRecord(ArmorRecord record)
        {
            _current = record;
            if (record == null) { ShowEmpty(); return; }

            TxtName.Text = string.IsNullOrWhiteSpace(record.NameEs)
                ? record.NameEn : record.NameEs;

            string numId = new string(record.EquipModelId
                .Where(char.IsDigit).ToArray());
            TxtId.Text = $"#{numId}";

            TxtCat.Text = record.Category switch
            {
                "Head" => "🪖 Cabeza",
                "Body" => "🥋 Cuerpo",
                "Arms" => "🧤 Brazos",
                "Legs" => "👢 Piernas",
                _ => record.Category
            };

            TxtGender.Text = "?";
            TxtFile.Text = string.IsNullOrWhiteSpace(record.FileName)
                ? "—" : record.FileName;

            RefreshToolButtons();
        }

        public void SetCurrentFile(string filePath)
        {
            _currentFile = filePath;
            RefreshToolButtons();
        }

        // ── Botones ───────────────────────────────────────────────────────────

        private void RefreshToolButtons()
        {
            bool hasFile = !string.IsNullOrEmpty(_currentFile)
                           && File.Exists(_currentFile);
            BtnWitchy.IsEnabled = hasFile;
            BtnFlver.IsEnabled = hasFile;
        }

        private void OnWitchyClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFile)) return;
            string witchy = AppConfig.Get("tools.witchybnd_path");
            if (!File.Exists(witchy))
            {
                MessageBox.Show(
                    "No se encontró WitchyBND.\nConfigura la ruta en data/settings.json → tools.witchybnd_path",
                    "Falta herramienta", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = witchy,
                    Arguments = $"\"{_currentFile}\"",
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error lanzando WitchyBND:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnFlverClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFile)) return;
            string flverExe = AppConfig.Instance.Tools.FlverEditorPath;
            if (!File.Exists(flverExe))
            {
                MessageBox.Show(
                    "No se encontró FLVER Editor.\nConfigura la ruta en data/settings.json → tools.flver_editor_path",
                    "Falta herramienta", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = flverExe,
                    Arguments = $"\"{_currentFile}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error lanzando FLVER Editor:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnLoadCsv(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Cargar CSV de armaduras",
                Filter = "CSV (*.csv)|*.csv"
            };
            if (dlg.ShowDialog() == true)
                CsvLoadRequested?.Invoke(dlg.FileName);
        }

        // ── Estado vacío ──────────────────────────────────────────────────────

        private void ShowEmpty()
        {
            TxtName.Text = "—";
            TxtId.Text = "—";
            TxtCat.Text = "—";
            TxtGender.Text = "—";
            TxtFile.Text = "—";
            BtnWitchy.IsEnabled = false;
            BtnFlver.IsEnabled = false;
        }
    }
}