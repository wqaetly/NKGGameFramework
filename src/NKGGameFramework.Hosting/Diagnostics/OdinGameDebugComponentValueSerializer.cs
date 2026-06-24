using System.Text;
using OdinSerializer;

namespace NKGGameFramework.Hosting.Diagnostics;

public sealed class OdinGameDebugComponentValueSerializer : IGameDebugComponentValueSerializer
{
    public ComponentValueDebugSnapshot Serialize(object value)
    {
        try
        {
            return new ComponentValueDebugSnapshot(
                "odin-json",
                Encoding.UTF8.GetString(SerializationUtility.SerializeValueWeak(
                    value,
                    DataFormat.JSON,
                    CreateSerializationContext())),
                Error: null);
        }
        catch (Exception exception)
        {
            return new ComponentValueDebugSnapshot(
                "odin-json",
                Payload: null,
                exception.Message);
        }
    }

    public object Deserialize(ComponentValueDebugSnapshot value, Type expectedType)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(expectedType);

        if (!StringComparer.Ordinal.Equals(value.Format, "odin-json"))
        {
            throw new ArgumentException($"Unsupported component value format '{value.Format}'.", nameof(value));
        }

        if (string.IsNullOrWhiteSpace(value.Payload))
        {
            throw new ArgumentException("Component value payload is empty.", nameof(value));
        }

        var result = SerializationUtility.DeserializeValueWeak(
            Encoding.UTF8.GetBytes(value.Payload),
            DataFormat.JSON,
            CreateDeserializationContext());

        if (result is null)
        {
            throw new ArgumentException("Component value payload deserialized to null.", nameof(value));
        }

        if (!expectedType.IsInstanceOfType(result))
        {
            throw new ArgumentException($"Component payload type '{result.GetType().FullName}' does not match '{expectedType.FullName}'.", nameof(value));
        }

        return result;
    }

    private static SerializationContext CreateSerializationContext()
    {
        var context = new SerializationContext();
        Configure(context.Config);
        return context;
    }

    private static DeserializationContext CreateDeserializationContext()
    {
        var context = new DeserializationContext();
        Configure(context.Config);
        return context;
    }

    private static void Configure(SerializationConfig config)
    {
        config.SerializationPolicy = SerializationPolicies.Everything;
        config.DebugContext.LoggingPolicy = LoggingPolicy.Silent;
        config.DebugContext.ErrorHandlingPolicy = ErrorHandlingPolicy.ThrowOnErrors;
    }
}
