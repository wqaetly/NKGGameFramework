import { createContext, lazy, Suspense, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import {
  DockviewReact,
  type DockviewApi,
  type DockviewReadyEvent,
  type IDockviewPanelProps,
  type SerializedDockview,
} from 'dockview-react';
import 'dockview-react/dist/styles/dockview.css';
import {
  Activity,
  Boxes,
  Bug,
  Cpu,
  Circle,
  Pause,
  Play,
  RefreshCw,
  Search,
  SkipForward,
  Sparkles,
  Square,
  Upload,
  X,
} from 'lucide-react';
import {
  createDebugSnapshotStream,
  fetchDumpRecordingState,
  fetchDebugSnapshotMessage,
  postDebugControl,
  postDumpRecording,
  postDebugMutation,
} from './api';
import { countComponentGroups, type ComponentMutationExecutor } from './componentGraphModel';
import type {
  ComponentStoreDebugSnapshot,
  EntityDebugSnapshot,
  GameDebugControlCommand,
  GameDebugControlState,
  GameDebugDumpDocument,
  GameDebugDumpRecordingState,
  GameDebugSnapshotMessage,
  GameDebugSnapshot,
  ModuleDebugSnapshot,
  ProcedureModuleDebugSnapshot,
  RuntimeContextDebugSnapshot,
  SceneDebugSnapshot,
  SystemDebugSnapshot,
  WorldDebugSnapshot,
} from './types';

type LoadState = 'idle' | 'loading' | 'ready' | 'error';

interface SceneSelection {
  worldName: string;
  sceneName: string;
}

type SceneEntry = {
  world: WorldDebugSnapshot;
  scene: SceneDebugSnapshot;
};

type DebugTotals = {
  worlds: number;
  scenes: number;
  entities: number;
  modules: number;
  systems: number;
  procedures: number;
};

type DockWorkspaceModel = {
  snapshot: GameDebugSnapshot | null;
  totals: DebugTotals;
  scenes: SceneEntry[];
  activeSceneEntry: SceneEntry | null;
  activeScene: SceneDebugSnapshot | null;
  filteredEntities: EntityDebugSnapshot[];
  selectedEntity: EntityDebugSnapshot | null;
  selectedEntityLoading: boolean;
  isDumpMode: boolean;
  selectScene: (entry: SceneEntry) => void;
  selectEntity: (entityId: number) => void;
  onSaveComponent: ComponentMutationExecutor;
};

type DockPanelProps = IDockviewPanelProps<Record<string, never>>;

const DOCK_LAYOUT_STORAGE_KEY = 'nkg.webdebug.layout.v3';
const LEGACY_DOCK_LAYOUT_STORAGE_KEYS = ['nkg.webdebug.layout.v1', 'nkg.webdebug.layout.v2'];
const FRAME_STREAM_STORAGE_KEY = 'nkg.webdebug.frameStream';
const LEGACY_FRAME_POLLING_STORAGE_KEY = 'nkg.webdebug.framePolling';
const LEGACY_AUTO_REFRESH_STORAGE_KEY = 'nkg.webdebug.autoRefresh';
const DOCK_PANEL_TITLES: Record<string, string> = {
  components: 'Components',
  scenes: 'Scenes',
  entities: 'Entities',
  runtime: 'Runtime',
  diagnostics: 'Diagnostics',
};

const ComponentGraphCanvas = lazy(() =>
  import('./ComponentGraphCanvas').then((module) => ({ default: module.ComponentGraphCanvas })),
);

const dockPanelComponents = {
  scenes: ScenesDockPanel,
  entities: EntitiesDockPanel,
  components: ComponentsDockPanel,
  runtime: RuntimeDockPanel,
  diagnostics: DiagnosticsDockPanel,
} satisfies Record<string, React.FunctionComponent<DockPanelProps>>;

const DockWorkspaceContext = createContext<DockWorkspaceModel | null>(null);

export function App() {
  const [snapshot, setSnapshot] = useState<GameDebugSnapshot | null>(null);
  const [loadState, setLoadState] = useState<LoadState>('idle');
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [control, setControl] = useState<GameDebugControlState | null>(null);
  const [query, setQuery] = useState('');
  const [selection, setSelection] = useState<SceneSelection | null>(null);
  const [selectedEntityId, setSelectedEntityId] = useState<number | null>(null);
  const [entityDetails, setEntityDetails] = useState<Record<string, EntityDebugSnapshot>>({});
  const [dockRevision, setDockRevision] = useState(0);
  const [frameStream, setFrameStream] = useState(readStoredFrameStream);
  const [dump, setDump] = useState<GameDebugDumpDocument | null>(null);
  const [dumpFrameIndex, setDumpFrameIndex] = useState(0);
  const [dumpPlaying, setDumpPlaying] = useState(false);
  const [dumpRecording, setDumpRecording] = useState<GameDebugDumpRecordingState | null>(null);
  const [dumpBusy, setDumpBusy] = useState(false);
  const refreshInFlightRef = useRef(false);
  const entityDetailLoadKeyRef = useRef<string | null>(null);
  const activeSceneEntryRef = useRef<SceneEntry | null>(null);
  const entityDetailsRef = useRef<Record<string, EntityDebugSnapshot>>({});
  const selectedEntityOverviewRef = useRef<EntityDebugSnapshot | null>(null);
  const selectedEntityDetailKeyRef = useRef<string | null>(null);
  const dumpFileInputRef = useRef<HTMLInputElement | null>(null);
  const dumpModeRef = useRef(false);
  const dumpFrames = dump?.frames ?? [];
  const dumpMode = dumpFrames.length > 0;
  const activeDumpFrame = dumpFrames[dumpFrameIndex] ?? null;

  const commitSnapshotMessage = useCallback((
    message: GameDebugSnapshotMessage,
    options: { clearEntityDetails?: boolean } = {},
  ) => {
    setError(null);
    setSnapshot(message.snapshot);
    setControl(message.control);
    if (options.clearEntityDetails) {
      setEntityDetails({});
    }
    setLoadState('ready');
  }, []);

  const commitEntityDetail = useCallback((
    message: GameDebugSnapshotMessage,
    entity: EntityDebugSnapshot,
    detailKey: string,
  ) => {
    setControl(message.control);
    setEntityDetails((current) => ({
      ...current,
      [detailKey]: entity,
    }));
  }, []);

  const loadDumpDocument = useCallback((nextDump: GameDebugDumpDocument) => {
    validateDumpDocument(nextDump);
    dumpModeRef.current = true;
    setDump(nextDump);
    setDumpFrameIndex(0);
    setDumpPlaying(false);
    setFrameStream(false);
    setEntityDetails({});
    commitSnapshotMessage(nextDump.frames[0], {
      clearEntityDetails: true,
    });
  }, [commitSnapshotMessage]);

  const refresh = useCallback(async (
    signal?: AbortSignal,
    options: { clearEntityDetails?: boolean } = {},
  ) => {
    if (refreshInFlightRef.current) {
      return;
    }

    refreshInFlightRef.current = true;
    setIsRefreshing(true);
    setLoadState((current) => (current === 'ready' ? current : 'loading'));
    setError(null);

    try {
      const next = await fetchDebugSnapshotMessage(signal, {
        includePayload: false,
        includeStructured: false,
      });
      if (dumpModeRef.current) {
        return;
      }

      commitSnapshotMessage(next, {
        clearEntityDetails: options.clearEntityDetails ?? true,
      });
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === 'AbortError') {
        return;
      }

      setError(caught instanceof Error ? caught.message : String(caught));
      setLoadState('error');
    } finally {
      refreshInFlightRef.current = false;
      setIsRefreshing(false);
    }
  }, [commitSnapshotMessage]);

  const loadEntityDetail = useCallback(async (
    worldName: string,
    sceneName: string,
    entityId: number,
    detailKey: string,
    signal?: AbortSignal,
  ) => {
    if (entityDetailLoadKeyRef.current === detailKey) {
      return;
    }

    entityDetailLoadKeyRef.current = detailKey;

    try {
      const detailMessage = await fetchDebugSnapshotMessage(signal, {
        worldName,
        sceneName,
        entityId,
        includePayload: true,
        includeStructured: true,
      });
      const entity = findEntityDetail(
        detailMessage.snapshot,
        worldName,
        sceneName,
        entityId,
      );
      if (!entity) {
        throw new Error(`Entity #${entityId} was not found in the debug snapshot.`);
      }

      if (dumpModeRef.current) {
        return;
      }

      commitEntityDetail(detailMessage, entity, detailKey);
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === 'AbortError') {
        return;
      }

      setError(caught instanceof Error ? caught.message : String(caught));
    } finally {
      if (entityDetailLoadKeyRef.current === detailKey) {
        entityDetailLoadKeyRef.current = null;
      }
    }
  }, [commitEntityDetail]);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);
    return () => controller.abort();
  }, [refresh]);

  useEffect(() => {
    const controller = new AbortController();
    fetchDumpRecordingState(controller.signal)
      .then(setDumpRecording)
      .catch(() => {
        // Older hosts can still serve the live inspector without dump recording.
      });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!activeDumpFrame) {
      return;
    }

    commitSnapshotMessage(activeDumpFrame, {
      clearEntityDetails: true,
    });
  }, [activeDumpFrame, commitSnapshotMessage]);

  const scenes = useMemo(() => flattenScenes(snapshot), [snapshot]);
  const activeSceneEntry = useMemo(
    () => findActiveSceneEntry(scenes, selection) ?? scenes[0] ?? null,
    [scenes, selection],
  );
  const activeScene = activeSceneEntry?.scene ?? null;
  const activeWorldName = activeSceneEntry?.world.name ?? null;
  const activeSceneName = activeSceneEntry?.scene.name ?? null;

  useEffect(() => {
    if (!selection && scenes[0]) {
      setSelection({ worldName: scenes[0].world.name, sceneName: scenes[0].scene.name });
    }
  }, [scenes, selection]);

  const filteredEntities = useMemo(
    () => filterEntities(activeScene?.entities ?? [], query),
    [activeScene, query],
  );

  const selectedEntityOverview = useMemo(() => {
    if (!filteredEntities.length) {
      return null;
    }

    return filteredEntities.find((entity) => entity.id === selectedEntityId) ?? filteredEntities[0];
  }, [filteredEntities, selectedEntityId]);
  const selectedEntityOverviewId = selectedEntityOverview?.id ?? null;
  const selectedEntityDetailKey = useMemo(
    () => activeWorldName && activeSceneName && selectedEntityOverviewId !== null
      ? createEntityDetailKey(
          activeWorldName,
          activeSceneName,
          selectedEntityOverviewId,
        )
      : null,
    [activeSceneName, activeWorldName, selectedEntityOverviewId],
  );
  const selectedEntity = selectedEntityDetailKey
    ? entityDetails[selectedEntityDetailKey] ?? selectedEntityOverview
    : selectedEntityOverview;
  const selectedEntityHasDetails = selectedEntity !== null && hasComponentValueDetails(selectedEntity);
  const selectedEntityLoading = !dumpMode && selectedEntity !== null && !selectedEntityHasDetails;

  useEffect(() => {
    activeSceneEntryRef.current = activeSceneEntry;
  }, [activeSceneEntry]);

  useEffect(() => {
    entityDetailsRef.current = entityDetails;
  }, [entityDetails]);

  useEffect(() => {
    selectedEntityOverviewRef.current = selectedEntityOverview;
  }, [selectedEntityOverview]);

  useEffect(() => {
    selectedEntityDetailKeyRef.current = selectedEntityDetailKey;
  }, [selectedEntityDetailKey]);

  useEffect(() => {
    writeStoredFrameStream(frameStream);
  }, [frameStream]);

  useEffect(() => {
    dumpModeRef.current = dumpMode;
  }, [dumpMode]);

  useEffect(() => {
    if (!frameStream || dumpMode) {
      return;
    }

    const stream = createDebugSnapshotStream({
      includePayload: false,
      includeStructured: false,
    });
    const handleSnapshot = (event: Event) => {
      const message = JSON.parse((event as MessageEvent<string>).data) as GameDebugSnapshotMessage;
      commitSnapshotMessage(message);

      const entry = activeSceneEntryRef.current;
      const entityOverview = selectedEntityOverviewRef.current;
      const detailKey = selectedEntityDetailKeyRef.current;
      if (
        entry &&
        entityOverview &&
        detailKey &&
        entityDetailsRef.current[detailKey] &&
        entityDetailLoadKeyRef.current !== detailKey
      ) {
        void loadEntityDetail(
          entry.world.name,
          entry.scene.name,
          entityOverview.id,
          detailKey,
        );
      }
    };

    stream.addEventListener('snapshot', handleSnapshot);
    stream.onerror = () => {
      setError('Debug frame stream disconnected. Reconnecting...');
    };

    return () => {
      stream.removeEventListener('snapshot', handleSnapshot);
      stream.close();
    };
  }, [commitSnapshotMessage, dumpMode, frameStream, loadEntityDetail]);

  useEffect(() => {
    if (!dumpMode || !dumpPlaying) {
      return;
    }

    const timer = window.setInterval(() => {
      setDumpFrameIndex((current) => {
        if (current >= dumpFrames.length - 1) {
          setDumpPlaying(false);
          return current;
        }

        return current + 1;
      });
    }, 240);

    return () => window.clearInterval(timer);
  }, [dumpFrames.length, dumpMode, dumpPlaying]);

  useEffect(() => {
    if (selectedEntityOverview && selectedEntityOverview.id !== selectedEntityId) {
      setSelectedEntityId(selectedEntityOverview.id);
    }
  }, [selectedEntityOverview, selectedEntityId]);

  useEffect(() => {
    if (
      !activeWorldName ||
      !activeSceneName ||
      selectedEntityOverviewId === null ||
      !selectedEntityDetailKey
    ) {
      return;
    }

    if (entityDetails[selectedEntityDetailKey]) {
      return;
    }

    const controller = new AbortController();
    void loadEntityDetail(
      activeWorldName,
      activeSceneName,
      selectedEntityOverviewId,
      selectedEntityDetailKey,
      controller.signal,
    );

    return () => controller.abort();
  }, [
    activeSceneName,
    activeWorldName,
    entityDetails,
    loadEntityDetail,
    selectedEntityDetailKey,
    selectedEntityOverviewId,
  ]);

  const totals = useMemo(() => summarize(snapshot), [snapshot]);

  const selectScene = useCallback((entry: SceneEntry) => {
    setSelection({ worldName: entry.world.name, sceneName: entry.scene.name });
    setSelectedEntityId(null);
  }, []);

  const selectEntity = useCallback((entityId: number) => {
    setSelectedEntityId(entityId);
  }, []);

  const resetDockLayout = useCallback(() => {
    try {
      window.localStorage.removeItem(DOCK_LAYOUT_STORAGE_KEY);
      for (const key of LEGACY_DOCK_LAYOUT_STORAGE_KEYS) {
        window.localStorage.removeItem(key);
      }
    } catch {
      // Layout reset should never block the debugger UI.
    }

    setDockRevision((revision) => revision + 1);
  }, []);

  const executeComponentMutation = useCallback<ComponentMutationExecutor>(
    async (entity, component, value) => {
      if (dumpMode) {
        setError('Dump playback is read-only.');
        return;
      }

      if (!activeSceneEntry) {
        return;
      }

      const result = await postDebugMutation({
        worldName: activeSceneEntry.world.name,
        sceneName: activeSceneEntry.scene.name,
        entityId: entity.id,
        entityVersion: entity.version,
        componentTypeFullName: component.type.fullName,
        componentAssemblyName: component.type.assemblyName,
        value,
      });

      if (!result.succeeded) {
        setError(result.message);
        return;
      }

      setEntityDetails({});
      await refresh();
    },
    [activeSceneEntry, dumpMode, refresh],
  );

  const dockModel = useMemo<DockWorkspaceModel>(
    () => ({
      snapshot,
      totals,
      scenes,
      activeSceneEntry,
      activeScene,
      filteredEntities,
      selectedEntity,
      selectedEntityLoading,
      isDumpMode: dumpMode,
      selectScene,
      selectEntity,
      onSaveComponent: executeComponentMutation,
    }),
    [
      snapshot,
      totals,
      scenes,
      activeSceneEntry,
      activeScene,
      filteredEntities,
      selectedEntity,
      selectedEntityLoading,
      dumpMode,
      selectScene,
      selectEntity,
      executeComponentMutation,
    ],
  );

  const executeControl = useCallback(
    async (command: GameDebugControlCommand) => {
      if (dumpMode) {
        if (command === 'play') {
          setDumpFrameIndex((current) => current >= dumpFrames.length - 1 ? 0 : current);
          setDumpPlaying(true);
          return;
        }

        if (command === 'pause') {
          setDumpPlaying(false);
          return;
        }

        setDumpPlaying(false);
        setDumpFrameIndex((current) => Math.min(current + 1, Math.max(0, dumpFrames.length - 1)));
        return;
      }

      const result = await postDebugControl({
        command,
        stepCount: command === 'step' ? 1 : null,
      });

      if (!result.succeeded) {
        setError(result.message);
        return;
      }

      setControl(result.state);
    },
    [dumpFrames.length, dumpMode],
  );

  const selectDumpFrame = useCallback((frameIndex: number) => {
    if (!dumpMode) {
      return;
    }

    setDumpPlaying(false);
    setDumpFrameIndex(Math.min(Math.max(0, frameIndex), dumpFrames.length - 1));
  }, [dumpFrames.length, dumpMode]);

  const clearDumpPlayback = useCallback(() => {
    dumpModeRef.current = false;
    setDump(null);
    setDumpFrameIndex(0);
    setDumpPlaying(false);
    setEntityDetails({});
  }, []);

  const returnToLive = useCallback(() => {
    clearDumpPlayback();
    void refresh(undefined, {
      clearEntityDetails: true,
    });
  }, [clearDumpPlayback, refresh]);

  const openDumpFilePicker = useCallback(() => {
    dumpFileInputRef.current?.click();
  }, []);

  const handleDumpFileChange = useCallback(async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) {
      return;
    }

    setDumpBusy(true);
    setError(null);
    try {
      const parsed = JSON.parse(await file.text()) as GameDebugDumpDocument;
      loadDumpDocument(parsed);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught));
    } finally {
      setDumpBusy(false);
    }
  }, [loadDumpDocument]);

  const toggleDumpRecording = useCallback(async () => {
    const command = dumpRecording?.isRecording ? 'stop' : 'start';
    setDumpBusy(true);
    setError(null);

    try {
      if (command === 'start') {
        clearDumpPlayback();
      }

      const result = await postDumpRecording({ command });
      setDumpRecording(result.state);

      if (!result.succeeded) {
        setError(result.message);
        return;
      }

      if (command === 'stop') {
        if (!result.dump) {
          setError('Dump recording finished without a dump document.');
          return;
        }

        loadDumpDocument(result.dump);
      } else {
        await refresh(undefined, {
          clearEntityDetails: true,
        });
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught));
    } finally {
      setDumpBusy(false);
    }
  }, [clearDumpPlayback, dumpRecording?.isRecording, loadDumpDocument, refresh]);

  return (
    <main className="app-shell">
      <input
        ref={dumpFileInputRef}
        className="hidden-file-input"
        type="file"
        accept=".json,.nkgdump,.nkgdump.json,application/json"
        onChange={(event) => void handleDumpFileChange(event)}
      />
      <header className="topbar">
        <div className="brand">
          <Bug size={22} aria-hidden />
          <div>
            <h1>NKG Debug Inspector</h1>
            <p>{snapshot ? formatCapturedAt(snapshot.capturedAt) : 'Waiting for snapshot'}</p>
          </div>
        </div>
        <div className="toolbar">
          <label className="search-field">
            <Search size={16} aria-hidden />
            <input
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Filter entities"
            />
          </label>
          <button
            className="icon-button"
            type="button"
            onClick={openDumpFilePicker}
            disabled={dumpBusy}
            title="Load dump file"
          >
            <Upload size={17} />
            Load Dump
          </button>
          <button
            className={dumpRecording?.isRecording ? 'icon-button active recording' : 'icon-button'}
            type="button"
            onClick={() => void toggleDumpRecording()}
            disabled={dumpBusy}
            title={dumpRecording?.isRecording ? 'Stop dump recording' : 'Start dump recording'}
          >
            {dumpRecording?.isRecording ? <Square size={16} /> : <Circle size={16} />}
            {dumpRecording?.isRecording ? 'Stop Rec' : 'Record'}
          </button>
          {dumpMode ? (
            <button
              className="icon-button"
              type="button"
              onClick={returnToLive}
              title="Return to live host"
            >
              <X size={17} />
              Live
            </button>
          ) : null}
          <button
            className={dumpPlaying ? 'icon-button active' : 'icon-button'}
            type="button"
            onClick={() => void executeControl('play')}
            title="Play"
          >
            <Play size={17} />
            Play
          </button>
          <button
            className="icon-button"
            type="button"
            onClick={() => void executeControl('pause')}
            title="Pause"
          >
            <Pause size={17} />
            Pause
          </button>
          <button
            className="icon-button"
            type="button"
            onClick={() => void executeControl('step')}
            title="Step frame"
          >
            <SkipForward size={17} />
            Step
            {!dumpMode && control?.pendingStepCount ? <b>{control.pendingStepCount}</b> : null}
          </button>
          <button
            className="primary-button"
            type="button"
            onClick={() => void refresh()}
            disabled={dumpMode}
          >
            <RefreshCw size={17} className={isRefreshing ? 'spin' : undefined} />
            Refresh
          </button>
          <button
            className={frameStream ? 'icon-button active' : 'icon-button'}
            type="button"
            onClick={() => setFrameStream((current) => !current)}
            disabled={dumpMode}
            title={frameStream ? 'Disconnect frame stream' : 'Subscribe to host frame stream'}
          >
            <Activity size={17} />
            Frame
          </button>
          <button className="icon-button" type="button" onClick={resetDockLayout} title="Reset layout">
            <Boxes size={17} />
            Layout
          </button>
        </div>
      </header>

      <DumpTimeline
        dump={dump}
        currentIndex={dumpFrameIndex}
        isPlaying={dumpPlaying}
        onSelectFrame={selectDumpFrame}
      />

      {error ? <div className="error-banner">{error}</div> : null}

      <DockWorkspace key={dockRevision} model={dockModel} />
    </main>
  );
}

