import { createContext, lazy, Suspense, useCallback, useContext, useEffect, useMemo, useState } from 'react';
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
  Pause,
  Play,
  RefreshCw,
  Search,
  SkipForward,
  Sparkles,
} from 'lucide-react';
import { fetchDebugControl, fetchDebugSnapshot, postDebugControl, postDebugMutation } from './api';
import { countComponentGroups, type ComponentMutationExecutor } from './componentGraphModel';
import type {
  ComponentStoreDebugSnapshot,
  EntityDebugSnapshot,
  GameDebugControlCommand,
  GameDebugControlState,
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
  selectScene: (entry: SceneEntry) => void;
  selectEntity: (entityId: number) => void;
  onSaveComponent: ComponentMutationExecutor;
};

type DockPanelProps = IDockviewPanelProps<Record<string, never>>;

const DOCK_LAYOUT_STORAGE_KEY = 'nkg.webdebug.layout.v3';
const LEGACY_DOCK_LAYOUT_STORAGE_KEYS = ['nkg.webdebug.layout.v1', 'nkg.webdebug.layout.v2'];

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
  const [error, setError] = useState<string | null>(null);
  const [control, setControl] = useState<GameDebugControlState | null>(null);
  const [query, setQuery] = useState('');
  const [selection, setSelection] = useState<SceneSelection | null>(null);
  const [selectedEntityId, setSelectedEntityId] = useState<number | null>(null);
  const [dockRevision, setDockRevision] = useState(0);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    setLoadState((current) => (current === 'ready' ? current : 'loading'));
    setError(null);

    try {
      const next = await fetchDebugSnapshot(signal);
      setSnapshot(next);
      setLoadState('ready');
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === 'AbortError') {
        return;
      }

      setError(caught instanceof Error ? caught.message : String(caught));
      setLoadState('error');
    }
  }, []);

  const refreshControl = useCallback(async (signal?: AbortSignal) => {
    try {
      const next = await fetchDebugControl(signal);
      setControl(next);
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === 'AbortError') {
        return;
      }

      setError(caught instanceof Error ? caught.message : String(caught));
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);
    void refreshControl(controller.signal);
    return () => controller.abort();
  }, [refresh, refreshControl]);

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

  const selectedEntity = useMemo(() => {
    if (!filteredEntities.length) {
      return null;
    }

    return filteredEntities.find((entity) => entity.id === selectedEntityId) ?? filteredEntities[0];
  }, [filteredEntities, selectedEntityId]);

  useEffect(() => {
    if (selectedEntity && selectedEntity.id !== selectedEntityId) {
      setSelectedEntityId(selectedEntity.id);
    }
  }, [selectedEntity, selectedEntityId]);

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

      await refresh();
    },
    [activeSceneEntry, refresh],
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
      selectScene,
      selectEntity,
      executeComponentMutation,
    ],
  );

  const executeControl = useCallback(
    async (command: GameDebugControlCommand) => {
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
    [],
  );

  return (
    <main className="app-shell">
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
            {control?.pendingStepCount ? <b>{control.pendingStepCount}</b> : null}
          </button>
          <button className="primary-button" type="button" onClick={() => void refresh()}>
            <RefreshCw size={17} className={loadState === 'loading' ? 'spin' : undefined} />
            Refresh
          </button>
          <button className="icon-button" type="button" onClick={resetDockLayout} title="Reset layout">
            <Boxes size={17} />
            Layout
          </button>
        </div>
      </header>

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
          onSaveComponent={model.onSaveComponent}
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
        <span>ID</span>
        <span>Components</span>
        <span>Groups</span>
        <span>Ver</span>
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
        <PanelTitle icon={<Sparkles size={14} />} title="Component Stores" />
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
  onSaveComponent,
}: {
  entity: EntityDebugSnapshot;
  componentQuery: string;
  onComponentQueryChange: (value: string) => void;
  onSaveComponent: ComponentMutationExecutor;
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
              onSaveComponent={onSaveComponent}
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
