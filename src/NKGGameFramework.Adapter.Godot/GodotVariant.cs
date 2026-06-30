namespace NKGGameFramework.Adapter.Godot;

public readonly struct GodotVariant
{
    private GodotVariant(
        GodotVariantKind kind,
        GodotColor color,
        IReadOnlyList<GodotVector2>? vector2Array,
        string? text,
        bool boolean,
        long integer,
        double number,
        GodotVector2 vector2)
    {
        Kind = kind;
        Color = color;
        Vector2Array = vector2Array;
        Text = text;
        Boolean = boolean;
        Integer = integer;
        Number = number;
        Vector2 = vector2;
    }

    public GodotVariantKind Kind { get; }

    public GodotColor Color { get; }

    public IReadOnlyList<GodotVector2>? Vector2Array { get; }

    public string? Text { get; }

    public bool Boolean { get; }

    public long Integer { get; }

    public double Number { get; }

    public GodotVector2 Vector2 { get; }

    public static GodotVariant FromColor(GodotColor value)
    {
        return new GodotVariant(GodotVariantKind.Color, value, null, null, false, 0, 0, default);
    }

    public static GodotVariant FromPackedVector2Array(IReadOnlyList<GodotVector2> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new GodotVariant(GodotVariantKind.PackedVector2Array, default, value, null, false, 0, 0, default);
    }

    public static GodotVariant FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new GodotVariant(GodotVariantKind.String, default, null, value, false, 0, 0, default);
    }

    public static GodotVariant FromBool(bool value)
    {
        return new GodotVariant(GodotVariantKind.Bool, default, null, null, value, 0, 0, default);
    }

    public static GodotVariant FromInteger(long value)
    {
        return new GodotVariant(GodotVariantKind.Integer, default, null, null, false, value, 0, default);
    }

    public static GodotVariant FromFloat(double value)
    {
        return new GodotVariant(GodotVariantKind.Float, default, null, null, false, 0, value, default);
    }

    public static GodotVariant FromVector2(GodotVector2 value)
    {
        return new GodotVariant(GodotVariantKind.Vector2, default, null, null, false, 0, 0, value);
    }
}

public enum GodotVariantKind : byte
{
    Color = 1,
    PackedVector2Array = 2,
    String = 3,
    Bool = 4,
    Integer = 5,
    Float = 6,
    Vector2 = 7
}