function DockWorkspace({ model }: { model: DockWorkspaceModel }) {
  const [api, setApi] = useState<DockviewApi | null>(null);

  const handleReady = useCallback((event: DockviewReadyEvent) => {
    setApi(event.api);

    if (!restoreDockLayout(event.api)) {
      createDefaultDockLayout(event.api);
      window.setTimeout(() => {
        applyDefaultDockSizing(event.api);
        saveDockLayout(event.api);
      }, 0);
    }

    normalizeDockPanelTitles(event.api);
    saveDockLayout(event.api);
  }, []);

  useEffect(() => {
    if (!api) {
      return;
    }

    const disposable = api.onDidLayoutChange(() => saveDockLayout(api));
    return () => disposable.dispose();
  }, [api]);

  return (
    <DockWorkspaceContext.Provider value={model}>
      <section className="dock-workspace">
        <DockviewReact
          className="dockview-theme-va-night"
          components={dockPanelComponents}
          onReady={handleReady}
          disableFloatingGroups={false}
        />
      </section>
    </DockWorkspaceContext.Provider>
  );
}

function DumpTimeline({
  dump,
  currentIndex,
  isPlaying,
  onSelectFrame,
}: {
  dump: GameDebugDumpDocument | null;
  currentIndex: number;
  isPlaying: boolean;
  onSelectFrame: (frameIndex: number) => void;
}) {
  const frames = dump?.frames ?? [];
  const current = frames[currentIndex] ?? null;
  const disabled = frames.length === 0;
  const max = Math.max(0, frames.length - 1);

  return (
    <section className={disabled ? 'dump-timeline disabled' : 'dump-timeline'}>
      <div className="dump-timeline-summary">
        <span>{dump?.name ?? 'Dump Timeline'}</span>
        <strong>{disabled ? 'No dump' : `${currentIndex + 1} / ${frames.length}`}</strong>
      </div>
      <input
        type="range"
        min={0}
        max={max}
        step={1}
        value={disabled ? 0 : currentIndex}
        disabled={disabled}
        onChange={(event) => onSelectFrame(Number(event.target.value))}
      />
      <div className="dump-timeline-frame">
        <span>{current ? `Frame ${current.frame.frame}` : 'Frame -'}</span>
        <span>{current ? current.frame.source : 'Idle'}</span>
        <span>{isPlaying ? 'Playing' : current ? formatCapturedAt(current.snapshot.capturedAt) : 'Paused'}</span>
      </div>
    </section>
  );
}

