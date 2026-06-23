using OdinSerializer;
using System.Text;

namespace NKGGameFramework.Serialization;

public sealed class OdinGameSerializer : IGameSerializer, IBinaryGameSerializer, IJsonGameSerializer
{
    private readonly DataFormat _format;
    private readonly ISerializationPolicy _policy;
    private readonly LoggingPolicy _loggingPolicy;
    private readonly ErrorHandlingPolicy _errorHandlingPolicy;

    public OdinGameSerializer(
        DataFormat format = DataFormat.Binary,
        ISerializationPolicy? policy = null,
        LoggingPolicy loggingPolicy = LoggingPolicy.Silent,
        ErrorHandlingPolicy errorHandlingPolicy = ErrorHandlingPolicy.ThrowOnErrors)
    {
        if (format == DataFormat.Nodes)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Node format is not stream backed.");
        }

        _format = format;
        _policy = policy ?? SerializationPolicies.Everything;
        _loggingPolicy = loggingPolicy;
        _errorHandlingPolicy = errorHandlingPolicy;
    }

    public string Serialize<T>(T value)
    {
        if (_format == DataFormat.JSON)
        {
            return SerializeToJson(value);
        }

        return Convert.ToBase64String(SerializeToBytes(value));
    }

    public T? Deserialize<T>(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (_format == DataFormat.JSON)
        {
            return DeserializeFromJson<T>(payload);
        }

        return DeserializeFromBytes<T>(Convert.FromBase64String(payload));
    }

    public byte[] SerializeToBytes<T>(T value)
    {
        return SerializeToBytes(value, _format);
    }

    public T? DeserializeFromBytes<T>(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return DeserializeFromBytes<T>(payload, _format);
    }

    public string SerializeToJson<T>(T value)
    {
        return Encoding.UTF8.GetString(SerializeToBytes(value, DataFormat.JSON));
    }

    public T? DeserializeFromJson<T>(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return DeserializeFromBytes<T>(Encoding.UTF8.GetBytes(payload), DataFormat.JSON);
    }

    private byte[] SerializeToBytes<T>(T value, DataFormat format)
    {
        return SerializationUtility.SerializeValue(value, format, CreateSerializationContext());
    }

    private T? DeserializeFromBytes<T>(byte[] payload, DataFormat format)
    {
        return SerializationUtility.DeserializeValue<T>(payload, format, CreateDeserializationContext());
    }

    private SerializationContext CreateSerializationContext()
    {
        var context = new SerializationContext();
        Configure(context.Config);
        return context;
    }

    private DeserializationContext CreateDeserializationContext()
    {
        var context = new DeserializationContext();
        Configure(context.Config);
        return context;
    }

    private void Configure(SerializationConfig config)
    {
        config.SerializationPolicy = _policy;
        config.DebugContext.LoggingPolicy = _loggingPolicy;
        config.DebugContext.ErrorHandlingPolicy = _errorHandlingPolicy;
    }
}
