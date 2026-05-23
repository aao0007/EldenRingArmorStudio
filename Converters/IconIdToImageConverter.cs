using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace EldenRingArmorStudio.Converters
{
    public class IconIdToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;

            string iconStr = value.ToString();
            if (string.IsNullOrWhiteSpace(iconStr) || iconStr == "0") return null;

            if (int.TryParse(iconStr, out int iconId))
            {
                // Forzar 5 dígitos (ej: 10009 -> "10009", 310 -> "00310")
                string idFormateado = iconId.ToString("D5");
                string nombreImagen = $"MENU_Knowledge_{idFormateado}.png";

                
                string rutaAbsoluta = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "icons", nombreImagen);

                // 2. Si no existe, intentar subir a la raíz del proyecto de desarrollo
                if (!File.Exists(rutaAbsoluta))
                {
                    string raizProyecto = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\"));
                    rutaAbsoluta = Path.Combine(raizProyecto, "data", "icons", nombreImagen);
                }

                // 3. Si el archivo existe de verdad, lo cargamos de forma segura para WPF
                if (File.Exists(rutaAbsoluta))
                {
                    try
                    {
                        BitmapImage bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(rutaAbsoluta);
                        bmp.CacheOption = BitmapCacheOption.OnLoad; // Carga inmediata en RAM
                        bmp.EndInit();
                        bmp.Freeze(); // Crucial para que comparta el hilo entre paneles sin bloquearse
                        return bmp;
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null; // Si no encuentra nada, devuelve vacío (puedes poner aquí una imagen por defecto)
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}