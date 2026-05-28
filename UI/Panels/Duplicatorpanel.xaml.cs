using EldenRingArmorStudio.Core;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace EldenRingArmorStudio.UI.Panels
{
    public partial class DuplicatorPanel : UserControl
    {
        private ArmorDatabase _db;
        private DuplicatorService _svc;
        private string _sourceFile;
        private string _sourceSetName;
        private readonly Dictionary<string, ArmorRecord> _idToRecord = new();
        private readonly DispatcherTimer _searchTimer;

        public event Action<ArmorRecord> RecordSelected;
        public event Action<string> DuplicateCompleted;

        public DuplicatorPanel()
        {
            InitializeComponent();

            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); FilterIds(); };

            TxtIdSearch.GotFocus += (_, _) =>
            {
                if (TxtIdSearch.Text == (string)TxtIdSearch.Tag) TxtIdSearch.Text = "";
            };
            TxtIdSearch.LostFocus += (_, _) =>
            {
                if (string.IsNullOrEmpty(TxtIdSearch.Text))
                    TxtIdSearch.Text = (string)TxtIdSearch.Tag;
            };
            TxtIdSearch.Text = (string)TxtIdSearch.Tag;

            ComboCatFilter.SelectedIndex = 0;

            // Capturar clic directo en cada item de la lista
            // (SelectionChanged no se dispara si el item ya estaba seleccionado)
            LstIds.PreviewMouseDown += OnLstIdsPreviewMouseDown;
        }

        // ── Inicialización ────────────────────────────────────────────────────

        public void Initialize(ArmorDatabase db)
        {
            _db = db;
            _svc = new DuplicatorService();
            RefreshPackFolders();
            TxtDestino.Text = GetModPartsPath();
        }

        public void RefreshPackFolders()
        {
            string modParts = GetModPartsPath();
            ComboPack.Items.Clear();

            if (Directory.Exists(modParts))
            {
                ComboPack.Items.Add(modParts);
                foreach (var d in Directory.GetDirectories(
                    modParts, "*", System.IO.SearchOption.AllDirectories))
                    ComboPack.Items.Add(d);
            }

            if (ComboPack.Items.Count > 0)
                ComboPack.SelectedIndex = 0;
        }

        public void SetSourceFile(string filePath, ArmorRecord record = null)
        {
            _sourceFile = filePath;
            _sourceSetName = record?.SetName ?? "";

            if (!string.IsNullOrEmpty(filePath))
            {
                string fn = Path.GetFileName(filePath).ToLower();
                int catIdx = fn.StartsWith("hd") ? 0
                           : fn.StartsWith("bd") ? 1
                           : fn.StartsWith("am") ? 2
                           : fn.StartsWith("lg") ? 3 : 0;
                ComboCatFilter.SelectedIndex = catIdx;
            }

            string pack = ComboPack.Text;
            if (!string.IsNullOrEmpty(pack))
                TxtDestino.Text = pack;

            UpdateDuplicateButton();
            TxtStatus.Text = string.IsNullOrEmpty(filePath)
                ? "" : $"Origen: {Path.GetFileName(filePath)}";
        }

        // ── Filtrado ──────────────────────────────────────────────────────────

        private void OnCatFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadRecordsForCategory();
            FilterIds();
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e) => FilterIds();

        private void OnIdSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtIdSearch.Text == (string)TxtIdSearch.Tag) return;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void LoadRecordsForCategory()
        {
            if (_db == null) return;
            _idToRecord.Clear();
            foreach (var r in _db.SearchArmor("", GetSelectedCategory(), false))
            {
                string numId = new string(r.EquipModelId.Where(char.IsDigit).ToArray());
                _idToRecord[numId] = r;
            }
        }

        private void FilterIds()
        {
            if (_db == null) return;

            bool inclAltered = ChkIncludeAltered.IsChecked == true;
            string q = TxtIdSearch.Text.Trim();
            if (q == (string)TxtIdSearch.Tag) q = "";

            var source = _idToRecord.Values.AsEnumerable();
            if (!inclAltered) source = source.Where(r => !r.IsAltered);
            if (!string.IsNullOrEmpty(q))
                source = source.Where(r =>
                    (r.NameEs ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (r.NameEn ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    r.EquipModelId.Contains(q, StringComparison.OrdinalIgnoreCase));

            LstIds.Items.Clear();
            foreach (var r in source)
            {
                string numId = new string(r.EquipModelId.Where(char.IsDigit).ToArray());
                string altered = r.IsAltered ? " [Alt]" : "";
                string display = $"{numId}  —  " +
                    $"{(string.IsNullOrEmpty(r.NameEs) ? r.NameEn : r.NameEs)}{altered}";

                LstIds.Items.Add(new ListBoxItem { Content = display, Tag = numId });
            }
        }

        private string GetSelectedCategory() =>
            (ComboCatFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "Head";

        // ── Clic en item → emitir record SIEMPRE ─────────────────────────────

        /// <summary>
        /// PreviewMouseDown garantiza que cada clic en un item, aunque ya estuviera
        /// seleccionado, emita el record al ArmorInfoPanel.
        /// </summary>
        private void OnLstIdsPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            // Encontrar el ListBoxItem bajo el cursor
            var element = e.OriginalSource as DependencyObject;
            while (element != null && element is not ListBoxItem)
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);

            if (element is ListBoxItem li &&
                li.Tag is string numId &&
                _idToRecord.TryGetValue(numId, out var record))
            {
                RecordSelected?.Invoke(record);
            }
        }

        // SelectionChanged sigue siendo necesario para actualizar el botón Duplicar
        private void OnIdsSelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdateDuplicateButton();

        // ── Selección rápida ──────────────────────────────────────────────────

        private void OnSelectSet(object sender, RoutedEventArgs e)
        {
            foreach (ListBoxItem item in LstIds.Items)
            {
                bool inSet = string.IsNullOrEmpty(_sourceSetName)
                    || (item.Content?.ToString() ?? "")
                        .Contains(_sourceSetName, StringComparison.OrdinalIgnoreCase);
                item.IsSelected = inSet;
            }
            UpdateDuplicateButton();
        }

        private void OnClearSelection(object sender, RoutedEventArgs e)
        {
            LstIds.UnselectAll();
            UpdateDuplicateButton();
        }

        // ── Carpetas ──────────────────────────────────────────────────────────

        private void OnBrowsePackFolder(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            { Title = "Selecciona o crea una subcarpeta de pack" };
            if (dlg.ShowDialog() == true)
            {
                string path = dlg.FolderName;
                if (!ComboPack.Items.Contains(path))
                    ComboPack.Items.Insert(0, path);
                ComboPack.Text = path;
                TxtDestino.Text = path;
            }
        }

        private void OnBrowseDestino(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            { Title = "Selecciona la carpeta destino" };
            if (dlg.ShowDialog() == true)
                TxtDestino.Text = dlg.FolderName;
        }

        private void OnPackSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboPack.SelectedItem is string s && !string.IsNullOrEmpty(s))
                TxtDestino.Text = s;
        }

        // ── Duplicado ─────────────────────────────────────────────────────────

        private void UpdateDuplicateButton()
        {
            BtnDuplicate.IsEnabled =
                !string.IsNullOrEmpty(_sourceFile) &&
                File.Exists(_sourceFile) &&
                LstIds.SelectedItems.Count > 0 &&
                !string.IsNullOrEmpty(TxtDestino.Text);
        }

        private async void OnDuplicate(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_sourceFile) || !File.Exists(_sourceFile))
            {
                MessageBox.Show("Selecciona primero un archivo origen en el panel Archivos.",
                    "Sin origen", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedIds = LstIds.SelectedItems
                .Cast<ListBoxItem>()
                .Select(i => i.Tag?.ToString())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (selectedIds.Count == 0)
            {
                MessageBox.Show("Selecciona al menos un ID destino.",
                    "Sin selección", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string dest = TxtDestino.Text.Trim();
            if (string.IsNullOrEmpty(dest))
            {
                MessageBox.Show("Indica la carpeta de destino.",
                    "Sin destino", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try { Directory.CreateDirectory(dest); }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo crear la carpeta:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BtnDuplicate.IsEnabled = false;
            TxtStatus.Text = $"Duplicando {selectedIds.Count} archivo(s)…";

            var progress = new Progress<string>(msg => TxtStatus.Text = msg);
            var results = await _svc.DuplicateToIdsAsync(
                _sourceFile, dest, selectedIds, progress);

            int ok = results.Count(r => r.Success);
            int err = results.Count(r => !r.Success);

            TxtStatus.Text = err == 0
                ? $"✅ {ok} archivo(s) copiados en:\n{dest}"
                : $"✅ {ok} OK  ⚠ {err} errores\n" +
                  string.Join("\n", results.Where(r => !r.Success)
                      .Select(r => $"  • ID {r.TargetId}: {r.Error}"));

            BtnDuplicate.IsEnabled = true;
            if (ok > 0) DuplicateCompleted?.Invoke(dest);
        }

        private static string GetModPartsPath()
        {
            string root = AppConfig.Instance.ModEngine2.RootPath;
            return string.IsNullOrEmpty(root) ? "" : Path.Combine(root, "mod", "parts");
        }
    }
}