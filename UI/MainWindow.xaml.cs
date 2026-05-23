using EldenRingArmorStudio.Core;
using EldenRingArmorStudio.ViewModels;
using Serilog;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EldenRingArmorStudio.UI
{
    public partial class MainWindow : Window
    {
        private ArmorDatabase _db;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => InitDatabase();
        }

        private void InitDatabase()
        {
            try
            {
                _db = new ArmorDatabase();
                Log.Information("DB cargada: {N} piezas", _db.Count());

                var items = _db.SearchArmor("").Select(r => new ArmorListItemViewModel
                {
                    EquipModelId = r.EquipModelId,
                    NameEn = r.NameEn,
                    NameEs = r.NameEs,
                    Category = r.Category,
                    SetName = r.SetName,
                    IsAltered = r.IsAltered,
                    IconIdM = r.IconIdM,
                    IconIdF = r.IconIdF,
                }).ToList();

                ArmorListBox.ItemsSource = items;
                ArmorListBox.SelectionChanged += ArmorListBox_SelectionChanged;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error inicializando la base de datos");
            }
        }

        private void ArmorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ArmorListBox.SelectedItem is not ArmorListItemViewModel item)
            {
                PropsPlaceholder.Visibility = Visibility.Visible;
                PropsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            PropsPlaceholder.Visibility = Visibility.Collapsed;
            PropsPanel.Visibility = Visibility.Visible;

            PropsIcon.Source = item.IconImageLarge;
            PropsNameEs.Text = item.NameEs;
            PropsNameEn.Text = item.NameEn;
            PropsCategory.Text = !string.IsNullOrWhiteSpace(item.SetName)
                                       ? $"Set: {item.SetName}  ·  {item.Category}"
                                       : item.Category;
            PropsModelId.Text = $"EquipModelId: {item.EquipModelId}"
                                 + (item.IconIdM != 0 ? $"  ·  IconIdM: {item.IconIdM}" : "")
                                 + (item.IconIdF != 0 ? $"  ·  IconIdF: {item.IconIdF}" : "");
        }
    }
}