function ScenesDockPanel(_props: DockPanelProps) {
  const model = useDockModel();
  const [sceneQuery, setSceneQuery] = useState('');
  const filteredScenes = useMemo(
    () => filterScenes(model.scenes, sceneQuery),
    [model.scenes, sceneQuery],
  );

  return (
    <DockPanelBody className="scenes-dock-panel">
      <SnapshotStats totals={model.totals} />

      <PanelTitle icon={<Boxes size={16} />} title="Scenes" />
      <PanelSearch value={sceneQuery} onChange={setSceneQuery} placeholder="Search scenes" />
      <div className="scene-list">
        {filteredScenes.map((entry) => (
          <button
            key={`${entry.world.name}:${entry.scene.name}`}
            className={model.activeSceneEntry?.scene === entry.scene ? 'scene-item active' : 'scene-item'}
            type="button"
            onClick={() => model.selectScene(entry)}
          >
            <span>{entry.scene.name}</span>
            <small>{entry.world.name}</small>
            <strong>{entry.scene.entityCount}</strong>
          </button>
        ))}
      </div>
    </DockPanelBody>
  );
}

function EntitiesDockPanel(_props: DockPanelProps) {
  const model = useDockModel();
  const [entityQuery, setEntityQuery] = useState('');
  const entities = useMemo(
    () => filterEntities(model.filteredEntities, entityQuery),
    [model.filteredEntities, entityQuery],
  );

  return (
    <DockPanelBody className="entities-dock-panel">
      <div className="pane-head">
        <div>
          <h2>{model.activeScene?.name ?? 'No scene'}</h2>
          <p>{entities.length} / {model.activeScene?.entityCount ?? 0} entities</p>
        </div>
        <PanelSearch value={entityQuery} onChange={setEntityQuery} placeholder="Search entities" />
      </div>

      <EntityTable
        entities={entities}
        selectedEntity={model.selectedEntity}
        onSelectEntity={model.selectEntity}
      />
    </DockPanelBody>
  );
}

