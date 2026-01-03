using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EVE_NEIC.App.Converters;

public class ProfitColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is decimal profit)
        {
            // Profit > 0 = green
            if (profit > 0) return Brushes.LimeGreen;
            // Profit < 0 = red
            if(profit < 0) return Brushes.Red;
        }
        
        // Default
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}