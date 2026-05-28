using EldenRingArmorStudio.Core;
using System;
using System.IO;
using System.Windows;

namespace EldenRingArmorStudio.UI.Dialogs
{
    public partial class SettingsDialog : Window
    {
        public SettingsDialog()
        {
            InitializeComponent();
            LoadCurrentValues();
        }

        // ── Cargar valores actuales ───────────────────────────────────────────

        private void LoadCurrentValues()
        {
            var cfg = AppConfig.Instance;

            TxtWitchy.Text    = cfg.Tools.WitchyBndPath;
            TxtFlver.Text     = cfg.Tools.FlverEditorPath;
            TxtSmithbox.Text  = cfg.Tools.SmithboxPath;
            TxtModEngine.Text = cfg.ModEngine2.RootPath;
            TxtPartsLib.Text  = cfg.Project.PartsLibraryPath;
            TxtGridSize.Text  = cfg.Ui.GridSize.ToString();

            RbDark.IsChecked  =  cfg.Ui.DarkMode;
            RbLight.IsChecked = !cfg.Ui.DarkMode;
        }

        // ── Botones explorar ──────────────────────────────────────────────────

        private void OnBrowseWitchy(object sender, RoutedEventArgs e) =>
            BrowseExe("WitchyBND.exe", path => TxtWitchy.Text = path);

        private void OnBrowseFlver(object sender, RoutedEventArgs e) =>
            BrowseExe("FLVER_Editor.exe", path => TxtFlver.Text = path);

        private void OnBrowseSmithbox(object sender, RoutedEventArgs e) =>
            BrowseExe("Smithbox.exe", path => TxtSmithbox.Text = path);

        private void OnBrowseModEngine(object sender, RoutedEventArgs e) =>
            BrowseFolder("Carpeta raíz de ModEngine2", path => TxtModEngine.Text = path);

        private void OnBrowsePartsLib(object sender, RoutedEventArgs e) =>
            BrowseFolder("Carpeta de biblioteca de parts", path => TxtPartsLib.Text = path);

        private static void BrowseExe(string defaultName, Action<string> onSelected)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = $"Selecciona {defaultName}",
                Filter = "Ejecutable (*.exe)|*.exe|Todos (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                onSelected(dlg.FileName);
        }

        private static void BrowseFolder(string title, Action<string> onSelected)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
            if (dlg.ShowDialog() == true)
                onSelected(dlg.FolderName);
        }

        // ── Guardar ───────────────────────────────────────────────────────────

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var cfg = AppConfig.Instance;

            cfg.Tools.WitchyBndPath    = TxtWitchy.Text.Trim();
            cfg.Tools.FlverEditorPath  = TxtFlver.Text.Trim();
            cfg.Tools.SmithboxPath     = TxtSmithbox.Text.Trim();
            cfg.ModEngine2.RootPath    = TxtModEngine.Text.Trim();
            cfg.Project.PartsLibraryPath = TxtPartsLib.Text.Trim();

            if (int.TryParse(TxtGridSize.Text, out int gs) && gs > 0)
                cfg.Ui.GridSize = gs;

            bool wantDark = RbDark.IsChecked == true;

            // Cambiar tema en caliente si cambió
            if (wantDark != cfg.Ui.DarkMode)
                ThemeManager.Apply(wantDark ? AppTheme.Dark : AppTheme.Light);
            else
                cfg.Ui.DarkMode = wantDark;

            cfg.Save();

            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
