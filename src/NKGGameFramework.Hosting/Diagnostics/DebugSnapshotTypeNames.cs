namespace NKGGameFramework.Hosting.Diagnostics;

internal static class DebugSnapshotTypeNames
{
    public static DebugTypeInfo Create(Type type)
    {
        return new DebugTypeInfo(
            type.Name,
            type.FullName ?? type.Name,
            type.Assembly.GetName().Name ?? string.Empty);
    }
}
