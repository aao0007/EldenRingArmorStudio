using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace EldenRingArmorStudio.ViewModels
{
    public class ArmorListItemViewModel
    {
        private const string IconsDir = "data/icons";

        public string EquipModelId { get; init; } = "";
        public string NameEn { get; init; } = "";
        public string NameEs { get; init; } = "";
        public string Category { get; init; } = "";
        public string SetName { get; init; } = "";
        public bool IsAltered { get; init; }
        public int IconIdM { get; init; }
        public int IconIdF { get; init; }

        public string DisplayName =>
            !string.IsNullOrWhiteSpace(NameEs) ? NameEs : NameEn;

        public string SubLine =>
            string.IsNullOrWhiteSpace(SetName)
                ? $"{Category}  ·  {EquipModelId}"
                : $"{SetName}  ·  {Category}";

        /// <summary>
        /// BitmapImage para binding. Usa IconIdM primero, IconIdF como fallback.
        /// La URI se construye como absoluta para evitar el fallo de rutas relativas en WPF.
        /// </summary>
        public BitmapImage IconImage => LoadIcon(48);

        /// <summary>Mismo icono pero decodificado a 128px para el panel de detalle.</summary>
        public BitmapImage IconImageLarge => LoadIcon(128);

        private BitmapImage LoadIcon(int decodeWidth)
        {
            var path = ResolveIconPath();
            if (path is null) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                // Uri absoluta — evita el bug de WPF con rutas relativas
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = decodeWidth;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private string ResolveIconPath()
        {
            // Intenta IconIdM primero, luego IconIdF
            foreach (var id in new[] { IconIdM, IconIdF })
            {
                if (id == 0) continue;
                // Path.GetFullPath convierte la ruta relativa en absoluta
                // usando el directorio de trabajo actual (donde está el .exe)
                var full = Path.GetFullPath(
                    Path.Combine(IconsDir, $"MENU_Knowledge_{id}.png"));
                if (File.Exists(full)) return full;
            }
            return null;
        }
    }
}