using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ChronoLog
{
    // Converts a hex color string (e.g., "#FF6B6B") to a contrasting Brush (Black or White)
    public class ContrastForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                string? hex = value as string;
                if (string.IsNullOrWhiteSpace(hex))
                {
                    return Brushes.White;
                }

                // Use ColorConverter to parse the hex string
                var colorObj = ColorConverter.ConvertFromString(hex);
                if (colorObj == null) return Brushes.White;
                Color c = (Color)colorObj;

                // Perceived luminance
                double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

                return lum > 0.5 ? Brushes.Black : Brushes.White;
            }
            catch
            {
                return Brushes.White;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
