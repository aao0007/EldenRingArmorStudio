using EldenRingArmorStudio.Core;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EldenRingArmorStudio.UI.Panels
{
    /// <summary>
    /// Panel de árbol de archivos con:
    ///  - Pestaña ModEngine: muestra PackName → modelos (sin carpeta parts)
    ///  - Pestaña Biblioteca: carpeta libre de .partsbnd.dcx
    ///  - Selección múltiple con Ctrl/Shift
    ///  - Renombrar con F2 o doble clic en el nombre
    ///  - Borrado por lotes con Supr
    ///  - Menú contextual completo
    /// </summary>
    public partial class FileTreePanel : UserControl
    {
        public event Action<string> FileSelected;

        private static readonly Dictionary<string, string> PartIcons = new()
        {
            { "hd", "🪖" }, { "bd", "🥋" }, { "am", "🧤" }, { "lg", "👢" }
        };
        private const string DcxExt = ".partsbnd.dcx";

        // Items marcados para selección múltiple
        private readonly List<TreeViewItem> _multiSelected = new();
        private TreeView ActiveTree =>
            (TabFiles.SelectedItem as TabItem)?.Name == "TabMod"
                ? TreeMod : TreeProject;

        public FileTreePanel()
        {
            InitializeComponent();
            SetupPlaceholder();
            Loaded += (_, _) => Refresh();
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        public void Refresh()
        {
            RefreshModEngineTree();
            RefreshBibliotecaTree();
        }

        public void RefreshModTab() => RefreshModEngineTree();

        /// <summary>
        /// Construye el árbol ModEngine:
        ///   ModEngine/mod/PackName/ → hd_m_1010.partsbnd.dcx (sin mostrar /parts)
        /// </summary>
        private void RefreshModEngineTree()
        {
            TreeProject.Items.Clear();
            string modRoot = AppConfig.Instance.ModEngine2.RootPath;
            if (string.IsNullOrEmpty(modRoot))
            {
                TreeProject.Items.Add(MakeLeaf("(configura ModEngine2 en ⚙ Configuración)", null));
                return;
            }

            string modDir = Path.Combine(modRoot, "mod");
            if (!Directory.Exists(modDir))
            {
                TreeProject.Items.Add(MakeLeaf($"(no existe: {modDir})", null));
                return;
            }

            foreach (var packDir in Directory.GetDirectories(modDir)
                                             .OrderBy(d => Path.GetFileName(d)))
            {
                string packName = Path.GetFileName(packDir);
                var packNode = MakeNode($"📦 {packName}", null); // nodo sin ruta = carpeta pack
                packNode.Tag = packDir; // guardamos la ruta del pack para contexto

                // Buscar modelos en packDir/parts/
                string partsPath = Path.Combine(packDir, "parts");
                if (Directory.Exists(partsPath))
                {
                    foreach (var file in Directory.GetFiles(partsPath, "*.partsbnd.dcx")
                                                  .OrderBy(f => f))
                    {
                        string fn = Path.GetFileName(file);
                        string pfx = fn.Split('_')[0].ToLower();
                        string icon = PartIcons.GetValueOrDefault(pfx, "📦");
                        packNode.Items.Add(MakeLeaf($"{icon} {fn}", file));
                    }
                }

                if (packNode.Items.Count == 0)
                    packNode.Items.Add(MakeLeaf("(carpeta vacía)", null));

                TreeProject.Items.Add(packNode);
            }
        }

        private void RefreshBibliotecaTree()
        {
            TreeMod.Items.Clear();
            string libPath = AppConfig.Instance.Project.PartsLibraryPath;
            if (string.IsNullOrEmpty(libPath) || !Directory.Exists(libPath))
            {
                TreeMod.Items.Add(MakeLeaf("(configura la biblioteca en ⚙ Configuración)", null));
                return;
            }
            PopulateGenericTree(TreeMod.Items, libPath);
        }

        private void PopulateGenericTree(ItemCollection parent, string dir)
        {
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir)
                    .OrderBy(e => File.Exists(e))
                    .ThenBy(e => Path.GetFileName(e).ToLowerInvariant()))
                {
                    if (Directory.Exists(entry))
                    {
                        var node = MakeNode($"📁 {Path.GetFileName(entry)}", entry);
                        PopulateGenericTree(node.Items, entry);
                        parent.Add(node);
                    }
                    else
                    {
                        string name = Path.GetFileName(entry);
                        if (!name.ToLower().EndsWith(DcxExt)) continue;
                        string pfx = name.Split('_')[0].ToLower();
                        parent.Add(MakeLeaf(
                            $"{PartIcons.GetValueOrDefault(pfx, "📦")} {name}", entry));
                    }
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Error árbol: {Dir}", dir); }
        }

        // ── Mouse / selección ─────────────────────────────────────────────────

        private void OnTreeMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Soporte de selección múltiple con Ctrl
            if (e.OriginalSource is not DependencyObject src) return;

            var clickedItem = GetTreeViewItem(src);
            if (clickedItem == null) return;

            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            if (ctrl)
            {
                // Toggle
                if (_multiSelected.Contains(clickedItem))
                {
                    _multiSelected.Remove(clickedItem);
                    SetHighlight(clickedItem, false);
                }
                else
                {
                    _multiSelected.Add(clickedItem);
                    SetHighlight(clickedItem, true);
                }
                e.Handled = true;
            }
            else if (!shift)
            {
                // Clic normal: limpiar selección múltiple previa
                foreach (var it in _multiSelected) SetHighlight(it, false);
                _multiSelected.Clear();
            }

            // Un solo clic solo selecciona el item visualmente.
            // El doble clic en OnTreeDoubleClick es quien emite FileSelected.
        }

        private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject src) return;
            var item = GetTreeViewItem(src);
            if (item == null) return;

            if (item.Tag is string path &&
                !string.IsNullOrEmpty(path) &&
                File.Exists(path) &&
                path.ToLower().EndsWith(".partsbnd.dcx"))
            {
                // Doble clic en .dcx → cargar en visor
                FileSelected?.Invoke(path);
                e.Handled = true;
            }
            // Si es carpeta, el TreeView expande/colapsa por defecto — no interferir
        }

        private void OnTreeKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2)
            {
                var tree = sender as TreeView;
                if (tree?.SelectedItem is TreeViewItem item && item.Tag is string path)
                    StartRename(item, path);
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelected(sender as TreeView);
            }
        }

        // ── Renombrar ─────────────────────────────────────────────────────────

        private void StartRename(TreeViewItem item, string path)
        {
            string oldName = Path.GetFileName(path);
            bool isDir = Directory.Exists(path);

            // Cambiar el header por un TextBox editable
            var tb = new TextBox
            {
                Text = isDir ? oldName : Path.GetFileNameWithoutExtension(oldName),
                FontSize = 11,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = item.Foreground,
                BorderThickness = new Thickness(0, 0, 0, 1),
                MinWidth = 120
            };

            item.Header = tb;
            tb.Focus();
            tb.SelectAll();

            tb.LostFocus += (_, _) => CommitRename(item, tb.Text.Trim(), path, isDir);
            tb.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter) { CommitRename(item, tb.Text.Trim(), path, isDir); ke.Handled = true; }
                if (ke.Key == Key.Escape) { CancelRename(item, path, isDir); ke.Handled = true; }
            };
        }

        private void CommitRename(TreeViewItem item, string newBaseName, string oldPath, bool isDir)
        {
            if (string.IsNullOrWhiteSpace(newBaseName))
            {
                CancelRename(item, oldPath, isDir);
                return;
            }

            string dir = Path.GetDirectoryName(oldPath)!;
            string newName = isDir ? newBaseName
                : newBaseName + Path.GetExtension(oldPath);
            if (!newName.EndsWith(".partsbnd.dcx", StringComparison.OrdinalIgnoreCase) && !isDir)
                newName += ".partsbnd.dcx";

            string newPath = Path.Combine(dir, newName);

            try
            {
                if (oldPath == newPath) { CancelRename(item, oldPath, isDir); return; }
                if (isDir) Directory.Move(oldPath, newPath);
                else File.Move(oldPath, newPath, overwrite: false);

                string pfx = newName.Split('_')[0].ToLower();
                string icon = isDir ? "📦"
                    : PartIcons.GetValueOrDefault(pfx, "📦");
                item.Header = $"{icon} {newName}";
                item.Tag = newPath;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al renombrar:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                CancelRename(item, oldPath, isDir);
            }
        }

        private static void CancelRename(TreeViewItem item, string path, bool isDir)
        {
            string name = Path.GetFileName(path);
            string pfx = name.Split('_')[0].ToLower();
            string icon = isDir ? "📦" : FileTreePanel.GetIconFor(pfx, isDir);
            item.Header = $"{icon} {name}";
        }

        private static string GetIconFor(string prefix, bool isDir)
        {
            if (isDir) return "📦";
            return new Dictionary<string, string>
            {
                {"hd","🪖"},{"bd","🥋"},{"am","🧤"},{"lg","👢"}
            }.GetValueOrDefault(prefix, "📦");
        }

        // ── Borrado ───────────────────────────────────────────────────────────

        private void DeleteSelected(TreeView tree)
        {
            // Recopilar: los multi-seleccionados + el nodo activo del árbol
            var toDelete = new List<TreeViewItem>(_multiSelected);

            if (tree?.SelectedItem is TreeViewItem sel && !toDelete.Contains(sel))
                toDelete.Add(sel);

            // Filtrar solo los que tienen un path real de archivo o carpeta
            var paths = toDelete
                .Where(i => i.Tag is string p &&
                    (File.Exists(p) || Directory.Exists(p)))
                .Select(i => (item: i, path: (string)i.Tag))
                .ToList();

            if (paths.Count == 0) return;

            string msg = paths.Count == 1
                ? $"¿Eliminar:\n{Path.GetFileName(paths[0].path)}?"
                : $"¿Eliminar {paths.Count} elementos?";

            var result = MessageBox.Show(msg, "Confirmar eliminación",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            int ok = 0, err = 0;
            foreach (var (item, path) in paths)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else File.Delete(path);

                    // Quitar del árbol
                    RemoveItemFromTree(tree, item);
                    ok++;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error eliminando {P}", path);
                    err++;
                }
            }

            _multiSelected.Clear();

            if (err > 0)
                MessageBox.Show($"{ok} eliminados, {err} errores.", "Resultado",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static void RemoveItemFromTree(TreeView tree, TreeViewItem target)
        {
            if (tree.Items.Contains(target)) { tree.Items.Remove(target); return; }
            foreach (TreeViewItem node in tree.Items)
                if (RemoveFromNode(node, target)) return;
        }

        private static bool RemoveFromNode(TreeViewItem parent, TreeViewItem target)
        {
            if (parent.Items.Contains(target)) { parent.Items.Remove(target); return true; }
            foreach (TreeViewItem child in parent.Items)
                if (RemoveFromNode(child, target)) return true;
            return false;
        }

        // ── Menú contextual ───────────────────────────────────────────────────

        private void OnContextMenu(object sender, ContextMenuEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject src) return;
            var item = GetTreeViewItem(src);
            var tree = sender as TreeView;
            string path = item?.Tag as string;

            var menu = new ContextMenu();

            // Refrescar siempre
            Add(menu, "🔄 Actualizar", _ => Refresh());
            menu.Items.Add(new Separator());

            if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
            {
                Add(menu, "✏ Renombrar (F2)", _ => StartRename(item!, path, Directory.Exists(path)));
                Add(menu, "📋 Copiar ruta", _ => Clipboard.SetText(path));
                menu.Items.Add(new Separator());

                // Borrado
                int selCount = _multiSelected.Count + (item != null && !_multiSelected.Contains(item) ? 1 : 0);
                string delLabel = selCount > 1
                    ? $"🗑 Eliminar {selCount} elementos"
                    : $"🗑 Eliminar {Path.GetFileName(path)}";
                Add(menu, delLabel, _ => DeleteSelected(tree));
            }

            // Abrir en explorador
            if (!string.IsNullOrEmpty(path))
            {
                string dir = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
                Add(menu, "📂 Abrir en explorador", _ =>
                    System.Diagnostics.Process.Start("explorer.exe", dir));
            }

            (sender as FrameworkElement)!.ContextMenu = menu;
        }

        // Sobrecarga estática usada desde el menú contextual — busca el FileTreePanel
        // subiendo por el árbol visual desde el TreeViewItem
        private static void StartRename(TreeViewItem item, string path, bool isDir)
        {
            DependencyObject current = item;
            while (current != null)
            {
                if (current is FileTreePanel panel) { panel.StartRename(item, path); return; }
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
        }

        private static void Add(ContextMenu menu, string header, Action<object> handler)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (s, _) => handler(s);
            menu.Items.Add(mi);
        }

        // ── Filtro de búsqueda ────────────────────────────────────────────────

        private void OnFilterChanged(object sender, TextChangedEventArgs e)
        {
            string text = TxtFilter.Text.Trim();
            if (text == (string)TxtFilter.Tag) text = "";
            ApplyFilter(TreeProject.Items, text.ToLower());
            ApplyFilter(TreeMod.Items, text.ToLower());
        }

        private static bool ApplyFilter(ItemCollection items, string text)
        {
            bool any = false;
            foreach (TreeViewItem it in items)
            {
                bool child = ApplyFilter(it.Items, text);
                bool self = string.IsNullOrEmpty(text)
                    || it.Header?.ToString()?.ToLower().Contains(text) == true;
                bool vis = self || child;
                it.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                any |= vis;
            }
            return any;
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

        // ── Highlight selección múltiple ─────────────────────────────────────

        private static void SetHighlight(TreeViewItem item, bool on)
        {
            item.Background = on
                ? new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(80, 74, 127, 204))
                : System.Windows.Media.Brushes.Transparent;
        }

        // ── Helpers de nodos ──────────────────────────────────────────────────

        private static TreeViewItem MakeNode(string header, string path) =>
            new() { Header = header, Tag = path, IsExpanded = false };

        private static TreeViewItem MakeLeaf(string header, string path) =>
            new() { Header = header, Tag = path };

        // Sube por el árbol visual hasta encontrar un TreeViewItem
        private static TreeViewItem GetTreeViewItem(DependencyObject source)
        {
            while (source != null && source is not TreeViewItem)
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            return source as TreeViewItem;
        }

        // ── Placeholder ───────────────────────────────────────────────────────

        private void SetupPlaceholder()
        {
            TxtFilter.Text = (string)TxtFilter.Tag;
            TxtFilter.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(150, 150, 150));

            TxtFilter.GotFocus += (_, _) =>
            {
                if (TxtFilter.Text == (string)TxtFilter.Tag)
                {
                    TxtFilter.Text = "";
                    TxtFilter.Foreground = System.Windows.Media.Brushes.Black;
                }
            };
            TxtFilter.LostFocus += (_, _) =>
            {
                if (string.IsNullOrEmpty(TxtFilter.Text))
                {
                    TxtFilter.Text = (string)TxtFilter.Tag;
                    TxtFilter.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(150, 150, 150));
                }
            };
        }
    }
}