function ComponentsDockPanel(_props: DockPanelProps) {
  const model = useDockModel();
  const [componentQuery, setComponentQuery] = useState('');

  return (
    <DockPanelBody className="components-dock-panel">
      {model.selectedEntity ? (
        <EntityDetails
          entity={model.selectedEntity}
          isLoading={model.selectedEntityLoading}
          componentQuery={componentQuery}
          onComponentQueryChange={setComponentQuery}
          onSaveComponent={model.onSaveComponent}
          readOnly={model.isDumpMode}
        />
      ) : (
        <EmptyDetails />
      )}
    </DockPanelBody>
  );
}

function RuntimeDockPanel(_props: DockPanelProps) {
  const model = useDockModel();
  const [runtimeQuery, setRuntimeQuery] = useState('');

  return (
    <DockPanelBody className="runtime-dock-panel">
      <PanelSearch value={runtimeQuery} onChange={setRuntimeQuery} placeholder="Search runtime" />
      <RuntimeDashboard runtimes={model.snapshot?.runtimes ?? []} query={runtimeQuery} />
    </DockPanelBody>
  );
}

function DiagnosticsDockPanel(_props: DockPanelProps) {
  const model = useDockModel();
  const [diagnosticQuery, setDiagnosticQuery] = useState('');

  return (
    <DockPanelBody className="diagnostics-dock-panel">
      <PanelSearch value={diagnosticQuery} onChange={setDiagnosticQuery} placeholder="Search diagnostics" />
      <SceneDiagnostics scene={model.activeScene} query={diagnosticQuery} />
    </DockPanelBody>
  );
}

