import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Activity,
  Boxes,
  Bug,
  Clock3,
  Cpu,
  RefreshCw,
  Search,
  Shield,
  Sparkles,
  ToggleLeft,
  ToggleRight,
  Zap,
} from 'lucide-react';
import { fetchDebugSnapshot, postDebugMutation } from './api';
import type {
  BuffDebugSnapshot,
  ComponentDebugSnapshot,
  ComponentValueDebugSnapshot,
  EntityDebugSnapshot,
  GameDebugSnapshot,
  RuntimeContextDebugSnapshot,
  SceneDebugSnapshot,
  SkillDebugSnapshot,
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

type ComponentMutationExecutor = (
  entity: EntityDebugSnapshot,
  component: ComponentDebugSnapshot,
  value: ComponentValueDebugSnapshot,
) => Promise<void>;

export function App() {
  const [snapshot, setSnapshot] = useState<GameDebugSnapshot | null>(null);
  const [loadState, setLoadState] = useState<LoadState>('idle');
  const [error, setError] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [query, setQuery] = useState('');
  const [selection, setSelection] = useState<SceneSelection | null>(null);
  const [selectedEntityId, setSelectedEntityId] = useState<number | null>(null);

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

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);
    return () => controller.abort();
  }, [refresh]);

  useEffect(() => {
    if (!autoRefresh) {
      return;
    }

    const timer = window.setInterval(() => void refresh(), 1000);
    return () => window.clearInterval(timer);
  }, [autoRefresh, refresh]);

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
            onClick={() => setAutoRefresh((value) => !value)}
            title="Toggle auto refresh"
          >
            {autoRefresh ? <ToggleRight size={18} /> : <ToggleLeft size={18} />}
            Auto
          </button>
          <button className="primary-button" type="button" onClick={() => void refresh()}>
            <RefreshCw size={17} className={loadState === 'loading' ? 'spin' : undefined} />
            Refresh
          </button>
        </div>
      </header>

      <section className="summary-strip">
        <Metric icon={<Boxes size={18} />} label="Worlds" value={totals.worlds} />
        <Metric icon={<Activity size={18} />} label="Scenes" value={totals.scenes} />
        <Metric icon={<Cpu size={18} />} label="Entities" value={totals.entities} />
        <Metric icon={<Zap size={18} />} label="Skills" value={totals.skills} />
        <Metric icon={<Shield size={18} />} label="Buffs" value={totals.buffs} />
      </section>

      {error ? <div className="error-banner">{error}</div> : null}

      <section className="workspace">
        <aside className="sidebar">
          <PanelTitle icon={<Activity size={16} />} title="Runtime" />
          <RuntimeList runtimes={snapshot?.runtimes ?? []} />

          <PanelTitle icon={<Boxes size={16} />} title="Scenes" />
          <div className="scene-list">
            {scenes.map(({ world, scene }) => (
              <button
                key={`${world.name}:${scene.name}`}
              className={activeSceneEntry?.scene === scene ? 'scene-item active' : 'scene-item'}
                type="button"
                onClick={() => {
                  setSelection({ worldName: world.name, sceneName: scene.name });
                  setSelectedEntityId(null);
                }}
              >
                <span>{scene.name}</span>
                <small>{world.name}</small>
                <strong>{scene.entityCount}</strong>
              </button>
            ))}
          </div>
        </aside>

        <section className="entity-pane">
          <div className="pane-head">
            <div>
              <h2>{activeScene?.name ?? 'No scene'}</h2>
              <p>{filteredEntities.length} entities</p>
            </div>
            <div className="component-store-row">
              {(activeScene?.componentStores ?? []).map((store) => (
                <span key={store.type.fullName} className="type-pill">
                  {store.type.name} <b>{store.count}</b>
                </span>
              ))}
            </div>
          </div>

          <div className="entity-table" role="table">
            <div className="entity-row head" role="row">
              <span>ID</span>
              <span>Components</span>
              <span>Skills</span>
              <span>Buffs</span>
            </div>
            {filteredEntities.map((entity) => (
              <button
                key={`${entity.id}:${entity.version}`}
                className={entity.id === selectedEntity?.id ? 'entity-row active' : 'entity-row'}
                type="button"
                role="row"
                onClick={() => setSelectedEntityId(entity.id)}
              >
                <span>#{entity.id}</span>
                <span>{entity.components.map((component) => component.type.name).join(', ')}</span>
                <span>{entity.skills.length}</span>
                <span>{entity.buffs.length}</span>
              </button>
            ))}
          </div>
        </section>

        <aside className="detail-pane">
          {selectedEntity ? (
            <EntityDetails entity={selectedEntity} onSaveComponent={executeComponentMutation} />
          ) : (
            <EmptyDetails />
          )}
        </aside>
      </section>
    </main>
  );
}

