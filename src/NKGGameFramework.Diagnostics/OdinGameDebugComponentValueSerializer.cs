using System.Text;
using OdinSerializer;

namespace NKGGameFramework.Diagnostics;

public sealed class OdinGameDebugComponentValueSerializer : IGameDebugComponentValueSerializer
{
    public ComponentValueDebugSnapshot Serialize(
        object value,
        GameDebugComponentValueSerializationOptions? options = null)
    {
        options ??= GameDebugComponentValueSerializationOptions.Default;

        try
        {
            if (value is null)
            {
                return new ComponentValueDebugSnapshot(
                    "odin-json",
                    Payload: null,
                    Error: null,
                    options.IncludeStructured
                        ? new ComponentValueDebugNode
                        {
                            Kind = "null",
                            Type = DebugSnapshotTypeNames.Create(typeof(object)),
                            Editable = true,
                            Value = null,
                        }
                        : null);
            }

            return new ComponentValueDebugSnapshot(
                "odin-json",
                options.IncludePayload
                    ? Encoding.UTF8.GetString(SerializationUtility.SerializeValueWeak(
                        value,
                        DataFormat.JSON,
                        GameDebugOdinSerialization.CreateSerializationContext()))
                    : null,
                Error: null,
                options.IncludeStructured
                    ? GameDebugStructuredComponentValue.Capture(value, options.StructuredCaptureOptions)
                    : null);
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

        if (!string.IsNullOrWhiteSpace(value.Payload))
        {
            var payloadResult = SerializationUtility.DeserializeValueWeak(
                Encoding.UTF8.GetBytes(value.Payload),
                DataFormat.JSON,
                GameDebugOdinSerialization.CreateDeserializationContext());

            if (payloadResult is null)
            {
                throw new ArgumentException("Component value payload deserialized to null.", nameof(value));
            }

            if (!expectedType.IsInstanceOfType(payloadResult))
            {
                throw new ArgumentException($"Component payload type '{payloadResult.GetType().FullName}' does not match '{expectedType.FullName}'.", nameof(value));
            }

            return value.Structured is null
                ? payloadResult
                : GameDebugStructuredComponentValue.Apply(value.Structured, payloadResult, expectedType);
        }

        if (value.Structured is null)
        {
            throw new ArgumentException("Component value payload is empty.", nameof(value));
        }

        if (Activator.CreateInstance(expectedType) is not { } emptyResult)
        {
            throw new ArgumentException($"Component type '{expectedType.FullName}' cannot be created from structured data.", nameof(value));
        }

        return GameDebugStructuredComponentValue.Apply(value.Structured, emptyResult, expectedType);
    }

}
