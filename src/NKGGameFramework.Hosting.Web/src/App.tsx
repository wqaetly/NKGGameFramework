import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import {
  Background,
  Controls,
  Handle,
  MarkerType,
  Position,
  ReactFlow,
  type Edge,
  type Node,
  type NodeProps,
  type NodeTypes,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
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
import type {
  ComponentGraphDebugSnapshot,
  ComponentStoreDebugSnapshot,
  ComponentDebugSnapshot,
  ComponentValueDebugNode,
  ComponentValueDebugSnapshot,
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

type ComponentMutationExecutor = (
  entity: EntityDebugSnapshot,
  component: ComponentDebugSnapshot,
  value: ComponentValueDebugSnapshot,
) => Promise<void>;

type ComponentNodeData = {
  entity: EntityDebugSnapshot;
  component: ComponentDebugSnapshot;
  onSaveComponent: ComponentMutationExecutor;
};

type ComponentGroupNodeData = {
  label: string;
  count: number;
};

type ComponentFlowNode = Node<ComponentNodeData, 'componentNode'>;
type ComponentGroupFlowNode = Node<ComponentGroupNodeData, 'componentGroup'>;
type ComponentGraphFlowNode = ComponentFlowNode | ComponentGroupFlowNode;

type ComponentTreeNode = {
  component: ComponentDebugSnapshot;
  children: ComponentTreeNode[];
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

const componentNodeTypes = {
  componentNode: ComponentNode,
  componentGroup: ComponentGroupNode,
} satisfies NodeTypes;

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
          <ComponentGraphCanvas
            entity={entity}
            query={componentQuery}
            onSaveComponent={onSaveComponent}
          />
        ) : (
          <p className="muted">None</p>
        )}
      </div>
    </div>
  );
}

function ComponentGraphCanvas({
  entity,
  query,
  onSaveComponent,
}: {
  entity: EntityDebugSnapshot;
  query: string;
  onSaveComponent: ComponentMutationExecutor;
}) {
  const graph = useMemo(
    () => buildComponentGraph(
      {
        ...entity,
        components: filterComponents(entity.components, query),
      },
      onSaveComponent,
    ),
    [entity, query, onSaveComponent],
  );

  return (
    <div className="component-flow">
      {graph.nodes.length ? (
        <ReactFlow
          nodes={graph.nodes}
          edges={graph.edges}
          nodeTypes={componentNodeTypes}
          defaultEdgeOptions={{
            type: 'smoothstep',
            markerEnd: { type: MarkerType.ArrowClosed, color: '#d17a22' },
          }}
          fitView
          fitViewOptions={{ padding: 0.18 }}
          minZoom={0.35}
          maxZoom={1.25}
          nodesDraggable={false}
          nodesConnectable={false}
          elementsSelectable
          panOnScroll
          zoomOnPinch
          proOptions={{ hideAttribution: true }}
        >
          <Background color="#2f3135" gap={18} size={1} />
          <Controls showInteractive={false} />
        </ReactFlow>
      ) : (
        <div className="component-flow-empty">No matching components</div>
      )}
    </div>
  );
}

function buildComponentGraph(
  entity: EntityDebugSnapshot,
  onSaveComponent: ComponentMutationExecutor,
) {
  const componentById = new Map<string, ComponentTreeNode>();

  for (const component of entity.components) {
    const graph = getComponentGraph(component);
    componentById.set(graph.id, { component, children: [] });
  }

  const roots: ComponentTreeNode[] = [];
  for (const node of componentById.values()) {
    const graph = getComponentGraph(node.component);
    const parent = graph.parentId ? componentById.get(graph.parentId) : null;
    if (parent && parent !== node) {
      parent.children.push(node);
    } else {
      roots.push(node);
    }
  }

  const sortNodes = (nodes: ComponentTreeNode[]) => {
    nodes.sort(compareComponentNodes);
    for (const node of nodes) {
      sortNodes(node.children);
    }
  };
  sortNodes(roots);

  const groups = groupComponentRoots(roots);
  const nodes: ComponentGraphFlowNode[] = [];
  const edges: Edge[] = [];
  let y = 0;

  for (const group of groups) {
    const groupId = `group:${group.label}`;
    nodes.push({
      id: groupId,
      type: 'componentGroup',
      position: { x: 0, y },
      data: { label: group.label, count: group.roots.length },
      selectable: false,
      draggable: false,
    });

    let groupHeight = 0;
    let rootY = y;
    for (const root of group.roots) {
      const subtreeHeight = layoutComponentTree(
        root,
        0,
        rootY,
        entity,
        onSaveComponent,
        nodes,
        edges,
      );
      rootY += subtreeHeight + 34;
      groupHeight += subtreeHeight + 34;
    }

    y += Math.max(groupHeight, 80) + 44;
  }

  return {
    nodes,
    edges,
    height: Math.min(Math.max(y + 64, 280), 720),
  };
}

