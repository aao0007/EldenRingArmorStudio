using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EldenRingArmorStudio.UI.Panels;

/// <summary>
/// Panel con dos árboles: biblioteca personal y mod/parts activo.
/// Doble clic en un .partsbnd.dcx emite FileSelected.
/// </summary>
public partial class FileTreePanel : UserControl
{
    public event Action<string> FileSelected;

    private static readonly Dictionary<string, string> PartIcons = new()
    {
        {"hd","🪖"},{"bd","🥋"},{"am","🧤"},{"lg","👢"}
    };
    private const string DcxExt = ".partsbnd.dcx";

    public FileTreePanel()
    {
        InitializeComponent();
        SetupSearchPlaceholder();
        Loaded += (_, _) => Refresh();
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    public void Refresh()
    {
        RefreshTree(TreeProject, Core.AppConfig.Instance.Project.PartsLibraryPath);
        RefreshModTab();
    }

    public void RefreshModTab()
    {
        var root = Core.AppConfig.Instance.ModEngine2.RootPath;
        string modParts = null;
        if (!string.IsNullOrEmpty(root))
        {
            modParts = Path.Combine(root, "mod", "parts");
            Directory.CreateDirectory(modParts);
        }
        RefreshTree(TreeMod, modParts ?? "");
    }

    private void RefreshTree(TreeView tree, string rootPath)
    {
        tree.Items.Clear();
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
        {
            tree.Items.Add(MakeLeaf("(ruta no configurada)", null));
            return;
        }
        PopulateTree(tree.Items, rootPath);
    }

    private void PopulateTree(ItemCollection parent, string dir)
    {
        try
        {
            var entries = Directory.EnumerateFileSystemEntries(dir)
                .OrderBy(e => File.Exists(e))
                .ThenBy(e => Path.GetFileName(e).ToLowerInvariant());

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry))
                {
                    var node = MakeNode($"📁 {Path.GetFileName(entry)}", entry);
                    PopulateTree(node.Items, entry);
                    parent.Add(node);
                }
                else
                {
                    var name = Path.GetFileName(entry);
                    if (!name.ToLower().EndsWith(DcxExt) || !name.ToLower().Contains(".partsbnd")) continue;
                    var prefix = name.Split('_')[0].ToLower();
                    var icon = PartIcons.GetValueOrDefault(prefix, "📦");
                    parent.Add(MakeLeaf($"{icon} {name}", entry));
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (Exception ex) { Log.Warning(ex, "Error poblando árbol: {Dir}", dir); }
    }

    // ── Helpers de nodos ──────────────────────────────────────────────────────

    private static TreeViewItem MakeNode(string header, string path)
    {
        var item = new TreeViewItem { Header = header, Tag = path };
        return item;
    }

    private static TreeViewItem MakeLeaf(string header, string path)
    {
        var item = new TreeViewItem { Header = header, Tag = path, IsExpanded = false };
        return item;
    }

    // ── Eventos ───────────────────────────────────────────────────────────────

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var tree = sender as TreeView;
        if (tree?.SelectedItem is not TreeViewItem item) return;
        var path = item.Tag as string;
        if (!string.IsNullOrEmpty(path) && path.ToLower().EndsWith(DcxExt))
            FileSelected?.Invoke(path);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        var text = TxtFilter.Text.Trim();
        if (text == (string)TxtFilter.Tag) text = "";
        ApplyFilter(TreeProject.Items, text.ToLower());
        ApplyFilter(TreeMod.Items, text.ToLower());
    }

    private static bool ApplyFilter(ItemCollection items, string text)
    {
        bool anyVisible = false;
        foreach (TreeViewItem item in items)
        {
            bool childVisible = ApplyFilter(item.Items, text);
            bool selfMatch = string.IsNullOrEmpty(text)
                             || item.Header?.ToString()?.ToLower().Contains(text) == true;
            bool visible = selfMatch || childVisible;
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            anyVisible |= visible;
        }
        return anyVisible;
    }

    private void OnContextMenu(object sender, ContextMenuEventArgs e)
    {
        var tree = sender as TreeView;
        var item = tree?.SelectedItem as TreeViewItem;
        var path = item?.Tag as string;

        var menu = new ContextMenu();

        var miRefresh = new MenuItem { Header = "🔄 Actualizar" };
        miRefresh.Click += (_, _) => Refresh();
        menu.Items.Add(miRefresh);

        if (!string.IsNullOrEmpty(path) && path.ToLower().EndsWith(DcxExt))
        {
            menu.Items.Add(new Separator());
            var miDel = new MenuItem { Header = "🗑 Eliminar archivo" };
            miDel.Click += (_, _) => DeleteFile(path);
            menu.Items.Add(miDel);
        }

        (sender as FrameworkElement)!.ContextMenu = menu;
    }

    private void DeleteFile(string path)
    {
        var result = MessageBox.Show(
            $"¿Eliminar {Path.GetFileName(path)}?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            File.Delete(path);
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Placeholder TextBox ───────────────────────────────────────────────────

    private void SetupSearchPlaceholder()
    {
        TxtFilter.Text = (string)TxtFilter.Tag;
        TxtFilter.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150));

        TxtFilter.GotFocus += (_, _) =>
        {
            if (TxtFilter.Text == (string)TxtFilter.Tag)
            {
                TxtFilter.Text = "";
                TxtFilter.Foreground = System.Windows.Media.Brushes.Black; // Texto negro al escribir
            }
        };
        TxtFilter.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(TxtFilter.Text))
            {
                TxtFilter.Text = (string)TxtFilter.Tag;
                TxtFilter.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)); // Gris al perder el foco
            }
        };
    }
}