function DockPanelBody({
  className,
  children,
}: {
  className?: string;
  children: React.ReactNode;
}) {
  return <div className={className ? `dock-panel-body ${className}` : 'dock-panel-body'}>{children}</div>;
}

function PanelSearch({
  value,
  onChange,
  placeholder,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder: string;
}) {
  return (
    <label className="panel-search">
      <Search size={13} aria-hidden />
      <input
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        spellCheck={false}
      />
    </label>
  );
}

function EntityTable({
  entities,
  selectedEntity,
  onSelectEntity,
}: {
  entities: EntityDebugSnapshot[];
  selectedEntity: EntityDebugSnapshot | null;
  onSelectEntity: (entityId: number) => void;
}) {
  return (
    <div className="entity-table" role="table">
      <div className="entity-row head" role="row">
        <span>Id</span>
        <span>Components</span>
        <span>Groups</span>
        <span>Version</span>
      </div>
      {entities.map((entity) => (
        <button
          key={`${entity.id}:${entity.version}`}
          className={entity.id === selectedEntity?.id ? 'entity-row active' : 'entity-row'}
          type="button"
          role="row"
          onClick={() => onSelectEntity(entity.id)}
        >
          <span>#{entity.id}</span>
          <span>{entity.components.map((component) => component.type.name).join(', ')}</span>
          <span>{countComponentGroups(entity.components)}</span>
          <span>{entity.version}</span>
        </button>
      ))}
    </div>
  );
}

