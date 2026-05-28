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
        // ── Estado ────────────────────────────────────────────────────────────
        private ArmorDatabase _db;
        private DuplicatorService _svc;
        private string _sourceFile;
        private string _sourceSetName;
        private string _currentPackBase = "";

        // Todos los records de la categoría actual (sin filtrar)
        private List<ArmorRecord> _allCategoryRecords = new();

        // IDs seleccionados PERSISTENTES (se mantienen aunque se filtre)
        private readonly HashSet<string> _selectedIds = new();

        // Mapa numId → record
        private readonly Dictionary<string, ArmorRecord> _idToRecord = new();

        private readonly DispatcherTimer _searchTimer;
        private bool _suppressSelectionChanged = false;

        // ── Eventos ───────────────────────────────────────────────────────────
        public event Action<ArmorRecord> RecordSelected;
        public event Action<string> DuplicateCompleted;

        public DuplicatorPanel()
        {
            InitializeComponent();

            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RebuildList(); };

            // Placeholder buscador
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

            // Placeholder nueva carpeta
            TxtNewPack.GotFocus += (_, _) =>
            {
                if (TxtNewPack.Text == (string)TxtNewPack.Tag) TxtNewPack.Text = "";
            };
            TxtNewPack.LostFocus += (_, _) =>
            {
                if (string.IsNullOrEmpty(TxtNewPack.Text))
                    TxtNewPack.Text = (string)TxtNewPack.Tag;
            };
            TxtNewPack.Text = (string)TxtNewPack.Tag;

            ComboCatFilter.SelectedIndex = 0;
            LstIds.PreviewMouseDown += OnLstIdsPreviewMouseDown;
        }

        // ── Inicialización ────────────────────────────────────────────────────

        public void Initialize(ArmorDatabase db)
        {
            _db = db;
            _svc = new DuplicatorService();
            RefreshPackFolders();
        }

        /// <summary>
        /// Lee las subcarpetas de ModEngine/mod y las muestra en LstPacks.
        /// Solo muestra carpetas que contengan una subcarpeta "parts" o que
        /// estén vacías (packs recién creados también se listan).
        /// </summary>
        public void RefreshPackFolders()
        {
            LstPacks.Items.Clear();

            string modRoot = AppConfig.Instance.ModEngine2.RootPath;
            if (string.IsNullOrEmpty(modRoot)) return;

            string modDir = Path.Combine(modRoot, "mod");
            if (!Directory.Exists(modDir)) return;

            foreach (var dir in Directory.GetDirectories(modDir)
                                         .OrderBy(d => Path.GetFileName(d)))
            {
                string name = Path.GetFileName(dir);
                // Mostrar todas las subcarpetas de mod/ como packs potenciales
                LstPacks.Items.Add(name);
            }
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

            UpdateDuplicateButton();
            TxtStatus.Text = string.IsNullOrEmpty(filePath)
                ? "" : $"Origen: {Path.GetFileName(filePath)}";
        }

        // ── Selección de pack ─────────────────────────────────────────────────

        private void OnPackListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstPacks.SelectedItem is string name)
            {
                // Limpiar el textbox de nueva carpeta
                TxtNewPack.Text = (string)TxtNewPack.Tag;

                string modRoot = AppConfig.Instance.ModEngine2.RootPath;
                _currentPackBase = Path.Combine(modRoot, "mod", name);
                UpdateDestinoLabel();
            }
        }

        private void OnNewPackTextChanged(object sender, TextChangedEventArgs e)
        {
            string txt = TxtNewPack.Text.Trim();
            if (string.IsNullOrEmpty(txt) || txt == (string)TxtNewPack.Tag)
                return;

            // Desmarcar selección en la lista de packs existentes
            LstPacks.SelectedItem = null;

            string modRoot = AppConfig.Instance.ModEngine2.RootPath;
            if (string.IsNullOrEmpty(modRoot)) return;

            _currentPackBase = Path.Combine(modRoot, "mod", txt);
            UpdateDestinoLabel();
        }

        private void UpdateDestinoLabel()
        {
            if (string.IsNullOrEmpty(_currentPackBase))
            {
                TxtDestinoResuelto.Text = "→ (sin selección)";
                BtnClear.IsEnabled = false;
            }
            else
            {
                string partsDir = DuplicatorService.ResolvePartsDir(_currentPackBase);
                TxtDestinoResuelto.Text = $"→ {partsDir}";

                BtnClear.IsEnabled = Directory.Exists(partsDir) &&
                    Directory.GetFiles(partsDir, "*.partsbnd.dcx").Length > 0;
            }
            UpdateDuplicateButton();
        }

        // ── Carga de records y filtrado ───────────────────────────────────────

        private void OnCatFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadRecordsForCategory();
            RebuildList();
        }

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
            _allCategoryRecords = _db.SearchArmor("", GetSelectedCategory(), false)
                .OrderBy(r =>
                {
                    string n = new string(r.EquipModelId.Where(char.IsDigit).ToArray());
                    return int.TryParse(n, out int v) ? v : int.MaxValue;
                })
                .ToList();

            foreach (var r in _allCategoryRecords)
            {
                string numId = new string(r.EquipModelId.Where(char.IsDigit).ToArray());
                _idToRecord[numId] = r;
            }
        }

        /// <summary>
        /// Reconstruye la lista visible aplicando el filtro de búsqueda,
        /// pero MANTIENE los IDs en _selectedIds marcados como seleccionados.
        /// </summary>
        private void RebuildList()
        {
            if (_db == null) return;

            string q = TxtIdSearch.Text.Trim();
            if (q == (string)TxtIdSearch.Tag) q = "";

            var source = _allCategoryRecords.AsEnumerable();
            if (!string.IsNullOrEmpty(q))
                source = source.Where(r =>
                    (r.NameEs ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (r.NameEn ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    r.EquipModelId.Contains(q, StringComparison.OrdinalIgnoreCase));

            _suppressSelectionChanged = true;
            LstIds.Items.Clear();

            foreach (var r in source)
            {
                string numId = new string(r.EquipModelId.Where(char.IsDigit).ToArray());
                string altered = r.IsAltered ? " [Alt]" : "";
                string label = string.IsNullOrEmpty(r.NameEs) ? r.NameEn : r.NameEs;

                var item = new ListBoxItem
                {
                    Content = $"{numId}  —  {label}{altered}",
                    Tag = numId,
                    IsSelected = _selectedIds.Contains(numId)
                };
                LstIds.Items.Add(item);
            }

            _suppressSelectionChanged = false;
            UpdateDuplicateButton();
        }

        private string GetSelectedCategory() =>
            (ComboCatFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "Head";

        // ── Clic en item → emitir record + persistir selección ────────────────

        private void OnLstIdsPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            var element = e.OriginalSource as DependencyObject;
            while (element != null && element is not ListBoxItem)
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);

            if (element is ListBoxItem li && li.Tag is string numId)
            {
                // Emitir info al ArmorInfoPanel
                if (_idToRecord.TryGetValue(numId, out var record))
                    RecordSelected?.Invoke(record);
            }
        }

        private void OnIdsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionChanged) return;

            // Sincronizar _selectedIds con el estado actual de la lista
            foreach (var added in e.AddedItems.Cast<ListBoxItem>()
                                   .Where(i => i.Tag is string))
                _selectedIds.Add((string)((ListBoxItem)added).Tag);

            foreach (var removed in e.RemovedItems.Cast<ListBoxItem>()
                                     .Where(i => i.Tag is string))
                _selectedIds.Remove((string)((ListBoxItem)removed).Tag);

            UpdateDuplicateButton();
        }

        // ── Selección rápida ──────────────────────────────────────────────────

        private void OnSelectSet(object sender, RoutedEventArgs e)
        {
            _suppressSelectionChanged = true;

            foreach (ListBoxItem item in LstIds.Items)
            {
                bool inSet = string.IsNullOrEmpty(_sourceSetName)
                    || (item.Content?.ToString() ?? "")
                        .Contains(_sourceSetName, StringComparison.OrdinalIgnoreCase);

                item.IsSelected = inSet;
                string id = item.Tag as string;
                if (id == null) continue;
                if (inSet) _selectedIds.Add(id);
                else _selectedIds.Remove(id);
            }

            _suppressSelectionChanged = false;
            UpdateDuplicateButton();
        }

        private void OnClearSelection(object sender, RoutedEventArgs e)
        {
            _selectedIds.Clear();
            _suppressSelectionChanged = true;
            LstIds.UnselectAll();
            _suppressSelectionChanged = false;
            UpdateDuplicateButton();
        }

        // ── Duplicado ─────────────────────────────────────────────────────────

        private void UpdateDuplicateButton()
        {
            BtnDuplicate.IsEnabled =
                !string.IsNullOrEmpty(_sourceFile) &&
                File.Exists(_sourceFile) &&
                _selectedIds.Count > 0 &&
                !string.IsNullOrEmpty(_currentPackBase);
        }

        private async void OnDuplicate(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_sourceFile) || !File.Exists(_sourceFile))
            {
                MessageBox.Show("Selecciona primero un archivo origen en el panel Archivos.",
                    "Sin origen", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_selectedIds.Count == 0)
            {
                MessageBox.Show("Selecciona al menos un ID destino.",
                    "Sin selección", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(_currentPackBase))
            {
                MessageBox.Show("Selecciona o escribe una carpeta de destino.",
                    "Sin destino", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Determinar género: m o f
            bool isFemale = RbFemale.IsChecked == true;
            bool withAltered = ChkAltered.IsChecked == true;

            BtnDuplicate.IsEnabled = false;
            BtnClear.IsEnabled = false;

            var allIds = new List<string>(_selectedIds);
            TxtStatus.Text = $"Duplicando {allIds.Count} slot(s)…";

            var progress = new Progress<string>(msg => TxtStatus.Text = msg);

            var results = await _svc.DuplicateToIdsAsync(
                _sourceFile, _currentPackBase, allIds,
                isFemale, withAltered, progress);

            int ok = results.Count(r => r.Success);
            int err = results.Count(r => !r.Success);

            string destDir = DuplicatorService.ResolvePartsDir(_currentPackBase);
            TxtStatus.Text = err == 0
                ? $"✅ {ok} archivo(s) copiados en:\n{destDir}"
                : $"✅ {ok} OK  ⚠ {err} errores\n" +
                  string.Join("\n", results.Where(r => !r.Success)
                      .Select(r => $"  • {r.TargetId}: {r.Error}"));

            // Si la carpeta es nueva, refrescar la lista de packs
            if (!LstPacks.Items.Contains(Path.GetFileName(_currentPackBase)))
                RefreshPackFolders();

            UpdateDestinoLabel();
            BtnDuplicate.IsEnabled = true;

            if (ok > 0) DuplicateCompleted?.Invoke(destDir);
        }

        // ── Vaciar carpeta parts ──────────────────────────────────────────────

        private void OnClearFolder(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPackBase)) return;

            string partsDir = DuplicatorService.ResolvePartsDir(_currentPackBase);
            if (!Directory.Exists(partsDir)) return;

            int count = Directory.GetFiles(partsDir, "*.partsbnd.dcx").Length;
            if (count == 0)
            {
                MessageBox.Show("La carpeta parts ya está vacía.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"¿Eliminar {count} archivo(s) .partsbnd.dcx de:\n{partsDir}?",
                "Confirmar vaciado", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            int deleted = DuplicatorService.ClearPartsFolder(_currentPackBase);
            TxtStatus.Text = $"🗑 {deleted} archivo(s) eliminados.";
            UpdateDestinoLabel();
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e) => RebuildList();
    }
}