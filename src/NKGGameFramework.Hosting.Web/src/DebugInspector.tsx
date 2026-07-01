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
  Lock,
  Pause,
  Play,
  RefreshCw,
  Search,
  SkipForward,
  Sparkles,
  Square,
  Unlock,
  Upload,
  X,
} from 'lucide-react';
import {
  createDebugSnapshotStream,
  createDebugApiBaseUrl,
  DEFAULT_DEBUG_API_CONNECTION,
  fetchDumpRecordingState,
  fetchDebugSnapshotMessage,
  fetchDumpPlaybackComponent,
  fetchDumpPlaybackFrame,
  openDumpPlayback,
  postDebugControl,
  postDumpRecording,
  postDebugMutation,
  setDebugApiBaseUrl,
  uploadDumpAnalysis,
  uploadDumpPlayback,
  type DebugApiConnection,
} from './api';
import { countComponentGroups, getComponentGraph, type ComponentMutationExecutor } from './componentGraphModel';
import type {
  ComponentDebugSnapshot,
  ComponentStoreDebugSnapshot,
  ComponentValueDebugNode,
  DebugTypeInfo,
  EntityDebugSnapshot,
  GameDebugControlCommand,
  GameDebugControlState,
  GameDebugDumpAnalysisEntry,
  GameDebugDumpAnalysisReport,
  GameDebugDumpPlaybackFrame,
  GameDebugDumpPlaybackManifest,
  GameDebugDumpRecordingMetrics,
  GameDebugDumpRecordingState,
  GameDebugFrameInfo,
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
  selectedComponentKey: string | null;
  activeInspectorPanelId: string | null;
  componentInspectors: ComponentInspectorPanelState[];
  componentDetails: Record<string, ComponentDetailEntry>;
  dumpAnalysis: GameDebugDumpAnalysisReport | null;
  dumpAnalysisBusy: boolean;
  isDumpAnalysisPanelOpen: boolean;
  isDumpMode: boolean;
  selectScene: (entry: SceneEntry) => void;
  selectEntity: (entityId: number) => void;
  inspectComponent: (entity: EntityDebugSnapshot, component: ComponentDebugSnapshot) => void;
  toggleInspectorLock: (panelId: string) => void;
  closeInspectorPanel: (panelId: string) => void;
  closeDumpAnalysisPanel: () => void;
  openDumpAnalysisFilePicker: () => void;
  reloadComponentDetail: (target: ComponentInspectorTarget) => void;
  onSaveComponent: ComponentMutationExecutor;
};

type ComponentInspectorTarget = {
  worldName: string;
  sceneName: string;
  entityId: number;
  entityVersion: number;
  componentTypeName: string;
  componentTypeFullName: string;
  componentAssemblyName: string;
  graphId: string;
};

type ComponentInspectorPanelState = {
  id: string;
  locked: boolean;
  target: ComponentInspectorTarget | null;
};

type ComponentDetailEntry =
  | {
      status: 'loading';
      entity?: EntityDebugSnapshot;
      component?: ComponentDebugSnapshot;
      dumpKey?: string;
    }
  | {
      status: 'ready';
      entity: EntityDebugSnapshot;
      component: ComponentDebugSnapshot;
      dumpKey?: string;
    }
  | {
      status: 'error';
      message: string;
      entity?: EntityDebugSnapshot;
      component?: ComponentDebugSnapshot;
      dumpKey?: string;
    };

type TimelineFrameSample = {
  index: number;
  frameNumber: number;
  fps: number | null;
  milliseconds: number | null;
  durationWeight: number;
  heightPercent: number;
  color: string;
  label: string;
};

type TimelineChartPoint = {
  index: number;
  frameNumber: number;
  fps: number | null;
  milliseconds: number | null;
  heightPercent: number;
  xPercent: number;
  color: string;
  label: string;
};

type TimelineTick = {
  index: number;
  leftPercent: number;
  label: string;
};

type TimelineFpsTick = {
  fps: number;
  trackBottomPercent: number;
  axisBottomPercent: number;
  label: string;
};

type ToastMessage = {
  title: string;
  body: string;
};

type FilePickerFileHandle = {
  getFile: () => Promise<File>;
};

type FilePickerWindow = Window & {
  showOpenFilePicker?: (options?: {
    id?: string;
    multiple?: boolean;
    excludeAcceptAllOption?: boolean;
    types?: Array<{
      description?: string;
      accept: Record<string, string[]>;
    }>;
  }) => Promise<FilePickerFileHandle[]>;
};

type DockPanelParams = {
  inspectorId?: string;
};

type DockPanelProps = IDockviewPanelProps<DockPanelParams>;

const DOCK_LAYOUT_STORAGE_KEY = 'nkg.webdebug.layout.v3';
const LEGACY_DOCK_LAYOUT_STORAGE_KEYS = ['nkg.webdebug.layout.v1', 'nkg.webdebug.layout.v2'];
const DEBUG_API_CONNECTION_STORAGE_KEY = 'nkg.webdebug.connection.v1';
const FRAME_STREAM_STORAGE_KEY = 'nkg.webdebug.frameStream';
const LEGACY_FRAME_POLLING_STORAGE_KEY = 'nkg.webdebug.framePolling';
const LEGACY_AUTO_REFRESH_STORAGE_KEY = 'nkg.webdebug.autoRefresh';
const STEP_SNAPSHOT_TIMEOUT_MS = 5000;
const STEP_SNAPSHOT_POLL_INTERVAL_MS = 50;
const DUMP_FINALIZE_TIMEOUT_MS = 120000;
const DUMP_FINALIZE_POLL_INTERVAL_MS = 250;
const TIMELINE_MIN_ZOOM = 1;
const TIMELINE_MAX_ZOOM = 16;
const TIMELINE_ZOOM_STEP = 0.5;
const TIMELINE_DEFAULT_MAX_FPS = 120;
const TIMELINE_MAX_MAX_FPS = 2000;
const TIMELINE_MIN_RENDERED_POINTS = 256;
const TIMELINE_MAX_RENDERED_POINTS = 4096;
const TIMELINE_RENDER_OVERSAMPLE = 1.25;
const TIMELINE_TRACK_HEIGHT_PX = 104;
const TIMELINE_CONTENT_HEIGHT_PX = 128;
const DUMP_FILE_PICKER_ID = 'nkg-debug-dumps';
const DUMP_ANALYSIS_FILE_PICKER_ID = 'nkg-debug-dump-analysis';
const DUMP_ANALYSIS_PANEL_ID = 'dump-analysis';
const SUPPORTED_DUMP_DOCUMENT_VERSIONS = new Set([4]);
const WEB_DUMP_DIRECTORY =
  typeof __NKG_WEB_DUMP_DIRECTORY__ === 'string' && __NKG_WEB_DUMP_DIRECTORY__.trim()
    ? __NKG_WEB_DUMP_DIRECTORY__
    : null;
const DOCK_PANEL_TITLES: Record<string, string> = {
  components: 'Components',
  inspector: 'Inspector',
  scenes: 'Scenes',
  entities: 'Entities',
  runtime: 'Runtime',
  diagnostics: 'Diagnostics',
  [DUMP_ANALYSIS_PANEL_ID]: 'Dump Report',
};

const DEFAULT_INSPECTOR_PANEL_ID = 'inspector';

const ComponentGraphCanvas = lazy(() =>
  import('./ComponentGraphCanvas').then((module) => ({ default: module.ComponentGraphCanvas })),
);

const ComponentInspector = lazy(() =>
  import('./ComponentGraphCanvas').then((module) => ({ default: module.ComponentInspector })),
);