function useDockModel() {
  const model = useContext(DockWorkspaceContext);
  if (!model) {
    throw new Error('Dock workspace model is not available.');
  }

  return model;
}

function createDefaultDockLayout(api: DockviewApi) {
  const componentsPanel = api.addPanel({
    id: 'components',
    title: 'Components',
    component: 'components',
    initialWidth: 980,
    initialHeight: 520,
  });

  api.addPanel({
    id: 'scenes',
    title: 'Scenes',
    component: 'scenes',
    initialWidth: 280,
    position: {
      referencePanel: 'components',
      direction: 'left',
    },
  });

  const entitiesPanel = api.addPanel({
    id: 'entities',
    title: 'Entities',
    component: 'entities',
    position: {
      referencePanel: 'scenes',
      direction: 'within',
    },
  });

  api.addPanel({
    id: 'runtime',
    title: 'Runtime',
    component: 'runtime',
    inactive: true,
    position: {
      referencePanel: 'scenes',
      direction: 'within',
    },
  });

  api.addPanel({
    id: 'diagnostics',
    title: 'Diagnostics',
    component: 'diagnostics',
    initialHeight: 420,
    position: {
      referencePanel: 'scenes',
      direction: 'below',
    },
  });

  entitiesPanel.api.setActive();
  componentsPanel.api.setActive();
}

function applyDefaultDockSizing(api: DockviewApi) {
  const navigationWidth = Math.min(540, Math.max(420, Math.round(api.width * 0.27)));
  const diagnosticsHeight = Math.min(520, Math.max(260, Math.round(api.height * 0.42)));
  const mainWidth = Math.max(520, api.width - navigationWidth);

  api.getPanel('scenes')?.api.setSize({ width: navigationWidth });
  api.getPanel('entities')?.api.setSize({ width: navigationWidth });
  api.getPanel('runtime')?.api.setSize({ width: navigationWidth });
  api.getPanel('diagnostics')?.api.setSize({ width: navigationWidth, height: diagnosticsHeight });
  api.getPanel('components')?.api.setSize({ width: mainWidth });
}

function normalizeDockPanelTitles(api: DockviewApi) {
  for (const [panelId, title] of Object.entries(DOCK_PANEL_TITLES)) {
    api.getPanel(panelId)?.api.setTitle(title);
  }
}

function restoreDockLayout(api: DockviewApi) {
  const serialized = readStoredDockLayout();
  if (!serialized) {
    return false;
  }

  try {
    api.fromJSON(serialized);
    return api.totalPanels > 0;
  } catch {
    return false;
  }
}

function readStoredDockLayout(): SerializedDockview | null {
  try {
    const serialized = window.localStorage.getItem(DOCK_LAYOUT_STORAGE_KEY);
    return serialized ? (JSON.parse(serialized) as SerializedDockview) : null;
  } catch {
    return null;
  }
}

function saveDockLayout(api: DockviewApi) {
  try {
    window.localStorage.setItem(DOCK_LAYOUT_STORAGE_KEY, JSON.stringify(api.toJSON()));
  } catch {
    // Layout persistence is best effort.
  }
}

function SnapshotStats({ totals }: { totals: DebugTotals }) {
  return (
    <div className="snapshot-stats">
      <StatPill label="Worlds" value={totals.worlds} />
      <StatPill label="Scenes" value={totals.scenes} />
      <StatPill label="Entities" value={totals.entities} />
      <StatPill label="Modules" value={totals.modules} />
      <StatPill label="Systems" value={totals.systems} />
      <StatPill label="Procedures" value={totals.procedures} />
    </div>
  );
}

function StatPill({ label, value }: { label: string; value: number }) {
  return (
    <div className="stat-pill">
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  );
}

function PanelTitle({ icon, title }: { icon: React.ReactNode; title: string }) {
  return (
    <div className="panel-title">
      {icon}
      <span>{title}</span>
    </div>
  );
}

function RuntimeDashboard({ runtimes, query }: { runtimes: RuntimeContextDebugSnapshot[]; query: string }) {
  return (
    <section className="diagnostic-grid">
      <section className="panel module-panel">
        <PanelTitle icon={<Cpu size={14} />} title="Modules" />
        <ModuleRows runtimes={runtimes} query={query} />
      </section>
      <section className="panel procedure-panel">
        <PanelTitle icon={<Activity size={14} />} title="Procedures" />
        <ProcedureRows runtimes={runtimes} query={query} />
      </section>
    </section>
  );
}

