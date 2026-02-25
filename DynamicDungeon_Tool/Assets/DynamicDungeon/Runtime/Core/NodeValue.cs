using System;

public sealed class NodeValue
{
    private readonly object _value;

    public PortDataKind Kind { get; }
    public object RawValue => _value;

    private NodeValue(PortDataKind kind, object value)
    {
        Kind = kind;
        _value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static NodeValue World(GenMap value) => new NodeValue(PortDataKind.World, value);
    public static NodeValue FloatLayer(FloatLayer value) => new NodeValue(PortDataKind.FloatLayer, value);
    public static NodeValue IntLayer(IntLayer value) => new NodeValue(PortDataKind.IntLayer, value);
    public static NodeValue BoolMask(BoolMaskLayer value) => new NodeValue(PortDataKind.BoolMask, value);
    public static NodeValue MarkerSet(MarkerSet value) => new NodeValue(PortDataKind.MarkerSet, value);
    public static NodeValue ValidationReport(ValidationReport value) => new NodeValue(PortDataKind.ValidationReport, value);

    public bool TryGetWorld(out GenMap value)
    {
        value = _value as GenMap;
        return Kind == PortDataKind.World && value != null;
    }

    public bool TryGetFloatLayer(out FloatLayer value)
    {
        value = _value as FloatLayer;
        return Kind == PortDataKind.FloatLayer && value != null;
    }

    public bool TryGetIntLayer(out IntLayer value)
    {
        value = _value as IntLayer;
        return Kind == PortDataKind.IntLayer && value != null;
    }

    public bool TryGetBoolMask(out BoolMaskLayer value)
    {
        value = _value as BoolMaskLayer;
        return Kind == PortDataKind.BoolMask && value != null;
    }

    public bool TryGetMarkerSet(out MarkerSet value)
    {
        value = _value as MarkerSet;
        return Kind == PortDataKind.MarkerSet && value != null;
    }

    public bool TryGetValidationReport(out ValidationReport value)
    {
        value = _value as ValidationReport;
        return Kind == PortDataKind.ValidationReport && value != null;
    }

    public override string ToString() => $"[{Kind}] {_value}";
}