const dockPanelComponents = {
  scenes: ScenesDockPanel,
  entities: EntitiesDockPanel,
  components: ComponentsDockPanel,
  inspector: ComponentInspectorDockPanel,
  runtime: RuntimeDockPanel,
  diagnostics: DiagnosticsDockPanel,
  dumpAnalysis: DumpAnalysisDockPanel,
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
  const [componentDetails, setComponentDetails] = useState<Record<string, ComponentDetailEntry>>({});
  const [componentInspectors, setComponentInspectors] = useState<ComponentInspectorPanelState[]>(() => [
    createEmptyInspectorPanel(),
  ]);
  const [activeInspectorPanelId, setActiveInspectorPanelId] = useState<string | null>(DEFAULT_INSPECTOR_PANEL_ID);
  const [dockRevision, setDockRevision] = useState(0);
  const [frameStream, setFrameStream] = useState(readStoredFrameStream);
  const [debugApiConnection, setDebugApiConnection] = useState<DebugApiConnection>(readStoredDebugApiConnection);
  const [debugApiDraft, setDebugApiDraft] = useState<DebugApiConnection>(debugApiConnection);
  const [dump, setDump] = useState<GameDebugDumpPlaybackManifest | null>(null);
  const [dumpFrameIndex, setDumpFrameIndex] = useState(0);
  const [dumpPlaying, setDumpPlaying] = useState(false);
  const [dumpRecording, setDumpRecording] = useState<GameDebugDumpRecordingState | null>(null);
  const [dumpBusy, setDumpBusy] = useState(false);
  const [dumpAnalysis, setDumpAnalysis] = useState<GameDebugDumpAnalysisReport | null>(null);
  const [dumpAnalysisBusy, setDumpAnalysisBusy] = useState(false);
  const [dumpAnalysisPanelOpen, setDumpAnalysisPanelOpen] = useState(false);
  const [toast, setToast] = useState<ToastMessage | null>(null);
  const refreshInFlightRef = useRef(false);
  const componentDetailLoadKeysRef = useRef<Set<string>>(new Set());
  const activeSceneEntryRef = useRef<SceneEntry | null>(null);
  const snapshotRef = useRef<GameDebugSnapshot | null>(null);
  const dumpRef = useRef<GameDebugDumpPlaybackManifest | null>(null);
  const dumpFrameIndexRef = useRef(0);
  const componentInspectorsRef = useRef<ComponentInspectorPanelState[]>([]);
  const componentDetailsRef = useRef<Record<string, ComponentDetailEntry>>({});
  const dumpFileInputRef = useRef<HTMLInputElement | null>(null);
  const dumpAnalysisFileInputRef = useRef<HTMLInputElement | null>(null);
  const dumpModeRef = useRef(false);
  const toastTimeoutRef = useRef<number | null>(null);
  const dumpDetailAbortRef = useRef<AbortController | null>(null);
  const refreshRevisionRef = useRef(0);
  const nextInspectorIdRef = useRef(1);
  const dumpFrames = dump?.frames ?? [];
  const dumpMode = dumpFrames.length > 0;
  const debugApiBaseUrl = useMemo(
    () => createDebugApiBaseUrl(debugApiConnection),
    [debugApiConnection],
  );

  const showToast = useCallback((title: string, body: string) => {
    if (toastTimeoutRef.current !== null) {
      window.clearTimeout(toastTimeoutRef.current);
    }

    setToast({ title, body });
    toastTimeoutRef.current = window.setTimeout(() => {
      setToast(null);
      toastTimeoutRef.current = null;
    }, 3000);
  }, []);

  useEffect(() => {
    return () => {
      if (toastTimeoutRef.current !== null) {
        window.clearTimeout(toastTimeoutRef.current);
      }
      dumpDetailAbortRef.current?.abort();
    };
  }, []);

  useEffect(() => {
    setDebugApiBaseUrl(debugApiBaseUrl);
  }, [debugApiBaseUrl]);

  const commitSnapshotMessage = useCallback((
    message: GameDebugSnapshotMessage,
    options: { clearComponentDetails?: boolean } = {},
  ) => {
    setError(null);
    snapshotRef.current = message.snapshot;
    setSnapshot(message.snapshot);
    setControl(message.control);
    if (options.clearComponentDetails) {
      setComponentDetails({});
    }
    setLoadState('ready');
  }, []);

  const loadDumpPlayback = useCallback((nextDump: GameDebugDumpPlaybackManifest) => {
    validateDumpPlaybackManifest(nextDump);
    dumpModeRef.current = true;
    setDump(nextDump);
    setDumpFrameIndex(0);
    setDumpPlaying(false);
    setFrameStream(false);
    setComponentDetails({});
    setLoadState('loading');
  }, []);

  const refresh = useCallback(async (
    signal?: AbortSignal,
    options: { clearComponentDetails?: boolean; waitForFrame?: boolean } = {},
  ) => {
    if (refreshInFlightRef.current) {
      return;
    }

    refreshInFlightRef.current = true;
    const refreshRevision = refreshRevisionRef.current;
    setIsRefreshing(true);
    setLoadState((current) => (current === 'ready' ? current : 'loading'));
    setError(null);

    try {
      const next = await fetchDebugSnapshotMessage(signal, {
        profile: 'singleFramePreview',
        includePayload: false,
        includeStructured: false,
        waitForFrame: options.waitForFrame,
      });
      if (dumpModeRef.current) {
        return;
      }

      if (refreshRevision !== refreshRevisionRef.current) {
        return;
      }

      commitSnapshotMessage(next, {
        clearComponentDetails: options.clearComponentDetails ?? true,
      });
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === 'AbortError') {
        return;
      }

      if (refreshRevision !== refreshRevisionRef.current) {
        return;
      }

      setError(caught instanceof Error ? caught.message : String(caught));
      setLoadState('error');
    } finally {
      if (refreshRevision === refreshRevisionRef.current) {
        refreshInFlightRef.current = false;
        setIsRefreshing(false);
      }
    }
  }, [commitSnapshotMessage, debugApiBaseUrl]);

  const loadComponentDetail = useCallback(async (
    target: ComponentInspectorTarget,
    options: { force?: boolean; silent?: boolean; signal?: AbortSignal } = {},
  ) => {
    const detailKey = createComponentDetailKey(target);
    const dumpRequest = dumpModeRef.current
      ? {
          id: dumpRef.current?.id ?? '',
          frameIndex: dumpFrameIndexRef.current,
        }
      : null;
    const loadKey = dumpRequest
      ? `${detailKey}:dump:${dumpRequest.id}:${dumpRequest.frameIndex}`
      : detailKey;
    const detailDumpKey = dumpRequest
      ? `${dumpRequest.id}:${dumpRequest.frameIndex}`
      : undefined;

    if (componentDetailLoadKeysRef.current.has(loadKey)) {
      return;
    }

    const currentEntry = componentDetailsRef.current[detailKey];
    if (
      !options.force &&
      currentEntry?.status === 'ready' &&
      currentEntry.dumpKey === detailDumpKey
    ) {
      return;
    }

    componentDetailLoadKeysRef.current.add(loadKey);
    if (!options.silent || !currentEntry) {
      setComponentDetails((current) => ({
        ...current,
        [detailKey]: {
          status: 'loading',
          entity: currentEntry?.entity,
          component: currentEntry?.component,
          dumpKey: detailDumpKey,
        },
      }));
    }

    try {
      if (dumpRequest) {
        const currentSnapshot = snapshotRef.current;
        const currentDump = dumpRef.current;
        const entity = currentSnapshot
          ? findEntityDetail(currentSnapshot, target.worldName, target.sceneName, target.entityId)
          : null;
        const snapshotComponent = entity ? findComponentDetail(entity, target) : null;
        if (!entity || !currentDump || !snapshotComponent) {
          throw new Error('Component value was not recorded in this dump frame.');
        }

        let component: ComponentDebugSnapshot;
        try {
          component = await fetchDumpPlaybackComponent({
            playbackId: currentDump.id,
            frameIndex: dumpRequest.frameIndex,
            worldName: target.worldName,
            sceneName: target.sceneName,
            entityId: target.entityId,
            componentTypeFullName: target.componentTypeFullName,
            componentAssemblyName: target.componentAssemblyName,
          }, options.signal);
        } catch (caught) {
          if (!hasRecordedComponentValue(snapshotComponent)) {
            throw caught;
          }

          component = snapshotComponent;
        }

        if (
          !dumpModeRef.current ||
          dumpRef.current?.id !== dumpRequest.id ||
          dumpFrameIndexRef.current !== dumpRequest.frameIndex
        ) {
          return;
        }

        setComponentDetails((current) => ({
          ...current,
          [detailKey]: {
            ...stabilizeComponentDetailEntry(current[detailKey], entity, component, detailDumpKey),
          },
        }));
        return;
      }

      const detailMessage = await fetchDebugSnapshotMessage(options.signal, {
        profile: 'stepEditable',
        worldName: target.worldName,
        sceneName: target.sceneName,
        entityId: target.entityId,
        componentTypeFullName: target.componentTypeFullName,
        componentAssemblyName: target.componentAssemblyName,
        includePayload: true,
        includeStructured: true,
        waitForFrame: false,
      });
      const entity = findEntityDetail(
        detailMessage.snapshot,
        target.worldName,
        target.sceneName,
        target.entityId,
      );
      const component = entity ? findComponentDetail(entity, target) : null;
      if (!entity || !component) {
        throw new Error(`Component '${target.componentTypeName}' was not found on Entity #${target.entityId}.`);
      }

      if (dumpModeRef.current) {
        return;
      }

      setControl(detailMessage.control);
      setComponentDetails((current) => ({
        ...current,
        [detailKey]: {
          ...stabilizeComponentDetailEntry(current[detailKey], entity, component, detailDumpKey),
        },
      }));
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === 'AbortError') {
        return;
      }

      if (
        dumpRequest &&
        (!dumpModeRef.current ||
          dumpRef.current?.id !== dumpRequest.id ||
          dumpFrameIndexRef.current !== dumpRequest.frameIndex)
      ) {
        return;
      }

      const message = caught instanceof Error ? caught.message : String(caught);
      setError(message);
      setComponentDetails((current) => ({
        ...current,
        [detailKey]: {
          status: 'error',
          message,
          entity: current[detailKey]?.entity,
          component: current[detailKey]?.component,
          dumpKey: detailDumpKey,
        },
      }));
    } finally {
      componentDetailLoadKeysRef.current.delete(loadKey);
    }
  }, []);

  const reloadInspectorDetails = useCallback((force = false, silent = false, signal?: AbortSignal) => {
    for (const panel of componentInspectorsRef.current) {
      if (panel.target) {
        void loadComponentDetail(panel.target, { force, silent, signal });
      }
    }
  }, [loadComponentDetail]);

  const reloadDumpInspectorDetails = useCallback((force = true, silent = true) => {
    dumpDetailAbortRef.current?.abort();
    const controller = new AbortController();
    dumpDetailAbortRef.current = controller;
    reloadInspectorDetails(force, silent, controller.signal);
  }, [reloadInspectorDetails]);

  const reloadComponentDetail = useCallback((target: ComponentInspectorTarget) => {
    void loadComponentDetail(target, { force: true });
  }, [loadComponentDetail]);

  const stabilizeComponentDetailEntry = useCallback((
    previousEntry: ComponentDetailEntry | undefined,
    entity: EntityDebugSnapshot,
    component: ComponentDebugSnapshot,
    dumpKey?: string,
  ) => {
    const previousReady = previousEntry?.status === 'ready' ? previousEntry : null;
    return {
      status: 'ready' as const,
      entity: previousReady && areEntitySnapshotsEqual(previousReady.entity, entity)
        ? previousReady.entity
        : entity,
      component: previousReady && areComponentSnapshotsEqual(previousReady.component, component)
        ? previousReady.component
        : component,
      dumpKey,
    };
  }, []);

  const waitForStepSnapshot = useCallback(async (targetRevision: number) => {
    const startedAt = Date.now();

    while (true) {
      const message = await fetchDebugSnapshotMessage(undefined, {
        profile: 'livePreview',
        includePayload: false,
        includeStructured: false,
        waitForFrame: false,
      });

      if (isConsumedStepSnapshot(message, targetRevision)) {
        return message;
      }

      if (Date.now() - startedAt >= STEP_SNAPSHOT_TIMEOUT_MS) {
        throw new Error('Step was queued, but no runtime frame was published before the timeout.');
      }

      await delay(STEP_SNAPSHOT_POLL_INTERVAL_MS);
    }
  }, []);

  const inspectComponent = useCallback((
    entity: EntityDebugSnapshot,
    component: ComponentDebugSnapshot,
  ) => {
    const entry = activeSceneEntryRef.current;
    if (!entry) {
      return;
    }

    const target = createInspectorTarget(entry, entity, component);
    let nextActivePanelId = DEFAULT_INSPECTOR_PANEL_ID;

    setComponentInspectors((current) => {
      const unlocked = current.find((panel) => !panel.locked);
      if (unlocked) {
        nextActivePanelId = unlocked.id;
        return current.map((panel) =>
          panel.id === unlocked.id ? { ...panel, target } : panel,
        );
      }

      const id = createInspectorPanelId(nextInspectorIdRef.current++);
      nextActivePanelId = id;
      return [
        ...current,
        {
          id,
          locked: false,
          target,
        },
      ];
    });
    setActiveInspectorPanelId(nextActivePanelId);
    void loadComponentDetail(target);
  }, [loadComponentDetail]);

  const toggleInspectorLock = useCallback((panelId: string) => {
    setComponentInspectors((current) =>
      current.map((panel) =>
        panel.id === panelId ? { ...panel, locked: !panel.locked } : panel,
      ),
    );
  }, []);

  const closeInspectorPanel = useCallback((panelId: string) => {
    setComponentInspectors((current) => current.filter((panel) => panel.id !== panelId));
    setActiveInspectorPanelId((current) => current === panelId ? null : current);
  }, []);

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
  }, [debugApiBaseUrl]);

  useEffect(() => {
    if (dumpRecording?.isRecording !== true) {
      return;
    }

    const timer = window.setInterval(() => {
      const controller = new AbortController();
      fetchDumpRecordingState(controller.signal)
        .then(setDumpRecording)
        .catch(() => {
          controller.abort();
        });
    }, 1000);

    return () => window.clearInterval(timer);
  }, [debugApiBaseUrl, dumpRecording?.isRecording]);

  useEffect(() => {
    if (!dumpMode || !dump) {
      return;
    }

    const controller = new AbortController();
    fetchDumpPlaybackFrame(dump.id, dumpFrameIndex, controller.signal)
      .then((message) => {
        commitSnapshotMessage(message, {
          clearComponentDetails: dumpFrameIndex === 0,
        });
        reloadDumpInspectorDetails(true, true);
      })
      .catch((caught) => {
        if (caught instanceof DOMException && caught.name === 'AbortError') {
          return;
        }

        setError(caught instanceof Error ? caught.message : String(caught));
        setLoadState('error');
      });

    return () => controller.abort();
  }, [commitSnapshotMessage, debugApiBaseUrl, dump, dumpFrameIndex, dumpMode, reloadDumpInspectorDetails]);

  const scenes = useMemo(() => flattenScenes(snapshot), [snapshot]);
  const activeSceneEntry = useMemo(
    () => findActiveSceneEntry(scenes, selection) ?? scenes[0] ?? null,
    [scenes, selection],
  );
  const activeScene = activeSceneEntry?.scene ?? null;

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
  const selectedEntity = selectedEntityOverview;
  const selectedComponentKey = useMemo(
    () => componentInspectors.find((panel) => !panel.locked && panel.target)?.target?.graphId ??
      componentInspectors.find((panel) => panel.target)?.target?.graphId ??
      null,
    [componentInspectors],
  );

  useEffect(() => {
    activeSceneEntryRef.current = activeSceneEntry;
  }, [activeSceneEntry]);

  useEffect(() => {
    snapshotRef.current = snapshot;
  }, [snapshot]);

  useEffect(() => {
    dumpRef.current = dump;
  }, [dump]);

  useEffect(() => {
    dumpFrameIndexRef.current = dumpFrameIndex;
  }, [dumpFrameIndex]);

  useEffect(() => {
    componentInspectorsRef.current = componentInspectors;
  }, [componentInspectors]);

  useEffect(() => {
    componentDetailsRef.current = componentDetails;
  }, [componentDetails]);

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
      profile: 'livePreview',
      includePayload: false,
      includeStructured: false,
    });
    const handleSnapshot = (event: Event) => {
      const message = JSON.parse((event as MessageEvent<string>).data) as GameDebugSnapshotMessage;
      commitSnapshotMessage(message);
      reloadInspectorDetails(true, true);
    };
    const handleError = () => {
      setError('Debug frame stream disconnected. Reconnecting...');
    };

    stream.addEventListener('snapshot', handleSnapshot);
    stream.addEventListener('error', handleError);

    return () => {
      stream.removeEventListener('snapshot', handleSnapshot);
      stream.removeEventListener('error', handleError);
      stream.close();
    };
  }, [
    commitSnapshotMessage,
    debugApiBaseUrl,
    dumpMode,
    frameStream,
    reloadInspectorDetails,
  ]);

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
    const controller = new AbortController();
    for (const panel of componentInspectors) {
      if (!panel.target) {
        continue;
      }

      const detailKey = createComponentDetailKey(panel.target);
      if (!componentDetails[detailKey]) {
        void loadComponentDetail(panel.target, { signal: controller.signal });
      }
    }

    return () => controller.abort();
  }, [componentDetails, componentInspectors, loadComponentDetail, snapshot?.capturedAt]);

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

      if (control?.isPaused !== true || control.pendingStepCount > 0) {
        setError('Pause debug playback before editing components.');
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

      setComponentDetails({});
      refreshRevisionRef.current += 1;
      refreshInFlightRef.current = false;
      await refresh(undefined, {
        waitForFrame: false,
      });
    },
    [activeSceneEntry, control, dumpMode, refresh],
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

      setError(null);

      try {
        const result = await postDebugControl({
          command,
          stepCount: command === 'step' ? 1 : null,
        });

        if (!result.succeeded) {
          setError(result.message);
          return;
        }

        setControl(result.state);

        if (command === 'step') {
          const targetRevision = result.state.revision + Math.max(1, result.state.pendingStepCount);
          const message = await waitForStepSnapshot(targetRevision);
          if (!dumpModeRef.current) {
            commitSnapshotMessage(message);
            reloadInspectorDetails(true, true);
          }
        }
      } catch (caught) {
        setError(caught instanceof Error ? caught.message : String(caught));
      }
    },
    [commitSnapshotMessage, dumpFrames.length, dumpMode, reloadInspectorDetails, waitForStepSnapshot],
  );

  const selectDumpFrame = useCallback((frameIndex: number) => {
    if (!dumpMode) {
      return;
    }

    const nextFrameIndex = Math.min(Math.max(0, frameIndex), dumpFrames.length - 1);
    dumpFrameIndexRef.current = nextFrameIndex;
    setDumpPlaying(false);
    setDumpFrameIndex(nextFrameIndex);
    reloadDumpInspectorDetails(true, true);
  }, [dumpFrames.length, dumpMode, reloadDumpInspectorDetails]);

  const clearDumpPlayback = useCallback(() => {
    dumpModeRef.current = false;
    setDump(null);
    setDumpFrameIndex(0);
    setDumpPlaying(false);
    setComponentDetails({});
  }, []);

  const returnToLive = useCallback(() => {
    clearDumpPlayback();
    void refresh(undefined, {
      clearComponentDetails: true,
    });
  }, [clearDumpPlayback, refresh]);

  const connectDebugApi = useCallback((event?: React.FormEvent<HTMLFormElement>) => {
    event?.preventDefault();

    const nextConnection = normalizeDebugApiConnection(debugApiDraft);
    if (!nextConnection) {
      setError('Debug host requires a host and a port between 1 and 65535.');
      return;
    }

    try {
      setDebugApiBaseUrl(createDebugApiBaseUrl(nextConnection));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught));
      return;
    }

    writeStoredDebugApiConnection(nextConnection);
    refreshRevisionRef.current += 1;
    refreshInFlightRef.current = false;
    setDebugApiConnection(nextConnection);
    setDebugApiDraft(nextConnection);
    clearDumpPlayback();
    setSnapshot(null);
    setControl(null);
    setComponentDetails({});
    setError(null);
    setLoadState('loading');
    void refresh(undefined, {
      clearComponentDetails: true,
    });
  }, [clearDumpPlayback, debugApiDraft, refresh]);

  const loadDumpFile = useCallback(async (file: File) => {
    setDumpBusy(true);
    setError(null);
    try {
      const playback = await uploadDumpPlayback(await file.arrayBuffer());
      loadDumpPlayback(playback);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught));
    } finally {
      setDumpBusy(false);
    }
  }, [loadDumpPlayback]);

  const loadDumpAnalysisFile = useCallback(async (file: File) => {
    setDumpAnalysisBusy(true);
    setDumpAnalysisPanelOpen(true);
    setError(null);
    try {
      const report = await uploadDumpAnalysis(await file.arrayBuffer());
      setDumpAnalysis(report);
      showToast('Dump analyzed', `${report.name} · ${formatBytes(report.serializedBytes)}`);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught));
    } finally {
      setDumpAnalysisBusy(false);
    }
  }, [showToast]);

  const openDumpFilePicker = useCallback(async () => {
    const picker = (window as FilePickerWindow).showOpenFilePicker;
    if (picker) {
      try {
        const [handle] = await picker({
          id: DUMP_FILE_PICKER_ID,
          multiple: false,
          excludeAcceptAllOption: false,
          types: [{
            description: 'NKG dump files',
            accept: {
              'application/octet-stream': ['.nkgdump'],
            },
          }],
        });

        if (handle) {
          await loadDumpFile(await handle.getFile());
        }

        return;
      } catch (caught) {
        if (isFilePickerAbort(caught)) {
          return;
        }

        setError(caught instanceof Error ? caught.message : String(caught));
        return;
      }
    }

    dumpFileInputRef.current?.click();
  }, [loadDumpFile]);

  const openDumpAnalysisFilePicker = useCallback(async () => {
    setDumpAnalysisPanelOpen(true);
    const picker = (window as FilePickerWindow).showOpenFilePicker;
    if (picker) {
      try {
        const [handle] = await picker({
          id: DUMP_ANALYSIS_FILE_PICKER_ID,
          multiple: false,
          excludeAcceptAllOption: false,
          types: [{
            description: 'NKG dump files',
            accept: {
              'application/octet-stream': ['.nkgdump'],
            },
          }],
        });

        if (handle) {
          await loadDumpAnalysisFile(await handle.getFile());
        }

        return;
      } catch (caught) {
        if (isFilePickerAbort(caught)) {
          return;
        }

        setError(caught instanceof Error ? caught.message : String(caught));
        return;
      }
    }

    dumpAnalysisFileInputRef.current?.click();
  }, [loadDumpAnalysisFile]);

  const handleDumpFileChange = useCallback(async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) {
      await loadDumpFile(file);
    }
  }, [loadDumpFile]);

  const handleDumpAnalysisFileChange = useCallback(async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) {
      await loadDumpAnalysisFile(file);
    }
  }, [loadDumpAnalysisFile]);

  const waitForDumpRecordingFinalized = useCallback(async (signal?: AbortSignal) => {
    const startedAt = Date.now();

    while (true) {
      signal?.throwIfAborted();
      const state = await fetchDumpRecordingState(signal);
      setDumpRecording(state);

      if (state.lastDumpError) {
        throw new Error(state.lastDumpError);
      }

      if (!state.isFinalizing) {
        if (!state.lastDumpPath) {
          throw new Error('Dump recording finished without a dump file path.');
        }

        return state;
      }

      if (Date.now() - startedAt >= DUMP_FINALIZE_TIMEOUT_MS) {
        throw new Error('Timed out while waiting for the dump recording to finish saving.');
      }

      await delay(DUMP_FINALIZE_POLL_INTERVAL_MS);
    }
  }, []);

  const toggleDumpRecording = useCallback(async () => {
    const command = dumpRecording?.isRecording ? 'stop' : 'start';
    setDumpBusy(true);
    setError(null);

    try {
      if (command === 'start') {
        clearDumpPlayback();
      }

      const result = await postDumpRecording({
        command,
        dumpDirectory: command === 'start' ? WEB_DUMP_DIRECTORY : undefined,
      });
      setDumpRecording(result.state);

      if (!result.succeeded) {
        setError(result.message);
        return;
      }

      if (command === 'stop') {
        const finalizedState = result.state.lastDumpPath && !result.state.isFinalizing
          ? result.state
          : await waitForDumpRecordingFinalized();

        const playback = await openDumpPlayback({
          path: finalizedState.lastDumpPath,
        });
        loadDumpPlayback(playback);
        showToast(
          'Dump saved',
          finalizedState.lastDumpPath ?? result.message,
        );
      } else {
        await refresh(undefined, {
          clearComponentDetails: true,
        });
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught));
    } finally {
      setDumpBusy(false);
    }
  }, [
    clearDumpPlayback,
    dumpRecording?.isRecording,
    loadDumpPlayback,
    refresh,
    showToast,
    waitForDumpRecordingFinalized,
  ]);

  const dockModel = useMemo<DockWorkspaceModel>(
    () => ({
      snapshot,
      totals,
      scenes,
      activeSceneEntry,
      activeScene,
      filteredEntities,
      selectedEntity,
      selectedComponentKey,
      activeInspectorPanelId,
      componentInspectors,
      componentDetails,
      dumpAnalysis,
      dumpAnalysisBusy,
      isDumpAnalysisPanelOpen: dumpAnalysisPanelOpen,
      isDumpMode: dumpMode,
      selectScene,
      selectEntity,
      inspectComponent,
      toggleInspectorLock,
      closeInspectorPanel,
      closeDumpAnalysisPanel: () => setDumpAnalysisPanelOpen(false),
      openDumpAnalysisFilePicker,
      reloadComponentDetail,
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
      selectedComponentKey,
      activeInspectorPanelId,
      componentInspectors,
      componentDetails,
      dumpAnalysis,
      dumpAnalysisBusy,
      dumpAnalysisPanelOpen,
      dumpMode,
      selectScene,
      selectEntity,
      inspectComponent,
      toggleInspectorLock,
      closeInspectorPanel,
      openDumpAnalysisFilePicker,
      reloadComponentDetail,
      executeComponentMutation,
    ],
  );

  const isDumpFinalizing = dumpRecording?.isFinalizing === true;
  const dumpRecordingMetrics = dumpRecording?.metrics ?? null;
  const dumpRecordingMetricsSummary = dumpRecordingMetrics
    ? formatRecordingMetricsSummary(dumpRecordingMetrics)
    : null;
  const dumpRecordingMetricsTitle = dumpRecordingMetrics
    ? formatRecordingMetricsTitle(dumpRecordingMetrics)
    : undefined;
  const playbackPaused = dumpMode ? !dumpPlaying : control?.isPaused ?? false;
  const playbackCommand: GameDebugControlCommand = playbackPaused ? 'play' : 'pause';
  const playbackLabel = playbackPaused ? 'Play' : 'Pause';
  const PlaybackIcon = playbackPaused ? Play : Pause;

  return (
    <main className="app-shell">
      <input
        ref={dumpFileInputRef}
        className="hidden-file-input"
        type="file"
        accept=".nkgdump,application/octet-stream"
        onChange={(event) => void handleDumpFileChange(event)}
      />
      <input
        ref={dumpAnalysisFileInputRef}
        className="hidden-file-input"
        type="file"
        accept=".nkgdump,application/octet-stream"
        onChange={(event) => void handleDumpAnalysisFileChange(event)}
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
          <form className="connection-form" onSubmit={(event) => connectDebugApi(event)}>
            <input
              value={debugApiDraft.host}
              onChange={(event) =>
                setDebugApiDraft((current) => ({
                  ...current,
                  host: event.target.value,
                }))
              }
              aria-label="Debug host IP"
              title="Debug host IP"
            />
            <input
              value={debugApiDraft.port}
              onChange={(event) =>
                setDebugApiDraft((current) => ({
                  ...current,
                  port: event.target.value,
                }))
              }
              inputMode="numeric"
              aria-label="Debug host port"
              title="Debug host port"
            />
            <button className="primary-button" type="submit" title={`Connect to ${debugApiBaseUrl}`}>
              <Activity size={17} />
              Connect
            </button>
          </form>
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
            className={dumpAnalysisPanelOpen ? 'icon-button active' : 'icon-button'}
            type="button"
            onClick={() => setDumpAnalysisPanelOpen(true)}
            title="Open dump analysis report"
          >
            <Sparkles size={17} />
            Analyze Dump
          </button>
          <button
            className={dumpRecording?.isRecording || isDumpFinalizing ? 'icon-button active recording' : 'icon-button'}
            type="button"
            onClick={() => void toggleDumpRecording()}
            disabled={dumpBusy || isDumpFinalizing}
            title={dumpRecording?.isRecording
              ? dumpRecordingMetricsTitle ?? 'Stop dump recording'
              : isDumpFinalizing
                ? 'Saving dump recording'
                : 'Start dump recording'}
          >
            {dumpRecording?.isRecording ? <Square size={16} /> : <Circle size={16} />}
            {dumpRecording?.isRecording ? 'Stop Rec' : isDumpFinalizing ? 'Saving' : 'Record'}
          </button>
          {dumpRecording?.isRecording && dumpRecordingMetricsSummary ? (
            <span className="recording-metrics" title={dumpRecordingMetricsTitle}>
              {dumpRecordingMetricsSummary}
            </span>
          ) : null}
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
            className={playbackPaused ? 'icon-button' : 'icon-button active'}
            type="button"
            onClick={() => void executeControl(playbackCommand)}
            title={playbackLabel}
          >
            <PlaybackIcon size={17} />
            {playbackLabel}
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

      {dumpMode ? (
        <DumpTimeline
          dump={dump}
          currentIndex={dumpFrameIndex}
          isPlaying={dumpPlaying}
          onSelectFrame={selectDumpFrame}
        />
      ) : null}

      {error ? <div className="error-banner">{error}</div> : null}
      {toast ? (
        <div className="toast-notice" role="status" aria-live="polite">
          <strong>{toast.title}</strong>
          <span>{toast.body}</span>
        </div>
      ) : null}

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

  useEffect(() => {
    if (!api) {
      return;
    }

    for (const inspector of model.componentInspectors) {
      const panel = api.getPanel(inspector.id);
      if (panel) {
        panel.api.updateParameters({ inspectorId: inspector.id });
        panel.api.setTitle(formatInspectorPanelTitle(inspector));
      } else {
        addInspectorDockPanel(api, inspector);
      }
    }

    if (model.activeInspectorPanelId) {
      api.getPanel(model.activeInspectorPanelId)?.api.setActive();
    }

    saveDockLayout(api);
  }, [api, model.activeInspectorPanelId, model.componentInspectors]);

  useEffect(() => {
    if (!api) {
      return;
    }

    if (model.isDumpAnalysisPanelOpen) {
      const panel = api.getPanel(DUMP_ANALYSIS_PANEL_ID);
      if (panel) {
        panel.api.setActive();
      } else {
        addDumpAnalysisDockPanel(api);
      }

      saveDockLayout(api);
    }
  }, [api, model.isDumpAnalysisPanelOpen]);

  useEffect(() => {
    if (!api) {
      return;
    }

    const disposable = api.onDidRemovePanel((panel) => {
      if (isInspectorPanelId(panel.api.id)) {
        model.closeInspectorPanel(panel.api.id);
      } else if (panel.api.id === DUMP_ANALYSIS_PANEL_ID) {
        model.closeDumpAnalysisPanel();
      }
    });
    return () => disposable.dispose();
  }, [api, model]);

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
  dump: GameDebugDumpPlaybackManifest | null;
  currentIndex: number;
  isPlaying: boolean;
  onSelectFrame: (frameIndex: number) => void;
}) {
  const frames = dump?.frames ?? [];
  const max = Math.max(0, frames.length - 1);
  const activeIndex = Math.min(Math.max(0, currentIndex), max);
  const current = frames[activeIndex] ?? null;
  const [timelineZoom, setTimelineZoom] = useState(TIMELINE_MIN_ZOOM);
  const [timelineMaxFpsInput, setTimelineMaxFpsInput] = useState(String(TIMELINE_DEFAULT_MAX_FPS));
  const [timelineViewportWidth, setTimelineViewportWidth] = useState(0);
  const timelineMaxFps = useMemo(() => readTimelineMaxFps(timelineMaxFpsInput), [timelineMaxFpsInput]);
  const samples = useMemo(() => createTimelineSamples(frames, timelineMaxFps), [frames, timelineMaxFps]);
  const ticks = useMemo(() => createTimelineTicks(samples, timelineZoom), [samples, timelineZoom]);
  const fpsAxisTicks = useMemo(() => createTimelineFpsAxisTicks(timelineMaxFps), [timelineMaxFps]);
  const timelinePoints = useMemo(
    () => createTimelineChartPoints(samples, timelineViewportWidth, timelineZoom),
    [samples, timelineViewportWidth, timelineZoom],
  );
  const timelineLinePath = useMemo(() => createTimelineLinePath(timelinePoints), [timelinePoints]);
  const timelineAreaPath = useMemo(() => createTimelineAreaPath(timelinePoints), [timelinePoints]);
  const timelineWeightTotal = useMemo(() => sumTimelineDurationWeights(samples), [samples]);
  const currentSample = samples[activeIndex] ?? null;
  const chartRef = useRef<HTMLDivElement | null>(null);
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const chartDraggingRef = useRef(false);
  const cursorLeft = getTimelineSampleCenterPercent(samples, activeIndex, timelineWeightTotal);
  const timelineContentWidth = timelineViewportWidth > 0
    ? Math.max(timelineViewportWidth, Math.round(timelineViewportWidth * timelineZoom))
    : undefined;
  const playbackState = isPlaying ? 'Playing' : 'Paused';
  const zoomLabel = `${Math.round(timelineZoom * 100)}%`;
  const plotStyle = {
    '--fps-20': `${getFrameSampleHeight(20, timelineMaxFps)}%`,
    '--fps-30': `${getFrameSampleHeight(30, timelineMaxFps)}%`,
    '--fps-60': `${getFrameSampleHeight(60, timelineMaxFps)}%`,
  } as React.CSSProperties;

  useEffect(() => {
    setTimelineZoom(TIMELINE_MIN_ZOOM);
  }, [dump?.name]);

  const updateTimelineMaxFps = useCallback((event: React.ChangeEvent<HTMLInputElement>) => {
    setTimelineMaxFpsInput(event.currentTarget.value);
  }, []);

  const commitTimelineMaxFps = useCallback(() => {
    setTimelineMaxFpsInput(String(readTimelineMaxFps(timelineMaxFpsInput)));
  }, [timelineMaxFpsInput]);

  useEffect(() => {
    const element = scrollRef.current;
    if (!element) {
      return;
    }

    const updateViewportWidth = () => {
      setTimelineViewportWidth(element.clientWidth);
    };

    updateViewportWidth();

    if (typeof ResizeObserver === 'undefined') {
      window.addEventListener('resize', updateViewportWidth);
      return () => window.removeEventListener('resize', updateViewportWidth);
    }

    const observer = new ResizeObserver(updateViewportWidth);
    observer.observe(element);
    return () => observer.disconnect();
  }, [dump?.name]);

  useEffect(() => {
    const element = scrollRef.current;
    if (!element || max <= 0) {
      return;
    }

    const cursorX = (getTimelineSampleCenterPercent(samples, activeIndex, timelineWeightTotal) / 100) *
      element.scrollWidth;
    const leftEdge = element.scrollLeft + 24;
    const rightEdge = element.scrollLeft + element.clientWidth - 24;

    if (cursorX < leftEdge) {
      element.scrollTo({ left: Math.max(0, cursorX - element.clientWidth * 0.35) });
    } else if (cursorX > rightEdge) {
      element.scrollTo({ left: Math.max(0, cursorX - element.clientWidth * 0.65) });
    }
  }, [activeIndex, max, samples, timelineWeightTotal, timelineZoom]);

  const zoomTimelineAtPointer = useCallback((event: WheelEvent) => {
    if (!event.ctrlKey) {
      return;
    }

    const element = scrollRef.current;
    if (!element) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();

    const rect = element.getBoundingClientRect();
    const pointerX = clamp(event.clientX - rect.left, 0, element.clientWidth);
    const anchorRatio = element.scrollWidth > 0
      ? (element.scrollLeft + pointerX) / element.scrollWidth
      : 0;
    const direction = event.deltaY < 0 ? 1 : -1;

    setTimelineZoom((currentZoom) => {
      const nextZoom = clamp(
        currentZoom + direction * TIMELINE_ZOOM_STEP,
        TIMELINE_MIN_ZOOM,
        TIMELINE_MAX_ZOOM,
      );

      if (nextZoom !== currentZoom) {
        window.requestAnimationFrame(() => {
          element.scrollLeft = clamp(
            anchorRatio * element.scrollWidth - pointerX,
            0,
            Math.max(0, element.scrollWidth - element.clientWidth),
          );
        });
      }

      return nextZoom;
    });
  }, []);

  useEffect(() => {
    const element = scrollRef.current;
    if (!element) {
      return;
    }

    element.addEventListener('wheel', zoomTimelineAtPointer, {
      capture: true,
      passive: false,
    });

    return () => {
      element.removeEventListener('wheel', zoomTimelineAtPointer, { capture: true });
    };
  }, [zoomTimelineAtPointer]);

  const selectFrameAtClientX = useCallback((clientX: number) => {
    if (max <= 0) {
      return;
    }

    const element = chartRef.current;
    if (!element) {
      return;
    }

    const rect = element.getBoundingClientRect();
    const ratio = clamp((clientX - rect.left) / rect.width, 0, 1);
    onSelectFrame(getTimelineIndexAtPercent(samples, ratio, timelineWeightTotal));
  }, [max, onSelectFrame, samples, timelineWeightTotal]);

  const selectFrameFromPointer = useCallback((event: React.PointerEvent<HTMLDivElement>) => {
    selectFrameAtClientX(event.clientX);
  }, [selectFrameAtClientX]);

  const beginChartDrag = useCallback((event: React.PointerEvent<HTMLDivElement>) => {
    chartDraggingRef.current = true;
    event.currentTarget.setPointerCapture(event.pointerId);
    selectFrameFromPointer(event);
  }, [selectFrameFromPointer]);

  const continueChartDrag = useCallback((event: React.PointerEvent<HTMLDivElement>) => {
    if (chartDraggingRef.current) {
      selectFrameFromPointer(event);
    }
  }, [selectFrameFromPointer]);

  const endChartDrag = useCallback((event: React.PointerEvent<HTMLDivElement>) => {
    chartDraggingRef.current = false;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
  }, []);

  const handleTimelineKeyDown = useCallback((event: React.KeyboardEvent<HTMLDivElement>) => {
    if (max <= 0) {
      return;
    }

    let nextIndex: number | null = null;
    if (event.key === 'ArrowLeft') {
      nextIndex = activeIndex - 1;
    } else if (event.key === 'ArrowRight') {
      nextIndex = activeIndex + 1;
    } else if (event.key === 'PageUp') {
      nextIndex = activeIndex - 10;
    } else if (event.key === 'PageDown') {
      nextIndex = activeIndex + 10;
    } else if (event.key === 'Home') {
      nextIndex = 0;
    } else if (event.key === 'End') {
      nextIndex = max;
    }

    if (nextIndex !== null) {
      event.preventDefault();
      onSelectFrame(clamp(nextIndex, 0, max));
    }
  }, [activeIndex, max, onSelectFrame]);

  if (frames.length === 0) {
    return null;
  }

  return (
    <section className="dump-timeline">
      <div className="dump-timeline-header">
        <div className="dump-timeline-title">
          <span>Logic Profiler</span>
          <strong>{dump?.name ?? 'Loaded dump'}</strong>
        </div>
        <div className="dump-timeline-meta">
          <div className="dump-timeline-stats">
            <span>{activeIndex + 1} / {frames.length}</span>
            <span>{current ? `Frame ${current.frame.frame}` : 'Frame -'}</span>
            <span>{currentSample ? formatFps(currentSample.fps) : 'FPS -'}</span>
            <span>{currentSample ? formatMilliseconds(currentSample.milliseconds) : 'Frame -'}</span>
            <span>{playbackState}</span>
          </div>
          <div className="dump-timeline-zoom" aria-label="Profiler zoom">
            <span>{zoomLabel}</span>
          </div>
          <label className="dump-timeline-scale">
            <span>Max FPS</span>
            <input
              type="number"
              step={10}
              value={timelineMaxFpsInput}
              onChange={updateTimelineMaxFps}
              onBlur={commitTimelineMaxFps}
            />
          </label>
        </div>
      </div>

      <div className="dump-profiler-viewport">
        <div className="dump-profiler-axis" aria-hidden>
          <span>FPS</span>
          {fpsAxisTicks.map((tick) => (
            <em
              key={tick.fps}
              style={{ bottom: `${tick.axisBottomPercent}%` }}
            >
              {tick.label}
            </em>
          ))}
        </div>
        <div ref={scrollRef} className="dump-profiler-scroll">
          <div
            className="dump-profiler-content"
            style={timelineContentWidth ? { width: `${timelineContentWidth}px` } : undefined}
          >
            <div className="dump-profiler-ruler" aria-hidden>
              {ticks.map((tick) => (
                <span
                  key={`${tick.index}:${tick.label}`}
                  style={{ left: `${tick.leftPercent}%` }}
                >
                  {tick.label}
                </span>
              ))}
            </div>
            <div
              ref={chartRef}
              className="dump-profiler-track"
              role="slider"
              aria-label="Frame profiler line chart"
              aria-valuemin={1}
              aria-valuemax={frames.length}
              aria-valuenow={activeIndex + 1}
              aria-valuetext={currentSample?.label}
              tabIndex={0}
              title={currentSample?.label}
              onKeyDown={handleTimelineKeyDown}
              onPointerDown={beginChartDrag}
              onPointerMove={continueChartDrag}
              onPointerUp={endChartDrag}
              onPointerCancel={endChartDrag}
            >
              <div className="dump-profiler-plot" aria-hidden style={plotStyle}>
                {fpsAxisTicks.map((tick) => (
                  <span
                    key={tick.fps}
                    className="dump-profiler-fps-line"
                    style={{ bottom: `${tick.trackBottomPercent}%` }}
                  />
                ))}
                <svg
                  className="dump-profiler-chart"
                  viewBox="0 0 100 100"
                  preserveAspectRatio="none"
                  focusable="false"
                >
                  <path className="dump-profiler-area" d={timelineAreaPath} />
                  <path className="dump-profiler-line" d={timelineLinePath} />
                </svg>
                {currentSample ? (
                  <span
                    className="dump-profiler-active-dot"
                    style={{
                      left: `${cursorLeft}%`,
                      bottom: `${currentSample.heightPercent}%`,
                      '--heat-color': currentSample.color,
                    } as React.CSSProperties}
                  />
                ) : null}
              </div>
            </div>
            <span
              className="dump-profiler-cursor"
              aria-hidden
              style={{ left: `${cursorLeft}%` }}
            />
          </div>
        </div>
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
          componentQuery={componentQuery}
          onComponentQueryChange={setComponentQuery}
          selectedComponentKey={model.selectedComponentKey}
          onInspectComponent={model.inspectComponent}
        />
      ) : (
        <EmptyDetails />
      )}
    </DockPanelBody>
  );
}