function Metric({ icon, label, value }: { icon: React.ReactNode; label: string; value: number }) {
  return (
    <div className="metric">
      {icon}
      <span>{label}</span>
      <strong>{value}</strong>
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

function RuntimeList({ runtimes }: { runtimes: RuntimeContextDebugSnapshot[] }) {
  if (!runtimes.length) {
    return <p className="muted">No runtime</p>;
  }

  return (
    <div className="runtime-list">
      {runtimes.map((runtime) => (
        <div key={runtime.index} className="runtime-item">
          <div>
            <strong>Runtime {runtime.index + 1}</strong>
            <small>{runtime.modules.length} modules</small>
          </div>
          {runtime.procedureModules.map((procedureModule) => (
            <span key={procedureModule.type.fullName} className="status-chip">
              {procedureModule.currentProcedure ?? 'Idle'}
            </span>
          ))}
        </div>
      ))}
    </div>
  );
}

function EntityDetails({
  entity,
  onSaveComponent,
}: {
  entity: EntityDebugSnapshot;
  onSaveComponent: ComponentMutationExecutor;
}) {
  return (
    <div className="details">
      <div className="detail-heading">
        <div>
          <h2>Entity #{entity.id}</h2>
          <p>Version {entity.version}</p>
        </div>
      </div>

      <DetailSection icon={<Zap size={16} />} title="Skills" count={entity.skills.length}>
        {entity.skills.length ? (
          entity.skills.map((skill) => <SkillItem key={skill.id} skill={skill} />)
        ) : (
          <p className="muted">None</p>
        )}
      </DetailSection>

      <DetailSection icon={<Shield size={16} />} title="Buffs" count={entity.buffs.length}>
        {entity.buffs.length ? (
          entity.buffs.map((buff) => <BuffItem key={`${buff.id}:${buff.source.id}`} buff={buff} />)
        ) : (
          <p className="muted">None</p>
        )}
      </DetailSection>

      <DetailSection icon={<Sparkles size={16} />} title="Components" count={entity.components.length}>
        <div className="component-list">
          {entity.components.map((component) => (
            <ComponentEditor
              key={component.type.fullName}
              entity={entity}
              component={component}
              onSaveComponent={onSaveComponent}
            />
          ))}
        </div>
      </DetailSection>
    </div>
  );
}

function DetailSection({
  icon,
  title,
  count,
  children,
}: {
  icon: React.ReactNode;
  title: string;
  count: number;
  children: React.ReactNode;
}) {
  return (
    <section className="detail-section">
      <div className="section-title">
        {icon}
        <span>{title}</span>
        <b>{count}</b>
      </div>
      {children}
    </section>
  );
}

function SkillItem({ skill }: { skill: SkillDebugSnapshot }) {
  return (
    <div className="data-item skill">
      <div>
        <strong>{skill.displayName ?? skill.id}</strong>
        <small>
          Lv.{skill.level} · {skill.kind} · {skill.costKind} {skill.cost}
        </small>
      </div>
      <div className="inline-stats">
        <span>
          <Clock3 size={13} /> {formatSeconds(skill.cooldownRemainingSeconds)} / {formatSeconds(skill.cooldownSeconds)}
        </span>
        {skill.tags.map((tag) => (
          <em key={tag}>{tag}</em>
        ))}
      </div>
    </div>
  );
}

function BuffItem({ buff }: { buff: BuffDebugSnapshot }) {
  return (
    <div className="data-item buff">
      <div>
        <strong>{buff.displayName ?? buff.id}</strong>
        <small>
          Lv.{buff.level} · {buff.state} · stacks {buff.stacks}
        </small>
      </div>
      <div className="inline-stats">
        <span>{buff.remainingDurationSeconds === null ? 'forever' : formatSeconds(buff.remainingDurationSeconds)}</span>
        {buff.tags.map((tag) => (
          <em key={tag}>{tag}</em>
        ))}
      </div>
    </div>
  );
}

function ComponentEditor({
  entity,
  component,
  onSaveComponent,
}: {
  entity: EntityDebugSnapshot;
  component: ComponentDebugSnapshot;
  onSaveComponent: ComponentMutationExecutor;
}) {
  const [draft, setDraft] = useState(formatComponentValue(component.value));
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    setDraft(formatComponentValue(component.value));
  }, [component.value]);

  return (
    <details open>
      <summary>{component.type.name}</summary>
      <textarea
        value={draft}
        onChange={(event) => setDraft(event.target.value)}
        spellCheck={false}
      />
      <div className="action-row component-actions">
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
                payload: draft,
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
    </details>
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

function filterEntities(entities: EntityDebugSnapshot[], query: string) {
  const normalized = query.trim().toLowerCase();
  if (!normalized) {
    return entities;
  }

  return entities.filter((entity) => {
    const searchable = [
      `#${entity.id}`,
      String(entity.id),
      ...entity.components.map((component) => component.type.name),
      ...entity.skills.flatMap((skill) => [skill.id, skill.displayName ?? '', ...skill.tags]),
      ...entity.buffs.flatMap((buff) => [buff.id, buff.displayName ?? '', ...buff.tags]),
    ]
      .join(' ')
      .toLowerCase();

    return searchable.includes(normalized);
  });
}

function summarize(snapshot: GameDebugSnapshot | null) {
  const worlds = snapshot?.worlds.length ?? 0;
  let scenes = 0;
  let entities = 0;
  let skills = 0;
  let buffs = 0;

  for (const world of snapshot?.worlds ?? []) {
    scenes += world.scenes.length;
    for (const scene of world.scenes) {
      entities += scene.entities.length;
      for (const entity of scene.entities) {
        skills += entity.skills.length;
        buffs += entity.buffs.length;
      }
    }
  }

  return { worlds, scenes, entities, skills, buffs };
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
