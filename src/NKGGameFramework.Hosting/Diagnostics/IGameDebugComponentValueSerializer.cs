namespace NKGGameFramework.Hosting.Diagnostics;

public interface IGameDebugComponentValueSerializer
{
    ComponentValueDebugSnapshot Serialize(object value);

    object Deserialize(ComponentValueDebugSnapshot value, Type expectedType);
}