function layoutComponentTree(
  tree: ComponentTreeNode,
  depth: number,
  y: number,
  entity: EntityDebugSnapshot,
  onSaveComponent: ComponentMutationExecutor,
  nodes: ComponentGraphFlowNode[],
  edges: Edge[],
) {
  const graph = getComponentGraph(tree.component);
  const ownHeight = estimateComponentNodeHeight(tree.component);
  const childHeights = tree.children.map(estimateComponentSubtreeHeight);
  const childrenHeight =
    childHeights.reduce((total, height) => total + height, 0) +
    Math.max(0, tree.children.length - 1) * 28;
  const subtreeHeight = Math.max(ownHeight, childrenHeight);
  const nodeY = y + (subtreeHeight - ownHeight) / 2;

  nodes.push({
    id: graph.id,
    type: 'componentNode',
    position: { x: 150 + depth * 430, y: nodeY },
    data: { entity, component: tree.component, onSaveComponent },
    draggable: false,
  });

  let childY = y + Math.max(0, (subtreeHeight - childrenHeight) / 2);
  for (const [index, child] of tree.children.entries()) {
    const childGraph = getComponentGraph(child.component);
    layoutComponentTree(
      child,
      depth + 1,
      childY,
      entity,
      onSaveComponent,
      nodes,
      edges,
    );
    edges.push({
      id: `${graph.id}->${childGraph.id}`,
      source: graph.id,
      sourceHandle: 'out',
      target: childGraph.id,
      targetHandle: 'in',
      type: 'smoothstep',
      className: 'component-flow-edge',
      zIndex: 0,
    });
    childY += childHeights[index] + 28;
  }

  return subtreeHeight;
}

function groupComponentRoots(roots: ComponentTreeNode[]) {
  const groups = new Map<string, ComponentTreeNode[]>();
  for (const root of roots) {
    const graph = getComponentGraph(root.component);
    const label = graph.group ?? 'Components';
    groups.set(label, [...(groups.get(label) ?? []), root]);
  }

  return [...groups.entries()]
    .map(([label, groupRoots]) => ({
      label,
      roots: groupRoots,
      order: Math.min(...groupRoots.map((root) => getComponentGraph(root.component).order)),
    }))
    .sort((left, right) => left.order - right.order || left.label.localeCompare(right.label));
}

function compareComponentNodes(left: ComponentTreeNode, right: ComponentTreeNode) {
  const leftGraph = getComponentGraph(left.component);
  const rightGraph = getComponentGraph(right.component);
  return (
    leftGraph.order - rightGraph.order ||
    (leftGraph.group ?? '').localeCompare(rightGraph.group ?? '') ||
    left.component.type.name.localeCompare(right.component.type.name)
  );
}

function estimateComponentSubtreeHeight(tree: ComponentTreeNode): number {
  const ownHeight = estimateComponentNodeHeight(tree.component);
  if (!tree.children.length) {
    return ownHeight;
  }

  const childrenHeight =
    tree.children.reduce((total, child) => total + estimateComponentSubtreeHeight(child), 0) +
    Math.max(0, tree.children.length - 1) * 28;
  return Math.max(ownHeight, childrenHeight);
}

function estimateComponentNodeHeight(component: ComponentDebugSnapshot) {
  const structuredRows = component.value.structured ? countStructuredRows(component.value.structured) : 5;
  return 74 + Math.min(280, Math.max(64, structuredRows * 27));
}

function countStructuredRows(node: ComponentValueDebugNode): number {
  if (node.kind === 'object') {
    return Math.max(1, node.children.reduce((total, child) => total + countStructuredRows(child), 0));
  }

  if (node.kind === 'list') {
    return Math.max(2, node.children.reduce((total, child) => total + countStructuredRows(child), 1));
  }

  return 1;
}

