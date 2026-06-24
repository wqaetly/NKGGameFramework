namespace NKGGameFramework.Hosting.Diagnostics;

public interface IGameDebugComponentValueSerializer
{
    ComponentValueDebugSnapshot Serialize(
        object value,
        GameDebugComponentValueSerializationOptions? options = null);

    object Deserialize(ComponentValueDebugSnapshot value, Type expectedType);
}
