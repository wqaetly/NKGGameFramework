namespace NKGGameFramework.Diagnostics;

internal static class GameDebugDumpBinaryCodec
{
    private const int Version = 1;

    public static byte[] Serialize(GameDebugDumpDocument dump)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Version);
        WriteDumpDocument(writer, dump);
        writer.Flush();
        return stream.ToArray();
    }

    public static GameDebugDumpDocument Deserialize(byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);
        var version = reader.ReadInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported debug dump binary version '{version}'.");
        }

        return ReadDumpDocument(reader);
    }

    public static byte[] SerializeComponentValues(IReadOnlyList<ComponentValueDebugSnapshot> values)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteList(writer, values, WriteComponentValue);
        writer.Flush();
        return stream.ToArray();
    }

    public static ComponentValueDebugSnapshot[] DeserializeComponentValues(byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);
        return ReadList(reader, ReadComponentValue);
    }

    private static void WriteDumpDocument(BinaryWriter writer, GameDebugDumpDocument dump)
    {
        writer.Write(dump.Format);
        writer.Write(dump.Version);
        writer.Write(dump.Name);
        WriteTimestamp(writer, dump.CreatedAt);
        WriteTimestamp(writer, dump.StartedAt);
        WriteTimestamp(writer, dump.EndedAt);
        writer.Write(dump.DroppedFrameCount);
        WriteList(writer, dump.Frames, WriteSnapshotMessage);
        WriteNullableList(writer, dump.BlockFrames, WriteDumpFrameBlocks);
    }

    private static GameDebugDumpDocument ReadDumpDocument(BinaryReader reader)
    {
        return new GameDebugDumpDocument(
            reader.ReadString(),
            reader.ReadInt32(),
            reader.ReadString(),
            ReadTimestamp(reader),
            ReadTimestamp(reader),
            ReadTimestamp(reader),
            reader.ReadInt32(),
            ReadList(reader, ReadSnapshotMessage),
            ReadNullableList(reader, ReadDumpFrameBlocks));
    }

    private static void WriteSnapshotMessage(BinaryWriter writer, GameDebugSnapshotMessage message)
    {
        WriteFrame(writer, message.Frame);
        WriteSnapshot(writer, message.Snapshot);
        WriteControlState(writer, message.Control);
    }

    private static GameDebugSnapshotMessage ReadSnapshotMessage(BinaryReader reader)
    {
        return new GameDebugSnapshotMessage(
            ReadFrame(reader),
            ReadSnapshot(reader),
            ReadControlState(reader));
    }

    private static void WriteControlState(BinaryWriter writer, GameDebugControlState state)
    {
        writer.Write(state.IsPaused);
        writer.Write(state.PendingStepCount);
        writer.Write(state.Revision);
        WriteNullableString(writer, state.LastCommand);
    }

    private static GameDebugControlState ReadControlState(BinaryReader reader)
    {
        return new GameDebugControlState(
            reader.ReadBoolean(),
            reader.ReadInt32(),
            reader.ReadInt64(),
            ReadNullableString(reader));
    }

    private static void WriteFrame(BinaryWriter writer, GameDebugFrameInfo frame)
    {
        writer.Write(frame.Sequence);
        writer.Write(frame.Source);
        writer.Write(frame.Frame);
        WriteTimestamp(writer, frame.CapturedAt);
        writer.Write(frame.Metrics is not null);
        if (frame.Metrics is not null)
        {
            writer.Write(frame.Metrics.DeltaSeconds);
            writer.Write(frame.Metrics.RealDeltaSeconds);
            writer.Write(frame.Metrics.LogicMilliseconds);
            writer.Write(frame.Metrics.LogicFramesPerSecond);
        }
    }

    private static GameDebugFrameInfo ReadFrame(BinaryReader reader)
    {
        var sequence = reader.ReadInt64();
        var source = reader.ReadString();
        var frame = reader.ReadInt64();
        var capturedAt = ReadTimestamp(reader);
        var metrics = reader.ReadBoolean()
            ? new GameDebugFrameMetrics(
                reader.ReadDouble(),
                reader.ReadDouble(),
                reader.ReadDouble(),
                reader.ReadDouble())
            : null;
        return new GameDebugFrameInfo(sequence, source, frame, capturedAt, metrics);
    }

    private static void WriteSnapshot(BinaryWriter writer, GameDebugSnapshot snapshot)
    {
        WriteTimestamp(writer, snapshot.CapturedAt);
        WriteList(writer, snapshot.Runtimes, WriteRuntime);
        WriteList(writer, snapshot.Worlds, WriteWorld);
    }

    private static GameDebugSnapshot ReadSnapshot(BinaryReader reader)
    {
        return new GameDebugSnapshot(
            ReadTimestamp(reader),
            ReadList(reader, ReadRuntime),
            ReadList(reader, ReadWorld));
    }

    private static void WriteRuntime(BinaryWriter writer, RuntimeContextDebugSnapshot runtime)
    {
        writer.Write(runtime.Index);
        writer.Write(runtime.IsDisposed);
        WriteList(writer, runtime.Modules, WriteModule);
        WriteList(writer, runtime.ProcedureModules, WriteProcedureModule);
    }

    private static RuntimeContextDebugSnapshot ReadRuntime(BinaryReader reader)
    {
        return new RuntimeContextDebugSnapshot(
            reader.ReadInt32(),
            reader.ReadBoolean(),
            ReadList(reader, ReadModule),
            ReadList(reader, ReadProcedureModule));
    }

    private static void WriteModule(BinaryWriter writer, ModuleDebugSnapshot module)
    {
        WriteTypeInfo(writer, module.Type);
        writer.Write(module.Priority);
        writer.Write(module.IsUpdateModule);
    }

    private static ModuleDebugSnapshot ReadModule(BinaryReader reader)
    {
        return new ModuleDebugSnapshot(
            ReadTypeInfo(reader),
            reader.ReadInt32(),
            reader.ReadBoolean());
    }

    private static void WriteProcedureModule(BinaryWriter writer, ProcedureModuleDebugSnapshot module)
    {
        WriteTypeInfo(writer, module.Type);
        writer.Write(module.IsInitialized);
        WriteNullableString(writer, module.CurrentProcedure);
        writer.Write(module.CurrentProcedureTime);
        WriteList(writer, module.Procedures, WriteProcedure);
    }

    private static ProcedureModuleDebugSnapshot ReadProcedureModule(BinaryReader reader)
    {
        return new ProcedureModuleDebugSnapshot(
            ReadTypeInfo(reader),
            reader.ReadBoolean(),
            ReadNullableString(reader),
            reader.ReadDouble(),
            ReadList(reader, ReadProcedure));
    }

    private static void WriteProcedure(BinaryWriter writer, ProcedureDebugSnapshot procedure)
    {
        WriteTypeInfo(writer, procedure.Type);
        writer.Write(procedure.IsCurrent);
    }

    private static ProcedureDebugSnapshot ReadProcedure(BinaryReader reader)
    {
        return new ProcedureDebugSnapshot(ReadTypeInfo(reader), reader.ReadBoolean());
    }

    private static void WriteWorld(BinaryWriter writer, WorldDebugSnapshot world)
    {
        writer.Write(world.Name);
        writer.Write(world.SceneCount);
        WriteList(writer, world.Scenes, WriteScene);
    }

    private static WorldDebugSnapshot ReadWorld(BinaryReader reader)
    {
        return new WorldDebugSnapshot(
            reader.ReadString(),
            reader.ReadInt32(),
            ReadList(reader, ReadScene));
    }

    private static void WriteScene(BinaryWriter writer, SceneDebugSnapshot scene)
    {
        writer.Write(scene.Name);
        writer.Write(scene.EntityCount);
        WriteList(writer, scene.Systems, WriteSystem);
        WriteList(writer, scene.ComponentStores, WriteComponentStore);
        WriteList(writer, scene.Entities, WriteEntity);
    }

    private static SceneDebugSnapshot ReadScene(BinaryReader reader)
    {
        return new SceneDebugSnapshot(
            reader.ReadString(),
            reader.ReadInt32(),
            ReadList(reader, ReadSystem),
            ReadList(reader, ReadComponentStore),
            ReadList(reader, ReadEntity));
    }

    private static void WriteSystem(BinaryWriter writer, SystemDebugSnapshot system)
    {
        WriteTypeInfo(writer, system.Type);
        writer.Write(system.Order);
        writer.Write(system.Enabled);
    }

    private static SystemDebugSnapshot ReadSystem(BinaryReader reader)
    {
        return new SystemDebugSnapshot(
            ReadTypeInfo(reader),
            reader.ReadInt32(),
            reader.ReadBoolean());
    }

    private static void WriteComponentStore(BinaryWriter writer, ComponentStoreDebugSnapshot store)
    {
        WriteTypeInfo(writer, store.Type);
        writer.Write(store.Count);
        WriteIntList(writer, store.EntityIds);
    }

    private static ComponentStoreDebugSnapshot ReadComponentStore(BinaryReader reader)
    {
        return new ComponentStoreDebugSnapshot(
            ReadTypeInfo(reader),
            reader.ReadInt32(),
            ReadIntArray(reader));
    }

    private static void WriteEntity(BinaryWriter writer, EntityDebugSnapshot entity)
    {
        writer.Write(entity.Id);
        writer.Write(entity.Version);
        WriteList(writer, entity.Components, WriteComponent);
        WriteList(writer, entity.Skills, WriteSkill);
        WriteList(writer, entity.Buffs, WriteBuff);
    }

    private static EntityDebugSnapshot ReadEntity(BinaryReader reader)
    {
        return new EntityDebugSnapshot(
            reader.ReadInt32(),
            reader.ReadInt32(),
            ReadList(reader, ReadComponent),
            ReadList(reader, ReadSkill),
            ReadList(reader, ReadBuff));
    }

    private static void WriteComponent(BinaryWriter writer, ComponentDebugSnapshot component)
    {
        WriteTypeInfo(writer, component.Type);
        WriteComponentValue(writer, component.Value);
        WriteGraph(writer, component.Graph);
    }

    private static ComponentDebugSnapshot ReadComponent(BinaryReader reader)
    {
        return new ComponentDebugSnapshot(
            ReadTypeInfo(reader),
            ReadComponentValue(reader),
            ReadGraph(reader));
    }

    private static void WriteComponentValue(BinaryWriter writer, ComponentValueDebugSnapshot value)
    {
        writer.Write(value.Format);
        WriteNullableString(writer, value.Payload);
        WriteNullableString(writer, value.Error);
        WriteNullable(writer, value.Structured, WriteNode);
    }

    private static ComponentValueDebugSnapshot ReadComponentValue(BinaryReader reader)
    {
        return new ComponentValueDebugSnapshot(
            reader.ReadString(),
            ReadNullableString(reader),
            ReadNullableString(reader),
            ReadNullable(reader, ReadNode));
    }

    private static void WriteNode(BinaryWriter writer, ComponentValueDebugNode node)
    {
        writer.Write(node.Kind);
        WriteNullableString(writer, node.Name);
        WriteTypeInfo(writer, node.Type);
        writer.Write(node.Editable);
        WriteNullableString(writer, node.Value);
        WriteList(writer, node.Children, WriteNode);
        WriteStringList(writer, node.Options);
        WriteNullable(writer, node.ElementType, WriteTypeInfo);
        WriteNullable(writer, node.ElementTemplate, WriteNode);
        WriteNullableString(writer, node.Error);
    }

    private static ComponentValueDebugNode ReadNode(BinaryReader reader)
    {
        return new ComponentValueDebugNode
        {
            Kind = reader.ReadString(),
            Name = ReadNullableString(reader),
            Type = ReadTypeInfo(reader),
            Editable = reader.ReadBoolean(),
            Value = ReadNullableString(reader),
            Children = ReadList(reader, ReadNode),
            Options = ReadStringArray(reader),
            ElementType = ReadNullable(reader, ReadTypeInfo),
            ElementTemplate = ReadNullable(reader, ReadNode),
            Error = ReadNullableString(reader),
        };
    }

    private static void WriteGraph(BinaryWriter writer, ComponentGraphDebugSnapshot graph)
    {
        writer.Write(graph.Id);
        WriteNullableString(writer, graph.ParentId);
        WriteNullable(writer, graph.ParentType, WriteTypeInfo);
        WriteNullableString(writer, graph.Group);
        writer.Write(graph.Order);
    }

    private static ComponentGraphDebugSnapshot ReadGraph(BinaryReader reader)
    {
        return new ComponentGraphDebugSnapshot(
            reader.ReadString(),
            ReadNullableString(reader),
            ReadNullable(reader, ReadTypeInfo),
            ReadNullableString(reader),
            reader.ReadInt32());
    }

    private static void WriteSkill(BinaryWriter writer, SkillDebugSnapshot skill)
    {
        writer.Write(skill.Id);
        WriteNullableString(writer, skill.DisplayName);
        writer.Write(skill.Level);
        writer.Write(skill.Kind);
        writer.Write(skill.ReleaseMode);
        writer.Write(skill.CostKind);
        writer.Write(skill.Cost);
        writer.Write(skill.CooldownSeconds);
        writer.Write(skill.CooldownRemainingSeconds);
        writer.Write(skill.IsCoolingDown);
        WriteStringList(writer, skill.Tags);
        WriteStringList(writer, skill.ResourceLocations);
        WriteStringList(writer, skill.EffectKeys);
    }

    private static SkillDebugSnapshot ReadSkill(BinaryReader reader)
    {
        return new SkillDebugSnapshot(
            reader.ReadString(),
            ReadNullableString(reader),
            reader.ReadInt32(),
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadBoolean(),
            ReadStringArray(reader),
            ReadStringArray(reader),
            ReadStringArray(reader));
    }

    private static void WriteBuff(BinaryWriter writer, BuffDebugSnapshot buff)
    {
        writer.Write(buff.Id);
        WriteNullableString(writer, buff.DisplayName);
        writer.Write(buff.Level);
        writer.Write(buff.Stacks);
        writer.Write(buff.State);
        writer.Write(buff.Kind);
        writer.Write(buff.EffectKey);
        WriteNullableDouble(writer, buff.RemainingDurationSeconds);
        WriteEntityRef(writer, buff.Source);
        WriteEntityRef(writer, buff.Target);
        WriteStringList(writer, buff.Tags);
    }

    private static BuffDebugSnapshot ReadBuff(BinaryReader reader)
    {
        return new BuffDebugSnapshot(
            reader.ReadString(),
            ReadNullableString(reader),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadString(),
            ReadNullableDouble(reader),
            ReadEntityRef(reader),
            ReadEntityRef(reader),
            ReadStringArray(reader));
    }

    private static void WriteEntityRef(BinaryWriter writer, EntityRefDebugSnapshot entityRef)
    {
        writer.Write(entityRef.Id);
        writer.Write(entityRef.Version);
        writer.Write(entityRef.IsAlive);
    }

    private static EntityRefDebugSnapshot ReadEntityRef(BinaryReader reader)
    {
        return new EntityRefDebugSnapshot(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadBoolean());
    }

    private static void WriteDumpFrameBlocks(BinaryWriter writer, GameDebugDumpFrameBlocks frame)
    {
        writer.Write(frame.Index);
        WriteList(writer, frame.Worlds, WriteDumpWorldBlocks);
    }

    private static GameDebugDumpFrameBlocks ReadDumpFrameBlocks(BinaryReader reader)
    {
        return new GameDebugDumpFrameBlocks(
            reader.ReadInt32(),
            ReadList(reader, ReadDumpWorldBlocks));
    }

    private static void WriteDumpWorldBlocks(BinaryWriter writer, GameDebugDumpWorldBlocks world)
    {
        writer.Write(world.Name);
        WriteList(writer, world.Scenes, WriteDumpSceneBlocks);
    }

    private static GameDebugDumpWorldBlocks ReadDumpWorldBlocks(BinaryReader reader)
    {
        return new GameDebugDumpWorldBlocks(
            reader.ReadString(),
            ReadList(reader, ReadDumpSceneBlocks));
    }

    private static void WriteDumpSceneBlocks(BinaryWriter writer, GameDebugDumpSceneBlocks scene)
    {
        writer.Write(scene.Name);
        WriteList(writer, scene.ComponentStores, WriteDumpComponentStoreBlock);
    }

    private static GameDebugDumpSceneBlocks ReadDumpSceneBlocks(BinaryReader reader)
    {
        return new GameDebugDumpSceneBlocks(
            reader.ReadString(),
            ReadList(reader, ReadDumpComponentStoreBlock));
    }

    private static void WriteDumpComponentStoreBlock(BinaryWriter writer, GameDebugDumpComponentStoreBlock block)
    {
        WriteTypeInfo(writer, block.Type);
        WriteIntList(writer, block.EntityIds);
        writer.Write(block.Format);
        WriteByteArray(writer, block.Payload);
        WriteNullableString(writer, block.Error);
    }

    private static GameDebugDumpComponentStoreBlock ReadDumpComponentStoreBlock(BinaryReader reader)
    {
        return new GameDebugDumpComponentStoreBlock(
            ReadTypeInfo(reader),
            ReadIntArray(reader),
            reader.ReadString(),
            ReadByteArray(reader),
            ReadNullableString(reader));
    }

    private static void WriteTypeInfo(BinaryWriter writer, DebugTypeInfo type)
    {
        writer.Write(type.Name);
        writer.Write(type.FullName);
        writer.Write(type.AssemblyName);
    }

    private static DebugTypeInfo ReadTypeInfo(BinaryReader reader)
    {
        return new DebugTypeInfo(
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadString());
    }

    private static void WriteTimestamp(BinaryWriter writer, DateTimeOffset value)
    {
        writer.Write(value.UtcTicks);
    }

    private static DateTimeOffset ReadTimestamp(BinaryReader reader)
    {
        return new DateTimeOffset(new DateTime(reader.ReadInt64(), DateTimeKind.Utc));
    }

    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            writer.Write(value);
        }
    }

    private static string? ReadNullableString(BinaryReader reader)
    {
        return reader.ReadBoolean() ? reader.ReadString() : null;
    }

    private static void WriteNullableDouble(BinaryWriter writer, double? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            writer.Write(value.Value);
        }
    }

    private static double? ReadNullableDouble(BinaryReader reader)
    {
        return reader.ReadBoolean() ? reader.ReadDouble() : null;
    }

    private static void WriteByteArray(BinaryWriter writer, byte[] value)
    {
        writer.Write(value.Length);
        writer.Write(value);
    }

    private static byte[] ReadByteArray(BinaryReader reader)
    {
        return reader.ReadBytes(reader.ReadInt32());
    }

    private static void WriteIntList(BinaryWriter writer, IReadOnlyList<int> values)
    {
        writer.Write(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            writer.Write(values[i]);
        }
    }

    private static int[] ReadIntArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var values = new int[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadInt32();
        }

        return values;
    }

    private static void WriteStringList(BinaryWriter writer, IReadOnlyList<string> values)
    {
        writer.Write(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            writer.Write(values[i]);
        }
    }

    private static string[] ReadStringArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var values = new string[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadString();
        }

        return values;
    }

    private static void WriteList<T>(BinaryWriter writer, IReadOnlyList<T> values, Action<BinaryWriter, T> write)
    {
        writer.Write(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            write(writer, values[i]);
        }
    }

    private static T[] ReadList<T>(BinaryReader reader, Func<BinaryReader, T> read)
    {
        var count = reader.ReadInt32();
        var values = new T[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = read(reader);
        }

        return values;
    }

    private static void WriteNullableList<T>(
        BinaryWriter writer,
        IReadOnlyList<T>? values,
        Action<BinaryWriter, T> write)
    {
        writer.Write(values is not null);
        if (values is not null)
        {
            WriteList(writer, values, write);
        }
    }

    private static T[]? ReadNullableList<T>(BinaryReader reader, Func<BinaryReader, T> read)
    {
        return reader.ReadBoolean() ? ReadList(reader, read) : null;
    }

    private static void WriteNullable<T>(BinaryWriter writer, T? value, Action<BinaryWriter, T> write)
        where T : class
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            write(writer, value);
        }
    }

    private static T? ReadNullable<T>(BinaryReader reader, Func<BinaryReader, T> read)
        where T : class
    {
        return reader.ReadBoolean() ? read(reader) : null;
    }
}