function ModuleRows({ runtimes, query }: { runtimes: RuntimeContextDebugSnapshot[]; query: string }) {
  const rows = runtimes.flatMap((runtime) =>
    runtime.modules.map((module) => ({
      runtime,
      module,
    })),
  ).filter(({ runtime, module }) =>
    matchesQuery(
      [
        runtime.index + 1,
        module.type.name,
        module.type.fullName,
        module.priority,
        module.isUpdateModule ? 'update' : '',
      ],
      query,
    ),
  );

  if (!rows.length) {
    return <p className="muted">No modules</p>;
  }

  return (
    <div className="compact-table module-table">
      <div className="compact-row head">
        <span>Runtime</span>
        <span>Module</span>
        <span>Priority</span>
        <span>Update</span>
      </div>
      {rows.map(({ runtime, module }) => (
        <ModuleRow key={`${runtime.index}:${module.type.fullName}`} runtime={runtime} module={module} />
      ))}
    </div>
  );
}

function ModuleRow({
  runtime,
  module,
}: {
  runtime: RuntimeContextDebugSnapshot;
  module: ModuleDebugSnapshot;
}) {
  return (
    <div className="compact-row">
      <span>#{runtime.index + 1}</span>
      <span title={module.type.fullName}>{module.type.name}</span>
      <span>{module.priority}</span>
      <span>{module.isUpdateModule ? 'Yes' : 'No'}</span>
    </div>
  );
}

function ProcedureRows({ runtimes, query }: { runtimes: RuntimeContextDebugSnapshot[]; query: string }) {
  const rows = runtimes.flatMap((runtime) =>
    runtime.procedureModules.map((module) => ({
      runtime,
      module,
    })),
  ).filter(({ runtime, module }) =>
    matchesQuery(
      [
        runtime.index + 1,
        module.type.name,
        module.type.fullName,
        module.currentProcedure ?? '',
        module.isInitialized ? 'initialized' : '',
        ...module.procedures.flatMap((procedure) => [
          procedure.type.name,
          procedure.type.fullName,
          procedure.isCurrent ? 'current' : '',
        ]),
      ],
      query,
    ),
  );

  if (!rows.length) {
    return <p className="muted">No procedure modules</p>;
  }

  return (
    <div className="compact-table procedure-table">
      <div className="compact-row head">
        <span>Runtime</span>
        <span>Module</span>
        <span>Current</span>
        <span>Time</span>
      </div>
      {rows.map(({ runtime, module }) => (
        <ProcedureRow key={`${runtime.index}:${module.type.fullName}`} runtime={runtime} module={module} />
      ))}
    </div>
  );
}