function ComponentInspectorDockPanel(props: DockPanelProps) {
  const model = useDockModel();
  const panelId = props.params.inspectorId ?? DEFAULT_INSPECTOR_PANEL_ID;
  const panel = model.componentInspectors.find((candidate) => candidate.id === panelId);
  const target = panel?.target ?? null;
  const entry = target ? model.componentDetails[createComponentDetailKey(target)] : null;
  const entity = entry?.entity ?? null;
  const component = entry?.component ?? null;
  const busy = entry?.status === 'loading';
  const entityVersion = entity?.version ?? target?.entityVersion;

  return (
    <DockPanelBody className="component-inspector-panel">
      <div className="inspector-heading">
        <div>
          <h2>{target?.componentTypeName ?? 'Inspector'}</h2>
          <p>{target ? `Entity #${target.entityId} · Version ${entityVersion}` : 'No component'}</p>
        </div>
        <div className="inspector-actions">
          {target ? (
            <button
              className={panel?.locked ? 'icon-button active compact' : 'icon-button compact'}
              type="button"
              onClick={() => model.toggleInspectorLock(panelId)}
              title={panel?.locked ? 'Unlock panel' : 'Lock panel'}
            >
              {panel?.locked ? <Lock size={15} /> : <Unlock size={15} />}
            </button>
          ) : null}
          {target ? (
            <button
              className="icon-button compact"
              type="button"
              onClick={() => model.reloadComponentDetail(target)}
              title="Refresh component"
              disabled={busy}
            >
              <RefreshCw size={15} className={busy ? 'spin' : undefined} />
            </button>
          ) : null}
        </div>
      </div>

      {entry?.status === 'error' ? <div className="error-banner compact">{entry.message}</div> : null}

      {entity && component ? (
        <Suspense fallback={<div className="empty-details"><span>Loading component</span></div>}>
          <ComponentInspector
            entity={entity}
            component={component}
            onSaveComponent={model.onSaveComponent}
            readOnly={model.isDumpMode}
          />
        </Suspense>
      ) : (
        <div className="empty-details">
          <Cpu size={28} />
          <span>{target ? 'Loading component' : 'No component'}</span>
        </div>
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

function DumpAnalysisDockPanel(_props: DockPanelProps) {
  const model = useDockModel();
  const report = model.dumpAnalysis;
  const topTypes = report?.types.slice(0, 8) ?? [];
  const topFields = report?.fields.slice(0, 8) ?? [];
  const topScenes = report?.scenes.slice(0, 5) ?? [];
  const topEntities = report?.entities.slice(0, 5) ?? [];
  const payloadPercent = report ? getPercent(report.total.payloadBytes, report.total.totalBytes) : 0;
  const structuredPercent = report ? getPercent(report.total.structuredBytes, report.total.totalBytes) : 0;
  const expandedRatio = report?.serializedBytes
    ? report.total.totalBytes / Math.max(1, report.serializedBytes)
    : 0;

  return (
    <DockPanelBody className="dump-analysis-panel">
      <div className="dump-report-heading">
        <div>
          <h2>{report?.name ?? 'Dump Report'}</h2>
          <p>
            {report
              ? `${report.frameCount} frames · file ${formatBytes(report.serializedBytes)} · expanded ${formatBytes(report.total.totalBytes)}`
              : 'No dump analyzed'}
          </p>
        </div>
        <button
          className="primary-button"
          type="button"
          onClick={model.openDumpAnalysisFilePicker}
          disabled={model.dumpAnalysisBusy}
          title="Load dump for analysis"
        >
          <Upload size={16} />
          {model.dumpAnalysisBusy ? 'Analyzing' : 'Load Dump'}
        </button>
      </div>

      {report ? (
        <>
          <div className="dump-report-summary">
            <DumpReportMetric label="File Size" value={formatBytes(report.serializedBytes)} />
            <DumpReportMetric label="Expanded" value={formatBytes(report.total.totalBytes)} />
            <DumpReportMetric label="Payload" value={formatBytes(report.total.payloadBytes)} tone="payload" />
            <DumpReportMetric label="Structured" value={formatBytes(report.total.structuredBytes)} tone="structured" />
            <DumpReportMetric label="Frames" value={String(report.frameCount)} />
          </div>

          {report.recordingMetrics ? (
            <div className="dump-report-summary">
              <DumpReportMetric
                label="Captured"
                value={`${report.recordingMetrics.capturedFrameCount}/${report.recordingMetrics.publishedFrameCount}`}
              />
              <DumpReportMetric
                label="Max Callback"
                value={formatCompactMilliseconds(report.recordingMetrics.maxFrameCallbackMilliseconds)}
              />
              <DumpReportMetric
                label="Max Capture"
                value={formatCompactMilliseconds(report.recordingMetrics.maxCaptureMilliseconds)}
              />
              <DumpReportMetric
                label="Max Stores"
                value={String(report.recordingMetrics.maxCapturedStoreCount)}
              />
              <DumpReportMetric
                label="Max Rows"
                value={String(report.recordingMetrics.maxCapturedEntityRowCount)}
              />
            </div>
          ) : null}

          <section className="dump-report-section">
            <div className="dump-report-section-title">
              <strong>Expanded Size Mix</strong>
              <span>
                {payloadPercent.toFixed(1)}% payload · {structuredPercent.toFixed(1)}% structured · {expandedRatio.toFixed(1)}x file
              </span>
            </div>
            <div className="dump-size-mix" aria-label="Dump size mix">
              <span
                className="payload"
                style={{ width: `${payloadPercent}%` }}
                title={`Payload ${formatBytes(report.total.payloadBytes)}`}
              />
              <span
                className="structured"
                style={{ width: `${structuredPercent}%` }}
                title={`Structured ${formatBytes(report.total.structuredBytes)}`}
              />
            </div>
          </section>

          <div className="dump-report-grid">
            <DumpReportRankList title="Heaviest Types" entries={topTypes} totalBytes={report.total.totalBytes} />
            <DumpReportRankList title="Heaviest Fields" entries={topFields} totalBytes={report.total.structuredBytes || report.total.totalBytes} />
            <DumpReportRankList title="Scenes" entries={topScenes} totalBytes={report.total.totalBytes} compact />
            <DumpReportRankList title="Entities" entries={topEntities} totalBytes={report.total.totalBytes} compact />
          </div>
        </>
      ) : (
        <div className="empty-details dump-report-empty">
          <Sparkles size={28} />
          <span>{model.dumpAnalysisBusy ? 'Analyzing dump' : 'Load a dump file to inspect its size profile'}</span>
        </div>
      )}
    </DockPanelBody>
  );
}

function DumpReportMetric({
  label,
  value,
  tone,
}: {
  label: string;
  value: string;
  tone?: 'payload' | 'structured';
}) {
  return (
    <div className={tone ? `dump-report-metric ${tone}` : 'dump-report-metric'}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function DumpReportRankList({
  title,
  entries,
  totalBytes,
  compact = false,
}: {
  title: string;
  entries: GameDebugDumpAnalysisEntry[];
  totalBytes: number;
  compact?: boolean;
}) {
  return (
    <section className="dump-report-section">
      <div className="dump-report-section-title">
        <strong>{title}</strong>
        <span>{entries.length} shown</span>
      </div>
      <div className={compact ? 'dump-rank-list compact' : 'dump-rank-list'}>
        {entries.length ? entries.map((entry, index) => (
          <div className="dump-rank-row" key={entry.key}>
            <div className="dump-rank-row-head">
              <span>{index + 1}</span>
              <strong title={entry.key}>{entry.displayName ?? entry.key}</strong>
              <em>{formatBytes(entry.size.totalBytes)}</em>
            </div>
            <div className="dump-rank-bar" aria-hidden>
              <span style={{ width: `${getPercent(entry.size.totalBytes, totalBytes)}%` }} />
            </div>
            <div className="dump-rank-meta">
              <span>{entry.count} samples</span>
              <span>{formatBytes(entry.size.payloadBytes)} payload</span>
              <span>{formatBytes(entry.size.structuredBytes)} structured</span>
            </div>
          </div>
        )) : (
          <div className="empty-details compact">
            <span>No entries</span>
          </div>
        )}
      </div>
    </section>
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
    id: DEFAULT_INSPECTOR_PANEL_ID,
    title: 'Inspector',
    component: 'inspector',
    initialWidth: 380,
    params: {
      inspectorId: DEFAULT_INSPECTOR_PANEL_ID,
    },
    position: {
      referencePanel: 'components',
      direction: 'right',
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
  const inspectorWidth = Math.min(460, Math.max(340, Math.round(api.width * 0.22)));
  const diagnosticsHeight = Math.min(520, Math.max(260, Math.round(api.height * 0.42)));
  const mainWidth = Math.max(520, api.width - navigationWidth - inspectorWidth);

  api.getPanel('scenes')?.api.setSize({ width: navigationWidth });
  api.getPanel('entities')?.api.setSize({ width: navigationWidth });
  api.getPanel('runtime')?.api.setSize({ width: navigationWidth });
  api.getPanel('diagnostics')?.api.setSize({ width: navigationWidth, height: diagnosticsHeight });
  api.getPanel('components')?.api.setSize({ width: mainWidth });
  api.getPanel(DEFAULT_INSPECTOR_PANEL_ID)?.api.setSize({ width: inspectorWidth });
}

function normalizeDockPanelTitles(api: DockviewApi) {
  for (const [panelId, title] of Object.entries(DOCK_PANEL_TITLES)) {
    api.getPanel(panelId)?.api.setTitle(title);
  }
}

function addInspectorDockPanel(api: DockviewApi, inspector: ComponentInspectorPanelState) {
  const referencePanel = api.getPanel(DEFAULT_INSPECTOR_PANEL_ID) ??
    api.getPanel('components') ??
    api.activePanel;
  const isDefault = inspector.id === DEFAULT_INSPECTOR_PANEL_ID;

  api.addPanel({
    id: inspector.id,
    title: formatInspectorPanelTitle(inspector),
    component: 'inspector',
    initialWidth: 380,
    params: {
      inspectorId: inspector.id,
    },
    position: referencePanel
      ? {
          referencePanel,
          direction: isDefault ? 'right' : 'within',
        }
      : undefined,
  });
}

function formatInspectorPanelTitle(inspector: ComponentInspectorPanelState) {
  const title = inspector.target?.componentTypeName ?? 'Inspector';
  return inspector.locked ? `${title} [Locked]` : title;
}

function isInspectorPanelId(panelId: string) {
  return panelId === DEFAULT_INSPECTOR_PANEL_ID || panelId.startsWith('inspector:');
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
  componentQuery,
  onComponentQueryChange,
  selectedComponentKey,
  onInspectComponent,
}: {
  entity: EntityDebugSnapshot;
  componentQuery: string;
  onComponentQueryChange: (value: string) => void;
  selectedComponentKey: string | null;
  onInspectComponent: (entity: EntityDebugSnapshot, component: ComponentDebugSnapshot) => void;
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
        {entity.components.length ? (
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
              selectedComponentKey={selectedComponentKey}
              onInspectComponent={(component) => onInspectComponent(entity, component)}
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

function createTimelineSamples(frames: GameDebugDumpPlaybackFrame[], maxFps: number): TimelineFrameSample[] {
  return frames.map((entry, index) => {
    const metrics = readTimelineFrameMetrics(entry.frame);
    const fpsText = formatFps(metrics.fps);
    const millisecondsText = formatMilliseconds(metrics.milliseconds);

    return {
      index,
      frameNumber: entry.frame.frame,
      fps: metrics.fps,
      milliseconds: metrics.milliseconds,
      durationWeight: getTimelineDurationWeight(metrics.milliseconds),
      heightPercent: getFrameSampleHeight(metrics.fps, maxFps),
      color: getFrameHeatColor(metrics.milliseconds),
      label: `Frame ${entry.frame.frame} | Logic ${millisecondsText} | ${fpsText}`,
    };
  });
}

function addDumpAnalysisDockPanel(api: DockviewApi) {
  const referencePanel = api.getPanel('diagnostics') ??
    api.getPanel('components') ??
    api.activePanel;

  api.addPanel({
    id: DUMP_ANALYSIS_PANEL_ID,
    title: DOCK_PANEL_TITLES[DUMP_ANALYSIS_PANEL_ID],
    component: 'dumpAnalysis',
    initialHeight: 420,
    position: referencePanel
      ? {
          referencePanel,
          direction: referencePanel.api.id === 'diagnostics' ? 'within' : 'below',
        }
      : undefined,
  });
}

function createTimelineFpsAxisTicks(maxFps: number): TimelineFpsTick[] {
  const normalizedMaxFps = readTimelineMaxFps(maxFps);
  const candidates = [20, 30, 60, 120, 240, 500, 1000, 2000].filter((fps) => fps <= normalizedMaxFps);
  const tickValues = Array.from(new Set([...candidates, normalizedMaxFps])).sort((left, right) => left - right);

  return tickValues.map((fps) => {
    const trackBottomPercent = getFrameSampleHeight(fps, normalizedMaxFps);

    return {
      fps,
      trackBottomPercent,
      axisBottomPercent: (trackBottomPercent / 100 * TIMELINE_TRACK_HEIGHT_PX / TIMELINE_CONTENT_HEIGHT_PX) * 100,
      label: `${fps}`,
    };
  });
}

function readTimelineMaxFps(value: string | number) {
  const parsed = typeof value === 'number' ? value : Number(value);
  if (!isPositiveFinite(parsed)) {
    return TIMELINE_DEFAULT_MAX_FPS;
  }

  return Math.round(clamp(parsed, 1, TIMELINE_MAX_MAX_FPS));
}

function createTimelineChartPoints(
  samples: TimelineFrameSample[],
  viewportWidth: number,
  zoom: number,
): TimelineChartPoint[] {
  if (samples.length === 0) {
    return [];
  }

  const effectiveViewportWidth = Number.isFinite(viewportWidth) && viewportWidth > 0 ? viewportWidth : 0;
  const targetPoints = effectiveViewportWidth > 0
    ? clamp(
      Math.ceil(effectiveViewportWidth * Math.max(1, zoom) * TIMELINE_RENDER_OVERSAMPLE),
      TIMELINE_MIN_RENDERED_POINTS,
      TIMELINE_MAX_RENDERED_POINTS,
    )
    : TIMELINE_MIN_RENDERED_POINTS;
  const totalWeight = sumTimelineDurationWeights(samples);
  const points: TimelineChartPoint[] = [];
  const seen = new Set<number>();
  const addPoint = (sample: TimelineFrameSample) => {
    if (seen.has(sample.index)) {
      return;
    }

    seen.add(sample.index);
    points.push({
      index: sample.index,
      frameNumber: sample.frameNumber,
      fps: sample.fps,
      milliseconds: sample.milliseconds,
      heightPercent: sample.heightPercent,
      xPercent: getTimelineSampleCenterPercent(samples, sample.index, totalWeight),
      color: sample.color,
      label: sample.label,
    });
  };

  if (samples.length <= targetPoints) {
    for (const sample of samples) {
      addPoint(sample);
    }

    return points;
  }

  const bucketCount = Math.min(samples.length, targetPoints);
  const bucketSize = Math.ceil(samples.length / bucketCount);
  addPoint(samples[0]);

  for (let start = 0; start < samples.length; start += bucketSize) {
    const end = Math.min(samples.length, start + bucketSize);
    const first = samples[start];
    let representative = first;
    let representativeMilliseconds = first.milliseconds;

    for (let index = start + 1; index < end; index++) {
      const candidate = samples[index];

      if (representativeMilliseconds === null) {
        if (candidate.milliseconds !== null) {
          representative = candidate;
          representativeMilliseconds = candidate.milliseconds;
        }

        continue;
      }

      if (candidate.milliseconds !== null && candidate.milliseconds > representativeMilliseconds) {
        representative = candidate;
        representativeMilliseconds = candidate.milliseconds;
      }
    }

    addPoint(representative);
  }

  addPoint(samples[samples.length - 1]);
  return points.sort((left, right) => left.index - right.index);
}

function createTimelineLinePath(points: TimelineChartPoint[]) {
  if (points.length === 0) {
    return '';
  }

  if (points.length === 1) {
    const point = points[0];
    return `M ${formatSvgCoordinate(point.xPercent)} ${formatSvgCoordinate(100 - point.heightPercent)} ` +
      `L ${formatSvgCoordinate(point.xPercent)} ${formatSvgCoordinate(100 - point.heightPercent)}`;
  }

  return points
    .map((point, index) => {
      const command = index === 0 ? 'M' : 'L';
      return `${command} ${formatSvgCoordinate(point.xPercent)} ${formatSvgCoordinate(100 - point.heightPercent)}`;
    })
    .join(' ');
}

function createTimelineAreaPath(points: TimelineChartPoint[]) {
  if (points.length === 0) {
    return '';
  }

  const first = points[0];
  const last = points[points.length - 1];
  const pointPath = points
    .map((point) => `${formatSvgCoordinate(point.xPercent)} ${formatSvgCoordinate(100 - point.heightPercent)}`)
    .join(' L ');

  return `M ${formatSvgCoordinate(first.xPercent)} 100 L ${pointPath} ` +
    `L ${formatSvgCoordinate(last.xPercent)} 100 Z`;
}

function createTimelineTicks(samples: TimelineFrameSample[], zoom: number): TimelineTick[] {
  if (samples.length === 0) {
    return [];
  }

  if (samples.length === 1) {
    return [{
      index: 0,
      leftPercent: 0,
      label: String(samples[0].frameNumber),
    }];
  }

  const tickCount = Math.min(Math.max(6, Math.round(6 * zoom)), samples.length);
  const ticks: TimelineTick[] = [];
  const used = new Set<number>();
  const totalWeight = sumTimelineDurationWeights(samples);

  for (let tick = 0; tick < tickCount; tick++) {
    const index = Math.round((samples.length - 1) * (tick / (tickCount - 1)));
    if (used.has(index)) {
      continue;
    }

    used.add(index);
    ticks.push({
      index,
      leftPercent: getTimelineSampleCenterPercent(samples, index, totalWeight),
      label: String(samples[index].frameNumber),
    });
  }

  return ticks;
}

function getTimelineIndexAtPercent(
  samples: TimelineFrameSample[],
  percent: number,
  totalWeight = sumTimelineDurationWeights(samples),
) {
  if (samples.length <= 1 || totalWeight <= 0) {
    return 0;
  }

  const targetWeight = clamp(percent, 0, 1) * totalWeight;
  let weight = 0;

  for (const sample of samples) {
    weight += sample.durationWeight;
    if (targetWeight <= weight) {
      return sample.index;
    }
  }

  return samples.length - 1;
}

function getTimelineSampleCenterPercent(
  samples: TimelineFrameSample[],
  index: number,
  totalWeight = sumTimelineDurationWeights(samples),
) {
  if (samples.length === 0 || totalWeight <= 0) {
    return 0;
  }

  const targetIndex = clamp(Math.round(index), 0, samples.length - 1);
  let weightBefore = 0;

  for (let sampleIndex = 0; sampleIndex < targetIndex; sampleIndex++) {
    weightBefore += samples[sampleIndex].durationWeight;
  }

  return ((weightBefore + samples[targetIndex].durationWeight / 2) / totalWeight) * 100;
}

function sumTimelineDurationWeights(samples: Array<{ durationWeight: number }>) {
  return samples.reduce((total, sample) => total + Math.max(1, sample.durationWeight), 0);
}

function readTimelineFrameMetrics(frame: GameDebugFrameInfo) {
  const metrics = frame.metrics;
  const milliseconds = isPositiveFinite(metrics?.logicMilliseconds)
    ? metrics.logicMilliseconds
    : null;
  const fps = isPositiveFinite(metrics?.logicFramesPerSecond)
    ? metrics.logicFramesPerSecond
    : milliseconds !== null && milliseconds > 0
      ? 1000 / milliseconds
      : null;

  return { fps, milliseconds };
}

function getFrameHeatColor(milliseconds: number | null) {
  if (milliseconds === null) {
    return '#4b5563';
  }

  if (milliseconds <= 16.7) {
    return '#2fbf71';
  }

  if (milliseconds <= 22.3) {
    return '#d9c84d';
  }

  if (milliseconds <= 33.4) {
    return '#f29f45';
  }

  if (milliseconds <= 50) {
    return '#e15f5b';
  }

  return '#b45cff';
}

function getFrameSampleHeight(fps: number | null, maxFps: number) {
  if (fps === null) {
    return 0;
  }

  return clamp((fps / Math.max(1, maxFps)) * 100, 0, 100);
}

function getTimelineDurationWeight(milliseconds: number | null) {
  return milliseconds === null
    ? 16.7
    : clamp(milliseconds, 4, 100);
}

function formatSvgCoordinate(value: number) {
  return clamp(value, 0, 100).toFixed(3);
}

function getPercent(value: number, total: number) {
  if (total <= 0 || value <= 0) {
    return 0;
  }

  return clamp((value / total) * 100, 0, 100);
}

function formatBytes(value: number) {
  const units = ['B', 'KB', 'MB', 'GB'];
  let size = Math.max(0, value);
  let unitIndex = 0;
  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024;
    unitIndex++;
  }

  const precision = unitIndex === 0 || size >= 100 ? 0 : 1;
  return `${size.toFixed(precision)} ${units[unitIndex]}`;
}

function formatRecordingMetricsSummary(metrics: GameDebugDumpRecordingMetrics) {
  return [
    `${metrics.capturedFrameCount}/${metrics.publishedFrameCount} frames`,
    `${metrics.pendingCaptureCount} pending`,
    `${formatCompactMilliseconds(metrics.averageCaptureMilliseconds)} cap avg`,
    `${metrics.lastCapturedStoreCount} stores`,
    `${metrics.lastCapturedEntityRowCount} rows`,
  ].join(' · ');
}

function formatRecordingMetricsTitle(metrics: GameDebugDumpRecordingMetrics) {
  const allocated = metrics.lastCaptureAllocatedBytes === null
    ? 'n/a'
    : formatBytes(metrics.lastCaptureAllocatedBytes);
  return [
    `Published frames: ${metrics.publishedFrameCount}`,
    `Captured frames: ${metrics.capturedFrameCount}`,
    `Pending captures: ${metrics.pendingCaptureCount}`,
    `Frame callback last/max/avg: ${formatCompactMilliseconds(metrics.lastFrameCallbackMilliseconds)} / ${formatCompactMilliseconds(metrics.maxFrameCallbackMilliseconds)} / ${formatCompactMilliseconds(metrics.averageFrameCallbackMilliseconds)}`,
    `Capture last/max/avg: ${formatCompactMilliseconds(metrics.lastCaptureMilliseconds)} / ${formatCompactMilliseconds(metrics.maxCaptureMilliseconds)} / ${formatCompactMilliseconds(metrics.averageCaptureMilliseconds)}`,
    `Last copied stores/entity rows: ${metrics.lastCapturedStoreCount} / ${metrics.lastCapturedEntityRowCount}`,
    `Max copied stores/entity rows: ${metrics.maxCapturedStoreCount} / ${metrics.maxCapturedEntityRowCount}`,
    `Total copied stores/entity rows: ${metrics.totalCapturedStoreCount} / ${metrics.totalCapturedEntityRowCount}`,
    `Last capture allocated bytes: ${allocated}`,
  ].join('\n');
}

function formatCompactMilliseconds(value: number) {
  return `${value.toFixed(value < 10 ? 2 : 1)} ms`;
}

function areEntitySnapshotsEqual(left: EntityDebugSnapshot, right: EntityDebugSnapshot) {
  return left.id === right.id && left.version === right.version;
}

function areComponentSnapshotsEqual(left: ComponentDebugSnapshot, right: ComponentDebugSnapshot) {
  return (
    left.type.name === right.type.name &&
    left.type.fullName === right.type.fullName &&
    left.type.assemblyName === right.type.assemblyName &&
    left.value.format === right.value.format &&
    left.value.payload === right.value.payload &&
    left.value.error === right.value.error &&
    areDebugNodesEqual(left.value.structured, right.value.structured)
  );
}

function areDebugNodesEqual(
  left: ComponentValueDebugNode | null,
  right: ComponentValueDebugNode | null,
): boolean {
  if (left === right) {
    return true;
  }

  if (!left || !right) {
    return false;
  }

  return (
    left.kind === right.kind &&
    left.name === right.name &&
    left.editable === right.editable &&
    left.value === right.value &&
    left.error === right.error &&
    areDebugTypesEqual(left.type, right.type) &&
    areNullableDebugTypesEqual(left.elementType, right.elementType) &&
    areStringArraysEqual(left.options, right.options) &&
    left.children.length === right.children.length &&
    left.children.every((child, index) => areDebugNodesEqual(child, right.children[index])) &&
    areDebugNodesEqual(left.elementTemplate, right.elementTemplate)
  );
}

function areNullableDebugTypesEqual(left: DebugTypeInfo | null, right: DebugTypeInfo | null) {
  if (left === right) {
    return true;
  }

  if (!left || !right) {
    return false;
  }

  return areDebugTypesEqual(left, right);
}

function areDebugTypesEqual(left: DebugTypeInfo, right: DebugTypeInfo) {
  return (
    left.name === right.name &&
    left.fullName === right.fullName &&
    left.assemblyName === right.assemblyName
  );
}

function areStringArraysEqual(left: string[], right: string[]) {
  return left.length === right.length && left.every((value, index) => value === right[index]);
}

function validateDumpPlaybackManifest(dump: GameDebugDumpPlaybackManifest) {
  if (!dump || dump.format !== 'nkg.debug.dump' || !SUPPORTED_DUMP_DOCUMENT_VERSIONS.has(dump.version)) {
    throw new Error('Unsupported debug dump file.');
  }

  if (!Array.isArray(dump.frames) || dump.frames.length === 0) {
    throw new Error('Debug dump file does not contain frames.');
  }
}

function isFilePickerAbort(value: unknown) {
  return value instanceof DOMException && value.name === 'AbortError';
}

function isConsumedStepSnapshot(message: GameDebugSnapshotMessage, targetRevision: number) {
  return message.control.revision >= targetRevision && message.control.lastCommand === 'step-consumed';
}

function delay(milliseconds: number) {
  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, milliseconds);
  });
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

function readStoredDebugApiConnection(): DebugApiConnection {
  try {
    const raw = window.localStorage.getItem(DEBUG_API_CONNECTION_STORAGE_KEY);
    if (!raw) {
      return DEFAULT_DEBUG_API_CONNECTION;
    }

    const parsed = JSON.parse(raw) as Partial<DebugApiConnection>;
    return normalizeDebugApiConnection({
      host: typeof parsed.host === 'string' ? parsed.host : DEFAULT_DEBUG_API_CONNECTION.host,
      port: typeof parsed.port === 'string' ? parsed.port : DEFAULT_DEBUG_API_CONNECTION.port,
    }) ?? DEFAULT_DEBUG_API_CONNECTION;
  } catch {
    return DEFAULT_DEBUG_API_CONNECTION;
  }
}

function writeStoredDebugApiConnection(connection: DebugApiConnection) {
  try {
    window.localStorage.setItem(DEBUG_API_CONNECTION_STORAGE_KEY, JSON.stringify(connection));
  } catch {
    // Connection memory is best-effort.
  }
}

function normalizeDebugApiConnection(connection: DebugApiConnection): DebugApiConnection | null {
  const host = connection.host.trim();
  const portNumber = Number(connection.port.trim());
  if (!host || !Number.isInteger(portNumber) || portNumber < 1 || portNumber > 65535) {
    return null;
  }

  return {
    host,
    port: String(portNumber),
  };
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

function createEmptyInspectorPanel(): ComponentInspectorPanelState {
  return {
    id: DEFAULT_INSPECTOR_PANEL_ID,
    locked: false,
    target: null,
  };
}

function createInspectorPanelId(index: number) {
  return `inspector:${index}`;
}

function createInspectorTarget(
  entry: SceneEntry,
  entity: EntityDebugSnapshot,
  component: ComponentDebugSnapshot,
): ComponentInspectorTarget {
  return {
    worldName: entry.world.name,
    sceneName: entry.scene.name,
    entityId: entity.id,
    entityVersion: entity.version,
    componentTypeName: component.type.name,
    componentTypeFullName: component.type.fullName,
    componentAssemblyName: component.type.assemblyName,
    graphId: getComponentGraph(component).id,
  };
}

function createComponentDetailKey(target: ComponentInspectorTarget) {
  return [
    target.worldName,
    target.sceneName,
    target.entityId,
    target.componentAssemblyName,
    target.componentTypeFullName,
  ].join('\u0000');
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

function findComponentDetail(
  entity: EntityDebugSnapshot,
  target: ComponentInspectorTarget,
) {
  return entity.components.find((component) =>
    component.type.fullName === target.componentTypeFullName &&
    component.type.assemblyName === target.componentAssemblyName,
  ) ?? null;
}

function hasRecordedComponentValue(component: ComponentDebugSnapshot) {
  return Boolean(
    component.value.payload ||
    component.value.structured ||
    component.value.error,
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

function formatFps(value: number | null) {
  return value === null ? 'FPS -' : `${value.toFixed(value < 100 ? 1 : 0)} FPS`;
}

function formatMilliseconds(value: number | null) {
  return value === null ? 'Logic -' : `${value.toFixed(value < 10 ? 2 : 1)} ms`;
}

function formatSeconds(value: number) {
  if (value <= 0) {
    return '0s';
  }

  return `${value.toFixed(value < 10 ? 1 : 0)}s`;
}

function isPositiveFinite(value: number | null | undefined): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0;
}

function clamp(value: number, min: number, max: number) {
  if (!Number.isFinite(value)) {
    return min;
  }

  return Math.min(Math.max(value, min), max);
}
