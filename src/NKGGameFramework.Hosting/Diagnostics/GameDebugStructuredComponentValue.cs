using System.Collections;
using System.Globalization;
using System.Reflection;

namespace NKGGameFramework.Hosting.Diagnostics;

internal static class GameDebugStructuredComponentValue
{
    private const int MaxDepth = 8;

    public static ComponentValueDebugNode Capture(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return CaptureNode(
            name: null,
            value,
            value.GetType(),
            editable: true,
            depth: 0,
            seen);
    }

    public static object Apply(ComponentValueDebugNode node, object target, Type expectedType)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(expectedType);

        return ApplyNode(node, target, expectedType);
    }

    private static ComponentValueDebugNode CaptureNode(
        string? name,
        object? value,
        Type declaredType,
        bool editable,
        int depth,
        HashSet<object> seen)
    {
        var valueType = Nullable.GetUnderlyingType(declaredType) ?? value?.GetType() ?? declaredType;
        var type = value?.GetType() ?? valueType;

        if (value is null)
        {
            return CreateNode("null", name, type, editable) with
            {
                Value = null,
            };
        }

        if (TryFormatScalar(value, type, out var scalarKind, out var scalarValue, out var options))
        {
            return CreateNode(scalarKind, name, type, editable) with
            {
                Value = scalarValue,
                Options = options,
            };
        }

        if (depth >= MaxDepth)
        {
            return CreateNode("unsupported", name, type, editable: false) with
            {
                Error = $"Maximum debug value depth ({MaxDepth}) was reached.",
            };
        }

        if (!type.IsValueType && !seen.Add(value))
        {
            return CreateNode("reference", name, type, editable: false) with
            {
                Error = "Reference already displayed.",
            };
        }

        if (TryGetListElementType(type, out var elementType) && value is IEnumerable enumerable)
        {
            return CaptureList(name, value, type, elementType, enumerable, editable, depth, seen);
        }

        var children = GetDebugMembers(type)
            .Select(member =>
            {
                try
                {
                    return CaptureNode(
                        member.Name,
                        member.GetValue(value),
                        member.ValueType,
                        member.CanWrite,
                        depth + 1,
                        seen);
                }
                catch (Exception exception) when (exception is TargetInvocationException or ArgumentException)
                {
                    return CreateNode("unsupported", member.Name, member.ValueType, editable: false) with
                    {
                        Error = exception.InnerException?.Message ?? exception.Message,
                    };
                }
            })
            .ToArray();

        return CreateNode("object", name, type, editable) with
        {
            Children = children,
        };
    }

    private static ComponentValueDebugNode CaptureList(
        string? name,
        object value,
        Type type,
        Type elementType,
        IEnumerable enumerable,
        bool editable,
        int depth,
        HashSet<object> seen)
    {
        var index = 0;
        var children = new List<ComponentValueDebugNode>();

        foreach (var item in enumerable)
        {
            children.Add(CaptureNode(
                $"[{index}]",
                item,
                elementType,
                editable,
                depth + 1,
                seen));
            index++;
        }

        return CreateNode("list", name, type, editable) with
        {
            Children = children,
            ElementType = DebugSnapshotTypeNames.Create(elementType),
            ElementTemplate = CaptureTemplate(elementType, depth + 1, seen),
        };
    }

    private static ComponentValueDebugNode? CaptureTemplate(
        Type elementType,
        int depth,
        HashSet<object> seen)
    {
        if (depth >= MaxDepth)
        {
            return null;
        }

        if (TryCreateDefaultValue(elementType, out var value))
        {
            return CaptureNode("New Item", value, elementType, editable: true, depth, seen);
        }

        if (IsScalarType(elementType))
        {
            return CaptureNode("New Item", GetScalarDefault(elementType), elementType, editable: true, depth, seen);
        }

        return CreateNode("unsupported", "New Item", elementType, editable: false) with
        {
            Error = $"No default value can be created for '{elementType.FullName}'.",
        };
    }

    private static object ApplyNode(ComponentValueDebugNode node, object target, Type declaredType)
    {
        var effectiveType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (node.Kind is "boolean" or "integer" or "number" or "string" or "enum" or "null")
        {
            return ConvertScalar(node, effectiveType);
        }

        if (node.Kind == "list")
        {
            return ApplyList(node, target, effectiveType);
        }

        if (node.Kind != "object")
        {
            return target;
        }

        foreach (var child in node.Children)
        {
            if (string.IsNullOrWhiteSpace(child.Name))
            {
                continue;
            }

            if (!TryGetDebugMember(effectiveType, child.Name, out var member))
            {
                continue;
            }

            var currentValue = member.GetValue(target);
            var nextValue = currentValue ?? CreateFallbackValue(member.ValueType);

            if (child.Kind is not ("object" or "list") && !member.CanWrite)
            {
                continue;
            }

            nextValue = ApplyNode(child, nextValue, member.ValueType);

            if (member.CanWrite)
            {
                member.SetValue(target, nextValue);
            }
        }

        return target;
    }

    private static object ApplyList(ComponentValueDebugNode node, object target, Type listType)
    {
        if (!TryGetListElementType(listType, out var elementType))
        {
            return target;
        }

        if (listType.IsArray)
        {
            var array = Array.CreateInstance(elementType, node.Children.Count);
            for (var index = 0; index < node.Children.Count; index++)
            {
                var child = node.Children[index];
                var item = CreateFallbackValue(elementType);
                array.SetValue(ApplyNode(child, item, elementType), index);
            }

            return array;
        }

        if (target is IList list)
        {
            list.Clear();
            foreach (var child in node.Children)
            {
                var item = CreateFallbackValue(elementType);
                list.Add(ApplyNode(child, item, elementType));
            }

            return target;
        }

        var concreteListType = typeof(List<>).MakeGenericType(elementType);
        if (Activator.CreateInstance(concreteListType) is not IList created)
        {
            return target;
        }

        foreach (var child in node.Children)
        {
            var item = CreateFallbackValue(elementType);
            created.Add(ApplyNode(child, item, elementType));
        }

        return created;
    }

    private static object ConvertScalar(ComponentValueDebugNode node, Type type)
    {
        if (node.Kind == "null")
        {
            return type.IsValueType ? Activator.CreateInstance(type)! : null!;
        }

        var text = node.Value ?? string.Empty;

        if (type == typeof(string))
        {
            return text;
        }

        if (type == typeof(bool))
        {
            return bool.Parse(text);
        }

        if (type.IsEnum)
        {
            return Enum.Parse(type, text, ignoreCase: false);
        }

        if (type == typeof(char))
        {
            return text.Length == 0 ? '\0' : text[0];
        }

        if (type == typeof(Guid))
        {
            return Guid.Parse(text);
        }

        if (type == typeof(TimeSpan))
        {
            return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        }

        if (type == typeof(DateTime))
        {
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        return Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
    }

    private static bool TryFormatScalar(
        object value,
        Type type,
        out string kind,
        out string scalarValue,
        out IReadOnlyList<string> options)
    {
        options = Array.Empty<string>();

        if (type == typeof(bool))
        {
            kind = "boolean";
            scalarValue = ((bool)value).ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
            return true;
        }

        if (type.IsEnum)
        {
            kind = "enum";
            scalarValue = value.ToString() ?? string.Empty;
            options = Enum.GetNames(type);
            return true;
        }

        if (IsIntegerType(type))
        {
            kind = "integer";
            scalarValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return true;
        }

        if (IsNumberType(type))
        {
            kind = "number";
            scalarValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return true;
        }

        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid))
        {
            kind = "string";
            scalarValue = value.ToString() ?? string.Empty;
            return true;
        }

        if (type == typeof(TimeSpan))
        {
            kind = "string";
            scalarValue = ((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture);
            return true;
        }

        if (type == typeof(DateTime))
        {
            kind = "string";
            scalarValue = ((DateTime)value).ToString("O", CultureInfo.InvariantCulture);
            return true;
        }

        if (type == typeof(DateTimeOffset))
        {
            kind = "string";
            scalarValue = ((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture);
            return true;
        }

        kind = string.Empty;
        scalarValue = string.Empty;
        return false;
    }

    private static bool IsScalarType(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        return effectiveType == typeof(bool)
            || effectiveType == typeof(string)
            || effectiveType == typeof(char)
            || effectiveType == typeof(Guid)
            || effectiveType == typeof(TimeSpan)
            || effectiveType == typeof(DateTime)
            || effectiveType == typeof(DateTimeOffset)
            || effectiveType.IsEnum
            || IsIntegerType(effectiveType)
            || IsNumberType(effectiveType);
    }

    private static bool IsIntegerType(Type type)
    {
        return type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong);
    }

    private static bool IsNumberType(Type type)
    {
        return type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal);
    }

    private static bool TryGetListElementType(Type type, out Type elementType)
    {
        if (type == typeof(string))
        {
            elementType = null!;
            return false;
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        foreach (var candidate in type.GetInterfaces().Append(type))
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IList<>))
            {
                elementType = candidate.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static IEnumerable<DebugMember> GetDebugMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        foreach (var field in type.GetFields(flags).OrderBy(static field => field.MetadataToken))
        {
            if (field.IsLiteral)
            {
                continue;
            }

            yield return new DebugMember(
                field.Name,
                field.FieldType,
                target => field.GetValue(target),
                (target, value) => field.SetValue(target, value),
                CanWrite: !field.IsInitOnly);
        }

        foreach (var property in type.GetProperties(flags).OrderBy(static property => property.MetadataToken))
        {
            if (property.GetMethod is null || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            yield return new DebugMember(
                property.Name,
                property.PropertyType,
                target => property.GetValue(target),
                (target, value) => property.SetValue(target, value),
                CanWrite: property.SetMethod is { IsPublic: true });
        }
    }

    private static bool TryGetDebugMember(Type type, string name, out DebugMember member)
    {
        var found = GetDebugMembers(type).FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Name, name));
        if (found is null)
        {
            member = null!;
            return false;
        }

        member = found;
        return true;
    }

    private static bool TryCreateDefaultValue(Type type, out object value)
    {
        if (type.IsValueType)
        {
            value = Activator.CreateInstance(type)!;
            return true;
        }

        var constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor is null)
        {
            value = null!;
            return false;
        }

        value = constructor.Invoke(null);
        return true;
    }

    private static object CreateFallbackValue(Type type)
    {
        return TryCreateDefaultValue(type, out var value)
            ? value
            : GetScalarDefault(type);
    }

    private static object GetScalarDefault(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

        if (effectiveType == typeof(string))
        {
            return string.Empty;
        }

        if (effectiveType == typeof(Guid))
        {
            return Guid.Empty;
        }

        if (effectiveType.IsEnum)
        {
            return Enum.GetValues(effectiveType).GetValue(0)!;
        }

        return effectiveType.IsValueType ? Activator.CreateInstance(effectiveType)! : string.Empty;
    }

    private static ComponentValueDebugNode CreateNode(string kind, string? name, Type type, bool editable)
    {
        return new ComponentValueDebugNode
        {
            Kind = kind,
            Name = name,
            Type = DebugSnapshotTypeNames.Create(type),
            Editable = editable,
        };
    }

    private sealed record DebugMember(
        string Name,
        Type ValueType,
        Func<object, object?> GetValue,
        Action<object, object?> SetValue,
        bool CanWrite);
}
