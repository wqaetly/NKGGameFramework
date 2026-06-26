namespace NKGGameFramework.Hosting.Diagnostics;

internal static class GameDebugComponentValueMaterializer
{
    public static ComponentDebugSnapshot MaterializeStructured(
        ComponentDebugSnapshot component,
        IGameDebugComponentValueSerializer serializer,
        GameDebugStructuredComponentValueCaptureOptions? structuredOptions = null)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        if (component.Value.Structured is not null ||
            !StringComparer.Ordinal.Equals(component.Value.Format, "odin-json") ||
            string.IsNullOrWhiteSpace(component.Value.Payload) ||
            !TryResolveType(component.Type, out var componentType))
        {
            return component;
        }

        try
        {
            var value = serializer.Deserialize(component.Value, componentType);
            var structured = serializer.Serialize(
                value,
                new GameDebugComponentValueSerializationOptions
                {
                    IncludePayload = false,
                    IncludeStructured = true,
                    StructuredCaptureOptions = structuredOptions ?? GameDebugStructuredComponentValueCaptureOptions.Default,
                }).Structured;

            return component with
            {
                Value = component.Value with
                {
                    Structured = structured,
                    Error = component.Value.Error,
                },
            };
        }
        catch (Exception exception)
        {
            return component with
            {
                Value = component.Value with
                {
                    Error = exception.Message,
                },
            };
        }
    }

    private static bool TryResolveType(DebugTypeInfo typeInfo, out Type type)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!StringComparer.Ordinal.Equals(assembly.GetName().Name, typeInfo.AssemblyName))
            {
                continue;
            }

            if (assembly.GetType(typeInfo.FullName, throwOnError: false) is { } resolved)
            {
                type = resolved;
                return true;
            }
        }

        type = null!;
        return false;
    }
}
