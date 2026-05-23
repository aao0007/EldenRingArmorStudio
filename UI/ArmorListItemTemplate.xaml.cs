using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace EldenRingArmorStudio.ViewModels
{
    /// <summary>
    /// ViewModel para cada fila de la lista de armaduras.
    /// Resuelve el icono desde data/icons/MENU_Knowledge_{IconIdM}.png (masculino)
    /// o data/icons/MENU_Knowledge_{IconIdF}.png (femenino) como fallback.
    /// </summary>
    public class ArmorListItemViewModel
    {
        private const string IconsDir = "data/icons";

        // ── Datos del registro ────────────────────────────────────────────────

        public string EquipModelId { get; init; } = "";
        public string NameEn { get; init; } = "";
        public string NameEs { get; init; } = "";
        public string Category { get; init; } = "";
        public string SetName { get; init; } = "";
        public bool IsAltered { get; init; }
        public int IconIdM { get; init; }
        public int IconIdF { get; init; }

        // ── Icono resuelto ────────────────────────────────────────────────────

        /// <summary>
        /// Ruta al PNG: primero intenta IconIdM, luego IconIdF como fallback.
        /// Devuelve null si ninguno existe en disco.
        /// </summary>
        public string? IconPath
        {
            get
            {
                if (IconIdM != 0)
                {
                    var p = Path.Combine(IconsDir, $"MENU_Knowledge_{IconIdM}.png");
                    if (File.Exists(p)) return p;
                }
                if (IconIdF != 0)
                {
                    var p = Path.Combine(IconsDir, $"MENU_Knowledge_{IconIdF}.png");
                    if (File.Exists(p)) return p;
                }
                return null;
            }
        }

        /// <summary>
        /// BitmapImage listo para binding en WPF. Null si no hay icono en disco.
        /// </summary>
        public BitmapImage? IconImage
        {
            get
            {
                var path = IconPath;
                if (path is null) return null;
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(Path.GetFullPath(path));
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 64;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch { return null; }
            }
        }

        // ── Display helpers ───────────────────────────────────────────────────

        /// <summary>Nombre a mostrar: español si existe, si no inglés.</summary>
        public string DisplayName =>
            !string.IsNullOrWhiteSpace(NameEs) ? NameEs : NameEn;

        /// <summary>Línea secundaria para la lista.</summary>
        public string SubLine =>
            string.IsNullOrWhiteSpace(SetName)
                ? $"{Category}  ·  {EquipModelId}"
                : $"{SetName}  ·  {Category}  ·  {EquipModelId}";
    }
}