using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public readonly record struct BuffApplicationResult(bool IsNewInstance, bool IsRefresh, BuffInstance Instance);

public readonly record struct BuffApplied(EntityRef Target, EntityRef Source, string BuffId, int Level, int Stacks);

public readonly record struct BuffRefreshed(EntityRef Target, EntityRef Source, string BuffId, int Level, int Stacks);

public readonly record struct BuffRemoved(EntityRef Target, EntityRef Source, string BuffId, int Level, int Stacks);