function ComponentNode({ data, selected }: NodeProps<ComponentFlowNode>) {
  const { entity, component, onSaveComponent } = data;
  const [draftPayload, setDraftPayload] = useState(formatComponentValue(component.value));
  const [draftNode, setDraftNode] = useState(() => cloneDebugNode(component.value.structured));
  const [saving, setSaving] = useState(false);
  const graph = getComponentGraph(component);
  const initial = component.type.name.trim().charAt(0).toUpperCase() || '?';

  useEffect(() => {
    setDraftPayload(formatComponentValue(component.value));
    setDraftNode(cloneDebugNode(component.value.structured));
  }, [component.value]);

  return (
    <div className={selected ? 'component-flow-node selected' : 'component-flow-node'}>
      <Handle type="target" position={Position.Left} id="in" isConnectable={false} />
      <div className="component-node-corner">
        <span className={component.value.error ? 'component-node-status error' : 'component-node-status'} />
        <span className="component-node-format">{component.value.format}</span>
      </div>
      <div className="component-flow-node-head">
        <div className="component-node-icon" aria-hidden>
          {initial}
        </div>
        <div className="component-node-title">
          <strong>{component.type.name}</strong>
          <small>{graph.group ?? component.type.fullName}</small>
        </div>
      </div>
      {draftNode ? (
        <div className="component-flow-node-fields nodrag nopan">
          <StructuredValueEditor node={draftNode} onChange={setDraftNode} />
        </div>
      ) : (
        <textarea
          className="component-raw-editor nodrag nopan"
          value={draftPayload}
          onChange={(event) => setDraftPayload(event.target.value)}
          spellCheck={false}
        />
      )}
      <div className="action-row component-actions component-node-actions nodrag nopan">
        <span>{component.value.format}</span>
        <button
          className="mini-button"
          type="button"
          disabled={saving || component.value.error !== null}
          onClick={async () => {
            setSaving(true);
            try {
              await onSaveComponent(entity, component, {
                ...component.value,
                payload: draftNode ? component.value.payload : draftPayload,
                structured: draftNode,
                error: null,
              });
            } finally {
              setSaving(false);
            }
          }}
        >
          {saving ? 'Saving' : 'Save'}
        </button>
      </div>
      <Handle type="source" position={Position.Right} id="out" isConnectable={false} />
    </div>
  );
}

function ComponentGroupNode({ data }: NodeProps<ComponentGroupFlowNode>) {
  return (
    <div className="component-flow-group">
      <strong>{data.label}</strong>
      <span>{data.count}</span>
    </div>
  );
}

function StructuredValueEditor({
  node,
  onChange,
  depth = 0,
}: {
  node: ComponentValueDebugNode;
  onChange: (next: ComponentValueDebugNode) => void;
  depth?: number;
}) {
  if (node.kind === 'object') {
    return (
      <div className={depth === 0 ? 'structured-editor' : 'field-group'}>
        {depth > 0 ? <FieldHeader node={node} /> : null}
        {node.children.length ? (
          node.children.map((child, index) => (
            <StructuredValueEditor
              key={`${child.name ?? index}:${child.type.fullName}`}
              node={child}
              depth={depth + 1}
              onChange={(nextChild) => onChange(updateChild(node, index, nextChild))}
            />
          ))
        ) : (
          <span className="field-note">No public fields</span>
        )}
      </div>
    );
  }

  if (node.kind === 'list') {
    return <ListField node={node} onChange={onChange} depth={depth} />;
  }

  return <ScalarField node={node} onChange={onChange} />;
}

function ListField({
  node,
  onChange,
  depth,
}: {
  node: ComponentValueDebugNode;
  onChange: (next: ComponentValueDebugNode) => void;
  depth: number;
}) {
  const canAdd = node.editable && node.elementTemplate !== null;

  return (
    <div className="field-group list-field">
      <div className="field-header">
        <FieldHeader node={node} />
        <button
          className="mini-button"
          type="button"
          disabled={!canAdd}
          onClick={() => onChange(addListItem(node))}
        >
          Add
        </button>
      </div>
      {node.children.length ? (
        node.children.map((child, index) => (
          <div key={`${child.name ?? index}:${child.type.fullName}`} className="list-item">
            <StructuredValueEditor
              node={child}
              depth={depth + 1}
              onChange={(nextChild) => onChange(updateChild(node, index, nextChild))}
            />
            <button
              className="mini-button danger"
              type="button"
              disabled={!node.editable}
              onClick={() => onChange(removeListItem(node, index))}
            >
              Remove
            </button>
          </div>
        ))
      ) : (
        <span className="field-note">Empty list</span>
      )}
    </div>
  );
}

function ScalarField({
  node,
  onChange,
}: {
  node: ComponentValueDebugNode;
  onChange: (next: ComponentValueDebugNode) => void;
}) {
  const disabled = !node.editable;

  return (
    <label className={disabled ? 'field-row readonly' : 'field-row'}>
      <FieldHeader node={node} />
      {renderScalarControl(node, onChange, disabled)}
    </label>
  );
}

