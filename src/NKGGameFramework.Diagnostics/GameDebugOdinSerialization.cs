using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using OdinSerializer;

namespace NKGGameFramework.Diagnostics;

internal static class GameDebugOdinSerialization
{
    private static readonly ConcurrentDictionary<Type, GameDebugSerializedMember[]> MemberCache = [];

    public static ISerializationPolicy Policy { get; } = new CustomSerializationPolicy(
        "NKGGameFramework.DebugComponent",
        allowNonSerializableTypes: true,
        ShouldSerializeMember);

    public static SerializationContext CreateSerializationContext()
    {
        var context = new SerializationContext();
        Configure(context.Config);
        return context;
    }

    public static DeserializationContext CreateDeserializationContext()
    {
        var context = new DeserializationContext();
        Configure(context.Config);
        return context;
    }

    public static IReadOnlyList<GameDebugSerializedMember> GetSerializedMembers(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return MemberCache.GetOrAdd(type, CreateSerializedMembers);
    }

    public static bool TryGetSerializedMember(
        Type type,
        string name,
        out GameDebugSerializedMember member)
    {
        foreach (var candidate in GetSerializedMembers(type))
        {
            if (StringComparer.Ordinal.Equals(candidate.Name, name))
            {
                member = candidate;
                return true;
            }
        }

        member = null!;
        return false;
    }

    internal static bool ShouldSerializeMember(MemberInfo member)
    {
        if (member.IsDefined(typeof(NonSerializedAttribute), inherit: true))
        {
            return false;
        }

        if (member is FieldInfo field)
        {
            if (field.IsStatic || field.IsLiteral)
            {
                return false;
            }

            if (field.IsDefined(typeof(OdinSerializeAttribute), inherit: true))
            {
                return true;
            }

            return field.IsPublic || IsPublicAutoPropertyBackingField(field);
        }

        if (member is PropertyInfo property)
        {
            if (IsStatic(property))
            {
                return false;
            }

            return property.IsDefined(typeof(OdinSerializeAttribute), inherit: true);
        }

        return member.IsDefined(typeof(OdinSerializeAttribute), inherit: true);
    }

    internal static bool TryGetAutoProperty(FieldInfo field, out PropertyInfo? property)
    {
        if (!field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) ||
            !field.Name.StartsWith("<", StringComparison.Ordinal) ||
            !field.Name.Contains(">k__BackingField", StringComparison.Ordinal))
        {
            property = null;
            return false;
        }

        var end = field.Name.IndexOf(">", StringComparison.Ordinal);
        if (end <= 1)
        {
            property = null;
            return false;
        }

        var propertyName = field.Name[1..end];
        property = field.DeclaringType?.GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property is not null;
    }

    private static void Configure(SerializationConfig config)
    {
        config.SerializationPolicy = Policy;
        config.DebugContext.LoggingPolicy = LoggingPolicy.Silent;
        config.DebugContext.ErrorHandlingPolicy = ErrorHandlingPolicy.ThrowOnErrors;
    }

    private static GameDebugSerializedMember[] CreateSerializedMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var members = new List<GameDebugSerializedMember>();
        var memberNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in type.GetFields(flags).OrderBy(static field => field.MetadataToken))
        {
            if (!ShouldSerializeMember(field))
            {
                continue;
            }

            var name = field.Name;
            if (TryGetAutoProperty(field, out var property) &&
                property is not null &&
                property.GetGetMethod(nonPublic: false) is not null)
            {
                name = property.Name;
            }

            if (!memberNames.Add(name))
            {
                continue;
            }

            members.Add(new GameDebugSerializedMember(
                name,
                field.Name,
                field.FieldType,
                target => field.GetValue(target),
                (target, value) => field.SetValue(target, value),
                CanWrite: !field.IsInitOnly));
        }

        foreach (var property in type.GetProperties(flags).OrderBy(static property => property.MetadataToken))
        {
            if (!ShouldSerializeMember(property) ||
                property.GetMethod is null ||
                property.GetIndexParameters().Length != 0 ||
                !memberNames.Add(property.Name))
            {
                continue;
            }

            members.Add(new GameDebugSerializedMember(
                property.Name,
                property.Name,
                property.PropertyType,
                target => property.GetValue(target),
                (target, value) => property.SetValue(target, value),
                CanWrite: property.SetMethod is not null));
        }

        return members.ToArray();
    }

    private static bool IsPublicAutoPropertyBackingField(FieldInfo field)
    {
        return TryGetAutoProperty(field, out var property)
            && property is not null
            && property.GetGetMethod(nonPublic: false) is not null;
    }

    private static bool IsStatic(PropertyInfo property)
    {
        return property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true;
    }
}

internal sealed record GameDebugSerializedMember(
    string Name,
    string SerializedName,
    Type ValueType,
    Func<object, object?> GetValue,
    Action<object, object?> SetValue,
    bool CanWrite);
