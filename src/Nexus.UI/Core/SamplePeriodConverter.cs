using System.ComponentModel;
using System.Globalization;

namespace Nexus.UI.Core;

[TypeConverter(typeof(SamplePeriodConverter))]
public class SamplePeriod
{
    public SamplePeriod(TimeSpan value)
    {
        Value = value;
    }

    public TimeSpan Value { get; }

    public override string ToString()
    {
        return Utilities.ToUnitString(Value);
    }
}

public class SamplePeriodConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }
 
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string input)
        {
            try
            {
                return new SamplePeriod(Utilities.ToSamplePeriod(input));
            }

            catch
            {
                return new SamplePeriod(TimeSpan.FromSeconds(1));
            }
            
        }

        return base.ConvertFrom(context, culture, value);
    }
}