function ProcedureRow({
  runtime,
  module,
}: {
  runtime: RuntimeContextDebugSnapshot;
  module: ProcedureModuleDebugSnapshot;
}) {
  return (
    <div className="compact-row procedure-row">
      <span>#{runtime.index + 1}</span>
      <span title={module.type.fullName}>{module.type.name}</span>
      <span>{module.currentProcedure ?? 'Idle'}</span>
      <span>{formatSeconds(module.currentProcedureTime)}</span>
      {module.procedures.length ? (
        <div className="procedure-chips">
          {module.procedures.map((procedure) => (
            <span
              key={procedure.type.fullName}
              className={procedure.isCurrent ? 'status-chip active' : 'status-chip'}
              title={procedure.type.fullName}
            >
              {procedure.type.name}
            </span>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function SceneDiagnostics({ scene, query }: { scene: SceneDebugSnapshot | null; query: string }) {
  return (
    <section className="diagnostic-grid">
      <section className="panel systems-panel">
        <PanelTitle icon={<Activity size={14} />} title="Systems" />
        <SystemRows systems={scene?.systems ?? []} query={query} />
      </section>
      <section className="panel stores-panel">
        <PanelTitle icon={<Sparkles size={14} />} title="ComponentStores" />
        <ComponentStoreRows stores={scene?.componentStores ?? []} query={query} />
      </section>
    </section>
  );
}

function SystemRows({ systems, query }: { systems: SystemDebugSnapshot[]; query: string }) {
  const rows = systems.filter((system) =>
    matchesQuery(
      [system.order, system.type.name, system.type.fullName, system.enabled ? 'enabled' : 'disabled'],
      query,
    ),
  );

  if (!rows.length) {
    return <p className="muted">No systems</p>;
  }

  return (
    <div className="compact-table system-table">
      <div className="compact-row head">
        <span>Order</span>
        <span>System</span>
        <span>Enabled</span>
      </div>
      {rows.map((system) => (
        <div key={system.type.fullName} className="compact-row">
          <span>{system.order}</span>
          <span title={system.type.fullName}>{system.type.name}</span>
          <span>{system.enabled ? 'Yes' : 'No'}</span>
        </div>
      ))}
    </div>
  );
}

function ComponentStoreRows({ stores, query }: { stores: ComponentStoreDebugSnapshot[]; query: string }) {
  const rows = stores.filter((store) =>
    matchesQuery([store.type.name, store.type.fullName, store.count, ...store.entityIds], query),
  );

  if (!rows.length) {
    return <p className="muted">No component stores</p>;
  }

  return (
    <div className="compact-table store-table">
      <div className="compact-row head">
        <span>Component</span>
        <span>Count</span>
        <span>Entities</span>
      </div>
      {rows.map((store) => (
        <div key={store.type.fullName} className="compact-row">
          <span title={store.type.fullName}>{store.type.name}</span>
          <span>{store.count}</span>
          <span>{store.entityIds.join(', ')}</span>
        </div>
      ))}
    </div>
  );
}

function EntityDetails({
  entity,
  isLoading,
  componentQuery,
  onComponentQueryChange,
  onSaveComponent,
  readOnly,
}: {
  entity: EntityDebugSnapshot;
  isLoading: boolean;
  componentQuery: string;
  onComponentQueryChange: (value: string) => void;
  onSaveComponent: ComponentMutationExecutor;
  readOnly: boolean;
}) {
  return (
    <div className="details component-details">
      <div className="detail-heading">
        <div>
          <h2>Entity #{entity.id}</h2>
          <p>Version {entity.version} · {entity.components.length} components</p>
        </div>
        <PanelSearch
          value={componentQuery}
          onChange={onComponentQueryChange}
          placeholder="Search components"
        />
      </div>

      <div className="component-detail-surface">
        {isLoading ? (
          <div className="component-flow">
            <div className="component-flow-empty">Loading components</div>
          </div>
        ) : entity.components.length ? (
          <Suspense
            fallback={
              <div className="component-flow">
                <div className="component-flow-empty">Loading components</div>
              </div>
            }
          >
            <ComponentGraphCanvas
              entity={entity}
              query={componentQuery}
              onSaveComponent={onSaveComponent}
              readOnly={readOnly}
            />
          </Suspense>
        ) : (
          <p className="muted">None</p>
        )}
      </div>
    </div>
  );
}

function EmptyDetails() {
  return (
    <div className="empty-details">
      <Cpu size={28} />
      <span>No entity</span>
    </div>
  );
}

function validateDumpDocument(dump: GameDebugDumpDocument) {
  if (!dump || dump.format !== 'nkg.debug.dump' || dump.version !== 1) {
    throw new Error('Unsupported debug dump file.');
  }

  if (!Array.isArray(dump.frames) || dump.frames.length === 0) {
    throw new Error('Debug dump file does not contain frames.');
  }
}

function readStoredFrameStream() {
  try {
    return window.localStorage.getItem(FRAME_STREAM_STORAGE_KEY) === 'true' ||
      window.localStorage.getItem(LEGACY_FRAME_POLLING_STORAGE_KEY) === 'true' ||
      window.localStorage.getItem(LEGACY_AUTO_REFRESH_STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

function writeStoredFrameStream(value: boolean) {
  try {
    window.localStorage.setItem(FRAME_STREAM_STORAGE_KEY, String(value));
    window.localStorage.removeItem(LEGACY_FRAME_POLLING_STORAGE_KEY);
    window.localStorage.removeItem(LEGACY_AUTO_REFRESH_STORAGE_KEY);
  } catch {
    // Frame stream preference is best-effort.
  }
}

function flattenScenes(snapshot: GameDebugSnapshot | null): SceneEntry[] {
  if (!snapshot) {
    return [];
  }

  return snapshot.worlds.flatMap((world) =>
    world.scenes.map((scene) => ({
      world,
      scene,
    })),
  );
}

function findActiveSceneEntry(
  scenes: SceneEntry[],
  selection: SceneSelection | null,
) {
  if (!selection) {
    return null;
  }

  return (
    scenes.find(({ world, scene }) => world.name === selection.worldName && scene.name === selection.sceneName) ??
    null
  );
}

function createEntityDetailKey(worldName: string, sceneName: string, entityId: number) {
  return `${worldName}\u0000${sceneName}\u0000${entityId}`;
}

function findEntityDetail(
  snapshot: GameDebugSnapshot,
  worldName: string,
  sceneName: string,
  entityId: number,
) {
  const world = snapshot.worlds.find((candidate) => candidate.name === worldName);
  const scene = world?.scenes.find((candidate) => candidate.name === sceneName);
  return scene?.entities.find((candidate) => candidate.id === entityId) ?? null;
}

function hasComponentValueDetails(entity: EntityDebugSnapshot) {
  if (!entity.components.length) {
    return true;
  }

  return entity.components.every((component) =>
    component.value.payload !== null ||
    component.value.structured !== null ||
    component.value.error !== null,
  );
}

function filterScenes(scenes: SceneEntry[], query: string) {
  return scenes.filter(({ world, scene }) =>
    matchesQuery([world.name, scene.name, scene.entityCount, scene.systems.length, scene.componentStores.length], query),
  );
}

function filterEntities(entities: EntityDebugSnapshot[], query: string) {
  return entities.filter((entity) =>
    matchesQuery(
      [
        `#${entity.id}`,
        String(entity.id),
        ...entity.components.map((component) => component.type.name),
        ...entity.skills.flatMap((skill) => [skill.id, skill.displayName ?? '', ...skill.tags]),
        ...entity.buffs.flatMap((buff) => [buff.id, buff.displayName ?? '', ...buff.tags]),
      ],
      query,
    ),
  );
}

function matchesQuery(parts: unknown[], query: string) {
  const normalized = query.trim().toLowerCase();
  if (!normalized) {
    return true;
  }

  return parts
    .filter((part) => part !== null && part !== undefined)
    .join(' ')
    .toLowerCase()
    .includes(normalized);
}

function summarize(snapshot: GameDebugSnapshot | null) {
  const worlds = snapshot?.worlds.length ?? 0;
  const modules = snapshot?.runtimes.reduce((sum, runtime) => sum + runtime.modules.length, 0) ?? 0;
  const procedures =
    snapshot?.runtimes.reduce(
      (sum, runtime) =>
        sum + runtime.procedureModules.reduce((procedureSum, module) => procedureSum + module.procedures.length, 0),
      0,
    ) ?? 0;
  let scenes = 0;
  let entities = 0;
  let systems = 0;

  for (const world of snapshot?.worlds ?? []) {
    scenes += world.scenes.length;
    for (const scene of world.scenes) {
      entities += scene.entities.length;
      systems += scene.systems.length;
    }
  }

  return { worlds, scenes, entities, modules, systems, procedures };
}

function formatCapturedAt(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(value));
}

function formatSeconds(value: number) {
  if (value <= 0) {
    return '0s';
  }

  return `${value.toFixed(value < 10 ? 1 : 0)}s`;
}