function FieldHeader({ node }: { node: ComponentValueDebugNode }) {
  return (
    <span className="field-label">
      <strong>{formatFieldName(node)}</strong>
      <small>{formatTypeName(node.type.name)}</small>
      {node.error ? <em>{node.error}</em> : null}
    </span>
  );
}

function renderScalarControl(
  node: ComponentValueDebugNode,
  onChange: (next: ComponentValueDebugNode) => void,
  disabled: boolean,
) {
  if (node.kind === 'boolean') {
    return (
      <input
        type="checkbox"
        checked={node.value === 'true'}
        disabled={disabled}
        onChange={(event) => onChange({ ...node, value: event.target.checked ? 'true' : 'false' })}
      />
    );
  }

  if (node.kind === 'integer' || node.kind === 'number') {
    return (
      <input
        type="number"
        step={node.kind === 'integer' ? 1 : 'any'}
        value={node.value ?? ''}
        disabled={disabled}
        onChange={(event) => onChange({ ...node, value: event.target.value })}
      />
    );
  }

  if (node.kind === 'enum') {
    return (
      <select
        value={node.value ?? ''}
        disabled={disabled}
        onChange={(event) => onChange({ ...node, value: event.target.value })}
      >
        {node.options.map((option) => (
          <option key={option} value={option}>
            {option}
          </option>
        ))}
      </select>
    );
  }

  if (node.kind === 'string' || node.kind === 'null') {
    return (
      <input
        type="text"
        value={node.value ?? ''}
        disabled={disabled || node.kind === 'null'}
        onChange={(event) => onChange({ ...node, value: event.target.value })}
      />
    );
  }

  return <span className="field-note">{node.error ?? node.kind}</span>;
}

function cloneDebugNode(node: ComponentValueDebugNode | null) {
  return node ? (JSON.parse(JSON.stringify(node)) as ComponentValueDebugNode) : null;
}

function updateChild(node: ComponentValueDebugNode, index: number, child: ComponentValueDebugNode) {
  return {
    ...node,
    children: node.children.map((candidate, candidateIndex) =>
      candidateIndex === index ? child : candidate,
    ),
  };
}

function addListItem(node: ComponentValueDebugNode) {
  if (!node.elementTemplate) {
    return node;
  }

  return {
    ...node,
    children: renameListChildren([
      ...node.children,
      {
        ...cloneDebugNode(node.elementTemplate)!,
        editable: true,
      },
    ]),
  };
}

function removeListItem(node: ComponentValueDebugNode, index: number) {
  return {
    ...node,
    children: renameListChildren(node.children.filter((_, candidateIndex) => candidateIndex !== index)),
  };
}

function renameListChildren(children: ComponentValueDebugNode[]) {
  return children.map((child, index) => ({
    ...child,
    name: `[${index}]`,
  }));
}

function formatFieldName(node: ComponentValueDebugNode) {
  return node.name ?? node.type.name;
}

function formatTypeName(typeName: string) {
  const tickIndex = typeName.indexOf('`');
  return tickIndex >= 0 ? typeName.slice(0, tickIndex) : typeName;
}

function getComponentGraph(component: ComponentDebugSnapshot): ComponentGraphDebugSnapshot {
  const graph = (component as ComponentDebugSnapshot & { graph?: ComponentGraphDebugSnapshot }).graph;
  return {
    id: graph?.id ?? `${component.type.assemblyName}:${component.type.fullName}`,
    parentId: graph?.parentId ?? null,
    parentType: graph?.parentType ?? null,
    group: graph?.group ?? null,
    order: graph?.order ?? 0,
  };
}

function countComponentGroups(components: ComponentDebugSnapshot[]) {
  return new Set(components.map((component) => getComponentGraph(component).group ?? 'Components')).size;
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

function filterComponents(components: ComponentDebugSnapshot[], query: string) {
  return components.filter((component) => {
    const graph = getComponentGraph(component);
    return matchesQuery(
      [
        component.type.name,
        component.type.fullName,
        component.value.format,
        component.value.payload ?? '',
        graph.group ?? '',
        graph.parentType?.name ?? '',
        graph.parentType?.fullName ?? '',
      ],
      query,
    );
  });
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

function formatComponentValue(value: ComponentValueDebugSnapshot) {
  if (value.error) {
    return `${value.format} error: ${value.error}`;
  }

  if (!value.payload) {
    return `${value.format}: <empty>`;
  }

  try {
    return JSON.stringify(JSON.parse(value.payload), null, 2);
  } catch {
    return value.payload;
  }
}
