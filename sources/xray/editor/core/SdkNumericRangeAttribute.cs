using System;

namespace xray.editor_base;

[AttributeUsage(AttributeTargets.Property)]
public sealed class SdkNumericRangeAttribute(double minimum, double maximum, double step, int precision) : Attribute
{
    public double Minimum { get; } = minimum;
    public double Maximum { get; } = maximum;
    public double Step { get; } = step;
    public int Precision { get; } = precision;
    public bool UpdateOnEditComplete { get; set; }
}
