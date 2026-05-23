using System;
using System.Globalization;
using System.Windows.Data;

namespace EldenRingArmorStudio.Converters
{
    /// <summary>
    /// Convierte null → false, cualquier otro valor → true.
    /// Usado en los DataTrigger para mostrar/ocultar el icono cuando IconImage es null.
    /// </summary>
    [ValueConversion(typeof(object), typeof(bool))]
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is not null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}