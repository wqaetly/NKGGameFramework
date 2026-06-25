using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using NKGGameFramework.Core;
using NKGGameFramework.Diagnostics;
using NKGGameFramework.Ecs;
using NKGGameFramework.Hosting.Diagnostics;

namespace NKGGameFramework.Tests.Hosting;

[Collection(GameDebugRegistryCollection.Name)]
public sealed class GameDebugHostTests
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public async Task Host_serves_snapshot_from_framework_registry()
    {
        GameDebugRuntimeRegistry.Clear();
        GameDebugFramePublisher.Shared.Reset();

        try
        {
            using var runtime = new RuntimeContext();
            using var world = new World("host-debug-world");
            var scene = world.CreateScene("battle");
            var entity = scene.CreateEntity()
                .Add(new PositionComponent(12, 34));
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = 0;

            var message = await GetSnapshotAfterRuntimeFrameAsync(
                client,
                "/_nkg/debug/snapshot",
                () => runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, ++frame)),
                timeout.Token);

            Assert.NotNull(message);
            Assert.Equal(nameof(RuntimeContext), message.Frame.Source);
            Assert.True(message.Frame.Frame >= 1);
            Assert.Contains(message.Snapshot.Runtimes, runtimeSnapshot => runtimeSnapshot.IsDisposed is false);
            var worldSnapshot = Assert.Single(
                message.Snapshot.Worlds,
                worldSnapshot => worldSnapshot.Name == world.Name);
            var sceneSnapshot = Assert.Single(worldSnapshot.Scenes);
            Assert.Equal(scene.Name, sceneSnapshot.Name);
            Assert.Contains(sceneSnapshot.Entities, entitySnapshot => entitySnapshot.Id == entity.Id.Value);
        }
        finally
        {
            GameDebugRuntimeRegistry.Clear();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public async Task Host_accepts_debug_control_commands()
    {
        GameDebugController.Shared.Reset();

        try
        {
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };

            var pauseResponse = await client.PostAsJsonAsync(
                "/_nkg/debug/control",
                new GameDebugControlRequest("pause"),
                JsonOptions);
            var pause = await pauseResponse.Content.ReadFromJsonAsync<GameDebugControlResult>(JsonOptions);

            Assert.True(pauseResponse.IsSuccessStatusCode);
            Assert.NotNull(pause);
            Assert.True(pause.Succeeded);
            Assert.True(pause.State.IsPaused);

            var stepResponse = await client.PostAsJsonAsync(
                "/_nkg/debug/control",
                new GameDebugControlRequest("step", StepCount: 1),
                JsonOptions);
            var step = await stepResponse.Content.ReadFromJsonAsync<GameDebugControlResult>(JsonOptions);

            Assert.True(stepResponse.IsSuccessStatusCode);
            Assert.NotNull(step);
            Assert.True(step.Succeeded);
            Assert.Equal(1, step.State.PendingStepCount);

            var state = await client.GetFromJsonAsync<GameDebugControlState>(
                "/_nkg/debug/control",
                JsonOptions);

            Assert.NotNull(state);
            Assert.True(state.IsPaused);
            Assert.Equal(1, state.PendingStepCount);
        }
        finally
        {
            GameDebugController.Shared.Reset();
        }
    }

    [Fact]
    public async Task Host_debug_control_commands_gate_framework_updates()
    {
        GameDebugController.Shared.Reset();

        try
        {
            using var runtime = new RuntimeContext();
            var module = runtime.RegisterModule(new CountingUpdateModule());
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };

            await client.PostAsJsonAsync(
                "/_nkg/debug/control",
                new GameDebugControlRequest("pause"),
                JsonOptions);
            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));

            Assert.Equal(0, module.UpdateCount);
            Assert.Equal(0, runtime.Time.Frame);

            await client.PostAsJsonAsync(
                "/_nkg/debug/control",
                new GameDebugControlRequest("step"),
                JsonOptions);
            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));

            Assert.Equal(1, module.UpdateCount);
            Assert.Equal(1, runtime.Time.Frame);

            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 2));

            Assert.Equal(1, module.UpdateCount);
            Assert.Equal(1, runtime.Time.Frame);

            await client.PostAsJsonAsync(
                "/_nkg/debug/control",
                new GameDebugControlRequest("play"),
                JsonOptions);
            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 2));

            Assert.Equal(2, module.UpdateCount);
            Assert.Equal(2, runtime.Time.Frame);
        }
        finally
        {
            GameDebugController.Shared.Reset();
        }
    }

    [Fact]
    public async Task Host_mutations_apply_at_runtime_frame_boundary()
    {
        GameDebugRuntimeRegistry.Clear();
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();

        try
        {
            using var runtime = new RuntimeContext();
            using var world = new World("mutation-boundary-world");
            var scene = world.CreateScene("battle");
            var entity = scene.CreateEntity()
                .Add(new PositionComponent(12, 34));
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
                options.EnableMutations = true;
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var valueSerializer = new OdinGameDebugComponentValueSerializer();
            var componentType = typeof(PositionComponent);
            var mutation = new GameDebugMutationRequest(
                world.Name,
                scene.Name,
                entity.Id.Value,
                entity.Version,
                componentType.FullName!,
                componentType.Assembly.GetName().Name!,
                valueSerializer.Serialize(new PositionComponent(99, 34)));

            var mutationTask = PostMutationAsync(client, mutation, timeout.Token);
            await Task.Delay(50, timeout.Token);

            Assert.False(mutationTask.IsCompleted);
            Assert.Equal(12, entity.Get<PositionComponent>().X);

            var frame = 0;
            for (var attempt = 0; attempt < 10 && !mutationTask.IsCompleted; attempt++)
            {
                runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, ++frame));
                await Task.Delay(10, timeout.Token);
            }

            var result = await mutationTask.WaitAsync(timeout.Token);

            Assert.True(result.Succeeded);
            Assert.Equal(99, entity.Get<PositionComponent>().X);

            var snapshot = await GetSnapshotAfterRuntimeFrameAsync(
                client,
                CreateSnapshotPath(
                    world,
                    scene,
                    includePayload: true,
                    includeStructured: true,
                    entityId: entity.Id.Value),
                () => runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, ++frame)),
                timeout.Token);
            var snapshotScene = FindSceneSnapshot(snapshot.Snapshot, world, scene);
            var snapshotEntity = Assert.Single(snapshotScene.Entities);
            var snapshotComponent = Assert.Single(
                snapshotEntity.Components,
                component => component.Type.Name == nameof(PositionComponent));
            var structured = snapshotComponent.Value.Structured;

            Assert.NotNull(structured);
            var x = FindChild(structured!, nameof(PositionComponent.X));
            Assert.Equal(99, double.Parse(x.Value!, CultureInfo.InvariantCulture));
        }
        finally
        {
            GameDebugRuntimeRegistry.Clear();
            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public async Task Host_snapshot_requests_observe_framework_changes_after_each_runtime_frame()
    {
        GameDebugRuntimeRegistry.Clear();
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();

        try
        {
            using var runtime = new RuntimeContext();
            using var world = new World("auto-debug-world");
            var scene = world.CreateScene("battle");
            var tracked = scene.CreateEntity()
                .Add(new PositionComponent(0, 0));
            runtime.RegisterModule(new FrameMutationModule(scene, tracked));
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = 0;

            for (var iteration = 0; iteration < 3; iteration++)
            {
                var summary = await GetSnapshotAfterRuntimeFrameAsync(
                    client,
                    CreateSnapshotPath(world, scene, includePayload: false, includeStructured: false),
                    () => runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, ++frame)),
                    timeout.Token);
                var summaryScene = FindSceneSnapshot(summary.Snapshot, world, scene);
                var positionStore = Assert.Single(
                    summaryScene.ComponentStores,
                    store => store.Type.Name == nameof(PositionComponent));
                var trackedSummary = Assert.Single(
                    summaryScene.Entities,
                    entity => entity.Id == tracked.Id.Value);
                var trackedSummaryComponent = Assert.Single(
                    trackedSummary.Components,
                    component => component.Type.Name == nameof(PositionComponent));

                Assert.Equal((int)summary.Frame.Frame + 1, summaryScene.EntityCount);
                Assert.Equal((int)summary.Frame.Frame + 1, positionStore.Count);
                Assert.Null(trackedSummaryComponent.Value.Payload);
                Assert.Null(trackedSummaryComponent.Value.Structured);

                var detail = await GetSnapshotAfterRuntimeFrameAsync(
                    client,
                    CreateSnapshotPath(
                        world,
                        scene,
                        includePayload: true,
                        includeStructured: true,
                        entityId: tracked.Id.Value),
                    () => runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, ++frame)),
                    timeout.Token);
                var detailScene = FindSceneSnapshot(detail.Snapshot, world, scene);
                var trackedDetail = Assert.Single(detailScene.Entities);
                var trackedDetailComponent = Assert.Single(
                    trackedDetail.Components,
                    component => component.Type.Name == nameof(PositionComponent));
                Assert.NotNull(trackedDetailComponent.Value.Structured);
                var structured = trackedDetailComponent.Value.Structured!;
                var x = FindChild(structured, nameof(PositionComponent.X));
                var y = FindChild(structured, nameof(PositionComponent.Y));

                Assert.NotNull(x.Value);
                Assert.NotNull(y.Value);
                Assert.Equal((double)detail.Frame.Frame, double.Parse(x.Value!, CultureInfo.InvariantCulture));
                Assert.Equal(detail.Frame.Frame * 10, double.Parse(y.Value!, CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            GameDebugRuntimeRegistry.Clear();
            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public async Task Host_snapshot_requests_can_filter_component_detail_payloads()
    {
        GameDebugRuntimeRegistry.Clear();
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();

        try
        {
            using var runtime = new RuntimeContext();
            using var world = new World("component-detail-world");
            var scene = world.CreateScene("battle");
            var entity = scene.CreateEntity()
                .Add(new PositionComponent(12, 34))
                .Add(new VelocityComponent(56, 78));
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = 0;

            var detail = await GetSnapshotAfterRuntimeFrameAsync(
                client,
                CreateSnapshotPath(
                    world,
                    scene,
                    includePayload: true,
                    includeStructured: true,
                    entityId: entity.Id.Value,
                    componentType: typeof(PositionComponent)),
                () => runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, ++frame)),
                timeout.Token);
            var detailScene = FindSceneSnapshot(detail.Snapshot, world, scene);
            var detailEntity = Assert.Single(detailScene.Entities);
            var component = Assert.Single(detailEntity.Components);

            Assert.Equal(nameof(PositionComponent), component.Type.Name);
            Assert.NotNull(component.Value.Payload);
            Assert.NotNull(component.Value.Structured);
        }
        finally
        {
            GameDebugRuntimeRegistry.Clear();
            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public async Task Host_stream_pushes_summary_snapshot_after_framework_frame()
    {
        GameDebugRuntimeRegistry.Clear();
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();

        try
        {
            using var runtime = new RuntimeContext();
            using var world = new World("stream-debug-world");
            var scene = world.CreateScene("battle");
            var tracked = scene.CreateEntity()
                .Add(new PositionComponent(0, 0));
            runtime.RegisterModule(new FrameMutationModule(scene, tracked));
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.GetAsync(
                CreateStreamPath(world, scene),
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var reader = new StreamReader(stream);

            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));

            var pushed = await ReadSseDataAsync<GameDebugSnapshotMessage>(reader, timeout.Token);
            var pushedScene = FindSceneSnapshot(pushed.Snapshot, world, scene);
            var positionStore = Assert.Single(
                pushedScene.ComponentStores,
                store => store.Type.Name == nameof(PositionComponent));

            Assert.Equal(nameof(RuntimeContext), pushed.Frame.Source);
            Assert.Equal(1, pushed.Frame.Frame);
            Assert.True(pushed.Frame.Sequence > 0);
            Assert.Equal(2, pushedScene.EntityCount);
            Assert.Equal(2, positionStore.Count);
            Assert.False(pushed.Control.IsPaused);
        }
        finally
        {
            GameDebugRuntimeRegistry.Clear();
            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public async Task Host_stream_snapshots_match_the_published_frame_event()
    {
        GameDebugRuntimeRegistry.Clear();
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();

        try
        {
            using var runtime = new RuntimeContext();
            using var world = new World("stream-consistency-world");
            var scene = world.CreateScene("battle");
            var tracked = scene.CreateEntity()
                .Add(new PositionComponent(0, 0));
            runtime.RegisterModule(new FrameMutationModule(scene, tracked));
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await client.GetAsync(
                CreateStreamPath(world, scene),
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var reader = new StreamReader(stream);

            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));
            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 2));

            var first = await ReadSseDataAsync<GameDebugSnapshotMessage>(reader, timeout.Token);
            var second = await ReadSseDataAsync<GameDebugSnapshotMessage>(reader, timeout.Token);

            Assert.Equal(1, first.Frame.Frame);
            Assert.Equal(2, FindSceneSnapshot(first.Snapshot, world, scene).EntityCount);
            Assert.Equal(2, second.Frame.Frame);
            Assert.Equal(3, FindSceneSnapshot(second.Snapshot, world, scene).EntityCount);
        }
        finally
        {
            GameDebugRuntimeRegistry.Clear();
            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public async Task Host_records_debug_dump_and_returns_document_on_stop()
    {
        GameDebugRuntimeRegistry.Clear();
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();
        string? savedPath = null;

        try
        {
            using var runtime = new RuntimeContext();
            using var world = new World("dump-debug-world");
            var scene = world.CreateScene("battle");
            var tracked = scene.CreateEntity()
                .Add(new PositionComponent(0, 0));
            runtime.RegisterModule(new FrameMutationModule(scene, tracked));
            await using var host = await GameDebugHost.StartAsync(options =>
            {
                options.Url = "http://127.0.0.1:0";
            });
            using var client = new HttpClient
            {
                BaseAddress = host.BaseAddress,
            };

            var startResponse = await client.PostAsJsonAsync(
                "/_nkg/debug/dump/recording",
                new GameDebugDumpRecordingRequest("start", "host-dump-test"),
                JsonOptions);
            var start = await startResponse.Content.ReadFromJsonAsync<GameDebugDumpRecordingResult>(JsonOptions);

            Assert.True(startResponse.IsSuccessStatusCode);
            Assert.NotNull(start);
            Assert.True(start.Succeeded);
            Assert.True(start.State.IsRecording);
            Assert.Equal(0, start.State.FrameCount);

            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 1));
            runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame: 2));

            var stopResponse = await client.PostAsJsonAsync(
                "/_nkg/debug/dump/recording",
                new GameDebugDumpRecordingRequest("stop"),
                JsonOptions);
            var stop = await stopResponse.Content.ReadFromJsonAsync<GameDebugDumpRecordingResult>(JsonOptions);

            Assert.True(stopResponse.IsSuccessStatusCode);
            Assert.NotNull(stop);
            Assert.True(stop.Succeeded);
            Assert.False(stop.State.IsRecording);

            savedPath = stop.State.LastDumpPath;
            Assert.False(string.IsNullOrWhiteSpace(savedPath));
            Assert.True(File.Exists(savedPath));
            Assert.Equal(GameDebugDumpFile.FileExtension, Path.GetExtension(savedPath));
            Assert.NotEqual((byte)'{', File.ReadAllBytes(savedPath)[0]);

            var openResponse = await client.PostAsJsonAsync(
                "/_nkg/debug/dump/playback",
                new GameDebugDumpPlaybackOpenRequest(savedPath),
                JsonOptions);
            var playback = await openResponse.Content.ReadFromJsonAsync<GameDebugDumpPlaybackManifest>(JsonOptions);

            Assert.True(openResponse.IsSuccessStatusCode);
            Assert.NotNull(playback);
            Assert.Equal("nkg.debug.dump", playback.Format);
            Assert.Equal(1, playback.Version);
            Assert.Equal(2, playback.Frames.Count);
            Assert.All(playback.Frames, frame => Assert.Equal(nameof(RuntimeContext), frame.Frame.Source));
            Assert.All(playback.Frames, frame =>
            {
                Assert.NotNull(frame.Frame.Metrics);
                Assert.Equal(0.016, frame.Frame.Metrics.DeltaSeconds, precision: 6);
                Assert.Equal(0.016, frame.Frame.Metrics.RealDeltaSeconds, precision: 6);
                Assert.True(frame.Frame.Metrics.LogicMilliseconds > 0d);
                Assert.True(frame.Frame.Metrics.LogicFramesPerSecond > 0d);
            });

            var lastFrame = await client.GetFromJsonAsync<GameDebugSnapshotMessage>(
                $"/_nkg/debug/dump/playback/frame?playbackId={playback.Id}&frameIndex=1",
                JsonOptions);
            Assert.NotNull(lastFrame);
            Assert.Equal(2, lastFrame.Frame.Frame);
            Assert.Contains(
                lastFrame.Snapshot.Worlds,
                worldSnapshot => worldSnapshot.Name == world.Name);

            using var uploadContent = new ByteArrayContent(await File.ReadAllBytesAsync(savedPath));
            uploadContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var uploadResponse = await client.PostAsync("/_nkg/debug/dump/playback/upload", uploadContent);
            var uploadedPlayback = await uploadResponse.Content.ReadFromJsonAsync<GameDebugDumpPlaybackManifest>(JsonOptions);

            Assert.True(uploadResponse.IsSuccessStatusCode);
            Assert.NotNull(uploadedPlayback);
            Assert.Equal(2, uploadedPlayback.Frames.Count);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
            {
                File.Delete(savedPath);
            }

            GameDebugRuntimeRegistry.Clear();
            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public void Dump_recorder_uses_recording_request_dump_directory()
    {
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();
        var dumpDirectory = Path.Combine(
            Path.GetTempPath(),
            "NKGGameFramework.Tests",
            Guid.NewGuid().ToString("N"));
        var fallbackDirectory = Path.Combine(
            Path.GetTempPath(),
            "NKGGameFramework.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            using var recorder = new GameDebugDumpRecorder(
                new EmptySnapshotProvider(),
                GameDebugController.Shared,
                GameDebugFramePublisher.Shared,
                new GameDebugOptions
                {
                    DumpDirectory = fallbackDirectory,
                });

            var start = recorder.Execute(new GameDebugDumpRecordingRequest(
                "start",
                "request-directory-test",
                dumpDirectory));
            Assert.True(start.Succeeded);

            var stop = recorder.Execute(new GameDebugDumpRecordingRequest("stop"));
            Assert.True(stop.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(stop.State.LastDumpPath));
            Assert.True(File.Exists(stop.State.LastDumpPath));
            Assert.Equal(GameDebugDumpFile.FileExtension, Path.GetExtension(stop.State.LastDumpPath));
            Assert.Equal(
                Path.GetFullPath(dumpDirectory),
                Path.GetDirectoryName(stop.State.LastDumpPath));
            Assert.False(Directory.Exists(fallbackDirectory));
        }
        finally
        {
            if (Directory.Exists(dumpDirectory))
            {
                Directory.Delete(dumpDirectory, true);
            }

            if (Directory.Exists(fallbackDirectory))
            {
                Directory.Delete(fallbackDirectory, true);
            }

            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public void Dump_recorder_keeps_all_recorded_frames()
    {
        GameDebugController.Shared.Reset();
        GameDebugFramePublisher.Shared.Reset();
        string? savedPath = null;

        try
        {
            using var runtime = new RuntimeContext();
            using var recorder = new GameDebugDumpRecorder(
                new EmptySnapshotProvider(),
                GameDebugController.Shared,
                GameDebugFramePublisher.Shared,
                new GameDebugOptions());

            var start = recorder.Execute(new GameDebugDumpRecordingRequest("start", "bounded-window-test"));
            Assert.True(start.Succeeded);
            Assert.Equal(0, start.State.FrameCount);

            for (var frame = 1; frame <= 5; frame++)
            {
                runtime.Update(GameFrameTime.FromSeconds(0.016, 0.016, frame));
            }

            var recording = recorder.GetState();
            Assert.True(recording.IsRecording);
            Assert.Equal(5, recording.FrameCount);
            Assert.Equal(0, recording.DroppedFrameCount);

            var stop = recorder.Execute(new GameDebugDumpRecordingRequest("stop"));
            savedPath = stop.State.LastDumpPath;

            Assert.True(stop.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(savedPath));
            Assert.Equal(GameDebugDumpFile.FileExtension, Path.GetExtension(savedPath));
            var playback = recorder.OpenPlayback(new GameDebugDumpPlaybackOpenRequest(savedPath));

            Assert.Equal(0, playback.DroppedFrameCount);
            Assert.Equal(5, playback.Frames.Count);
            Assert.Equal(1, playback.Frames[0].Frame.Frame);
            Assert.Equal(2, playback.Frames[1].Frame.Frame);
            Assert.Equal(3, playback.Frames[2].Frame.Frame);
            Assert.Equal(4, playback.Frames[3].Frame.Frame);
            Assert.Equal(5, playback.Frames[4].Frame.Frame);
            Assert.All(playback.Frames, frame =>
            {
                Assert.NotNull(frame.Frame.Metrics);
                Assert.Equal(0.016, frame.Frame.Metrics.DeltaSeconds, precision: 6);
                Assert.Equal(0.016, frame.Frame.Metrics.RealDeltaSeconds, precision: 6);
                Assert.True(frame.Frame.Metrics.LogicMilliseconds >= 0d);
                Assert.True(frame.Frame.Metrics.LogicFramesPerSecond >= 0d);
            });
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
            {
                File.Delete(savedPath);
            }

            GameDebugController.Shared.Reset();
            GameDebugFramePublisher.Shared.Reset();
        }
    }

    [Fact]
    public async Task AutoStart_returns_null_when_code_options_are_disabled()
    {
        await GameDebugHostAutoStart.StopAsync();

        var host = await GameDebugHostAutoStart.TryStartAsync(GameDebugHostStartupOptions.Disabled);

        Assert.Null(host);
        Assert.Null(GameDebugHostAutoStart.BaseAddress);
    }

    [Fact]
    public async Task AutoStart_starts_once_from_code_options()
    {
        await GameDebugHostAutoStart.StopAsync();

        try
        {
            var startup = new GameDebugHostStartupOptions
            {
                Enabled = true,
                Url = "http://127.0.0.1:0",
                EnableMutations = true,
            };
            var first = await GameDebugHostAutoStart.TryStartAsync(startup);
            var second = await GameDebugHostAutoStart.TryStartAsync(startup);

            Assert.NotNull(first);
            Assert.Same(first, second);
            Assert.Equal(first.BaseAddress, GameDebugHostAutoStart.BaseAddress);

            using var client = new HttpClient
            {
                BaseAddress = first.BaseAddress,
            };
            var health = await client.GetStringAsync("/_nkg/debug/health");
            Assert.Contains("\"status\":\"ok\"", health, StringComparison.Ordinal);
        }
        finally
        {
            await GameDebugHostAutoStart.StopAsync();
        }
    }

    private readonly record struct PositionComponent(double X, double Y) : IComponent;

    private readonly record struct VelocityComponent(double X, double Y) : IComponent;

    private sealed class FrameMutationModule : Module, IUpdateModule
    {
        private readonly Scene _scene;
        private readonly Entity _tracked;

        public FrameMutationModule(Scene scene, Entity tracked)
        {
            _scene = scene;
            _tracked = tracked;
        }

        public void Update(in GameFrameTime time)
        {
            _tracked.Add(new PositionComponent(time.Frame, time.Frame * 10));
            _scene.CreateEntity()
                .Add(new PositionComponent(time.Frame, time.Frame));
        }
    }

    private sealed class CountingUpdateModule : Module, IUpdateModule
    {
        public int UpdateCount { get; private set; }

        public void Update(in GameFrameTime time)
        {
            UpdateCount++;
        }
    }

    private sealed class EmptySnapshotProvider : IGameDebugSnapshotProvider
    {
        public GameDebugSnapshot Capture(GameDebugSnapshotCaptureOptions? options = null)
        {
            return new GameDebugSnapshot(DateTimeOffset.UtcNow, [], []);
        }
    }

    private static string CreateSnapshotPath(
        World world,
        Scene scene,
        bool includePayload,
        bool includeStructured,
        int? entityId = null,
        Type? componentType = null)
    {
        return CreateDebugPath("snapshot", world, scene, includePayload, includeStructured, entityId, componentType);
    }

    private static string CreateStreamPath(World world, Scene scene)
    {
        return CreateDebugPath("stream", world, scene, includePayload: false, includeStructured: false);
    }

    private static string CreateDebugPath(
        string endpoint,
        World world,
        Scene scene,
        bool includePayload,
        bool includeStructured,
        int? entityId = null,
        Type? componentType = null)
    {
        var query = new List<string>
        {
            $"worldName={Uri.EscapeDataString(world.Name)}",
            $"sceneName={Uri.EscapeDataString(scene.Name)}",
            $"includePayload={includePayload.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}",
            $"includeStructured={includeStructured.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}",
        };

        if (entityId is { } id)
        {
            query.Add($"entityId={id.ToString(CultureInfo.InvariantCulture)}");
        }

        if (componentType is not null)
        {
            query.Add($"componentTypeFullName={Uri.EscapeDataString(componentType.FullName!)}");
            query.Add($"componentAssemblyName={Uri.EscapeDataString(componentType.Assembly.GetName().Name!)}");
        }

        return $"/_nkg/debug/{endpoint}?{string.Join("&", query)}";
    }

    private static SceneDebugSnapshot FindSceneSnapshot(
        GameDebugSnapshot? snapshot,
        World world,
        Scene scene)
    {
        Assert.NotNull(snapshot);
        var worldSnapshot = Assert.Single(
            snapshot.Worlds,
            candidate => candidate.Name == world.Name);
        return Assert.Single(
            worldSnapshot.Scenes,
            candidate => candidate.Name == scene.Name);
    }

    private static ComponentValueDebugNode FindChild(ComponentValueDebugNode node, string name)
    {
        return Assert.Single(node.Children, child => child.Name == name);
    }

    private static async Task<GameDebugSnapshotMessage> GetSnapshotAfterRuntimeFrameAsync(
        HttpClient client,
        string path,
        Action advanceFrame,
        CancellationToken cancellationToken)
    {
        var request = client.GetFromJsonAsync<GameDebugSnapshotMessage>(
            path,
            JsonOptions,
            cancellationToken);
        for (var attempt = 0; attempt < 10 && !request.IsCompleted; attempt++)
        {
            advanceFrame();
            await Task.Delay(10, cancellationToken);
        }

        return await request.WaitAsync(cancellationToken)
            ?? throw new JsonException("The debug snapshot response was empty.");
    }

    private static async Task<GameDebugMutationResult> PostMutationAsync(
        HttpClient client,
        GameDebugMutationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            "/_nkg/debug/mutations",
            request,
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<GameDebugMutationResult>(
            JsonOptions,
            cancellationToken)
            ?? throw new JsonException("The debug mutation response was empty.");
    }

    private static async Task<T> ReadSseDataAsync<T>(StreamReader reader, CancellationToken cancellationToken)
    {
        var data = string.Empty;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new EndOfStreamException("The debug stream ended before an SSE data event was received.");
            }

            if (line.Length == 0)
            {
                if (data.Length == 0)
                {
                    continue;
                }

                return JsonSerializer.Deserialize<T>(data, JsonOptions)
                    ?? throw new JsonException($"Could not deserialize SSE data as '{typeof(T).Name}'.");
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data += line["data: ".Length..];
            }
        }
    }

}
