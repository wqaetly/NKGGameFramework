using System.Reflection;
using OdinSerializer;

namespace NKGGameFramework.Hosting.Diagnostics;

internal static class GameDebugOdinSerializationPolicy
{
    public static ISerializationPolicy Instance => GameDebugOdinSerialization.Policy;

    internal static bool ShouldSerializeMember(MemberInfo member)
    {
        return GameDebugOdinSerialization.ShouldSerializeMember(member);
    }

    internal static bool TryGetAutoProperty(FieldInfo field, out PropertyInfo? property)
    {
        return GameDebugOdinSerialization.TryGetAutoProperty(field, out property);
    }
}
