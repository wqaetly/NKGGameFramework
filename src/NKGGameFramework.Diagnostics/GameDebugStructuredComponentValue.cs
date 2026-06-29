using System.Collections;
using System.Globalization;
using System.Reflection;
using NKGGameFramework.Core;
using NKGGameFramework.Ecs;

namespace NKGGameFramework.Diagnostics;

internal static class GameDebugStructuredComponentValue
{
    public static ComponentValueDebugNode Capture(
        object value,
        GameDebugStructuredComponentValueCaptureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        options ??= GameDebugStructuredComponentValueCaptureOptions.Default;

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return CaptureNode(
            name: null,
            value,
            value.GetType(),
            editable: true,
            depth: 0,
            seen,
            options);
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
        HashSet<object> seen,
        GameDebugStructuredComponentValueCaptureOptions options)
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

        if (TryFormatScalar(value, type, out var scalarKind, out var scalarValue, out var scalarOptions))
        {
            return CreateNode(scalarKind, name, type, editable) with
            {
                Value = scalarValue,
                Options = scalarOptions,
            };
        }

        if (options.StopAtRuntimeReferences && depth > 0 && IsRuntimeReferenceBoundary(type))
        {
            return CreateNode("reference", name, type, editable: false) with
            {
                Value = FormatRuntimeReference(value),
                Error = "Runtime reference boundary.",
            };
        }

        if (depth >= options.MaxDepth)
        {
            return CreateNode("unsupported", name, type, editable: false) with
            {
                Error = $"Maximum debug value depth ({options.MaxDepth}) was reached.",
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
            return CaptureList(name, value, type, elementType, enumerable, editable, depth, seen, options);
        }

        var children = GameDebugOdinSerialization.GetSerializedMembers(type)
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
                        seen,
                        options);
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
        HashSet<object> seen,
        GameDebugStructuredComponentValueCaptureOptions options)
    {
        var index = 0;
        var children = new List<ComponentValueDebugNode>();
        var truncated = false;
        string? error = null;

        try
        {
            foreach (var item in enumerable)
            {
                if (options.MaxCollectionItems is { } maxItems && index >= maxItems)
                {
                    truncated = true;
                    break;
                }

                children.Add(CaptureNode(
                    $"[{index}]",
                    item,
                    elementType,
                    editable,
                    depth + 1,
                    seen,
                    options));
                index++;
            }
        }
        catch (Exception exception)
        {
            error = exception.InnerException?.Message ?? exception.Message;
        }

        return CreateNode("list", name, type, editable) with
        {
            Children = truncated
                ? children
                    .Append(CreateNode("unsupported", $"[{index}+]", elementType, editable: false) with
                    {
                        Error = $"Collection preview was truncated at {options.MaxCollectionItems} item(s).",
                    })
                    .ToArray()
                : children,
            ElementType = DebugSnapshotTypeNames.Create(elementType),
            ElementTemplate = options.CaptureElementTemplate
                ? CaptureTemplate(elementType, depth + 1, seen, options)
                : null,
            Error = error ?? (truncated
                ? $"Collection preview was truncated at {options.MaxCollectionItems} item(s)."
                : null),
        };
    }

    private static ComponentValueDebugNode? CaptureTemplate(
        Type elementType,
        int depth,
        HashSet<object> seen,
        GameDebugStructuredComponentValueCaptureOptions options)
    {
        if (depth >= options.MaxDepth)
        {
            return null;
        }

        if (TryCreateDefaultValue(elementType, out var value))
        {
            return CaptureNode("New Item", value, elementType, editable: true, depth, seen, options);
        }

        if (IsScalarType(elementType))
        {
            return CaptureNode("New Item", GetScalarDefault(elementType), elementType, editable: true, depth, seen, options);
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

            if (!GameDebugOdinSerialization.TryGetSerializedMember(effectiveType, child.Name, out var member))
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

        if (TryPopulateGenericCollection(target, elementType, node))
        {
            return target;
        }

        if (TryCreateCollectionValue(listType, elementType, out var collection) &&
            TryPopulateGenericCollection(collection, elementType, node))
        {
            return collection;
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
            if (!candidate.IsGenericType)
            {
                continue;
            }

            var definition = candidate.GetGenericTypeDefinition();
            if (definition == typeof(IList<>) ||
                definition == typeof(IReadOnlyList<>) ||
                definition == typeof(ISet<>) ||
                definition == typeof(IReadOnlySet<>) ||
                definition == typeof(ICollection<>) ||
                definition == typeof(IReadOnlyCollection<>) ||
                definition == typeof(IEnumerable<>))
            {
                elementType = candidate.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static bool IsRuntimeReferenceBoundary(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        return effectiveType == typeof(World)
            || effectiveType == typeof(Scene)
            || effectiveType == typeof(SystemGroup)
            || effectiveType == typeof(RuntimeContext)
            || typeof(ISystem).IsAssignableFrom(effectiveType)
            || typeof(NKGGameFramework.Core.Module).IsAssignableFrom(effectiveType)
            || typeof(IEventBus).IsAssignableFrom(effectiveType);
    }

    private static string FormatRuntimeReference(object value)
    {
        return value switch
        {
            World world => world.Name,
            Scene scene => scene.Name,
            RuntimeContext runtime => runtime.IsDisposed ? "Disposed runtime" : "Runtime",
            _ => value.GetType().Name,
        };
    }

    private static bool TryPopulateGenericCollection(
        object target,
        Type elementType,
        ComponentValueDebugNode node)
    {
        var collectionType = typeof(ICollection<>).MakeGenericType(elementType);
        if (!collectionType.IsInstanceOfType(target))
        {
            return false;
        }

        collectionType.GetMethod(nameof(ICollection<object>.Clear))!.Invoke(target, []);
        var add = collectionType.GetMethod(nameof(ICollection<object>.Add))!;
        foreach (var child in node.Children)
        {
            var item = CreateFallbackValue(elementType);
            add.Invoke(target, [ApplyNode(child, item, elementType)]);
        }

        return true;
    }

    private static bool TryCreateCollectionValue(Type collectionType, Type elementType, out object collection)
    {
        if (!collectionType.IsInterface &&
            collectionType.GetConstructor(Type.EmptyTypes) is not null &&
            Activator.CreateInstance(collectionType) is { } concreteCollection)
        {
            collection = concreteCollection;
            return true;
        }

        var hashSetType = typeof(HashSet<>).MakeGenericType(elementType);
        if (IsSetLikeType(collectionType) && collectionType.IsAssignableFrom(hashSetType))
        {
            collection = Activator.CreateInstance(hashSetType)!;
            return true;
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        if (collectionType.IsAssignableFrom(listType))
        {
            collection = Activator.CreateInstance(listType)!;
            return true;
        }

        if (collectionType.IsAssignableFrom(hashSetType))
        {
            collection = Activator.CreateInstance(hashSetType)!;
            return true;
        }

        collection = null!;
        return false;
    }

    private static bool IsSetLikeType(Type type)
    {
        return type.GetInterfaces()
            .Append(type)
            .Any(static candidate =>
                candidate.IsGenericType &&
                (candidate.GetGenericTypeDefinition() == typeof(ISet<>) ||
                    candidate.GetGenericTypeDefinition() == typeof(IReadOnlySet<>)));
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

}
