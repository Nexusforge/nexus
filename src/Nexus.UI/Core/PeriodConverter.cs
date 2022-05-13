using System.ComponentModel;
using System.Globalization;

namespace Nexus.UI.Core;

[TypeConverter(typeof(PeriodConverter))]
public record Period
{
    internal const string SINGLE_FILE_LABEL = "Single File";

    public Period(TimeSpan value)
    {
        Value = value;
    }

    public TimeSpan Value { get; }

    public override string ToString()
    {
        return Value.Equals(default)
            ? SINGLE_FILE_LABEL
            : Utilities.ToUnitString(Value);
    }
}

public class PeriodConverter : TypeConverter
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
                return input == Period.SINGLE_FILE_LABEL
                    ? new Period(TimeSpan.Zero)
                    : new Period(Utilities.ToPeriod(input));
            }

            catch
            {
                return new Period(default);
            }
            
        }

        return base.ConvertFrom(context, culture, value);
    }
}