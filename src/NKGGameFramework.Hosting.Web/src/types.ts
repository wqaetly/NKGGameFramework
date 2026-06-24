export interface GameDebugSnapshot {
  capturedAt: string;
  runtimes: RuntimeContextDebugSnapshot[];
  worlds: WorldDebugSnapshot[];
}

export interface GameDebugFrameInfo {
  sequence: number;
  source: string;
  frame: number;
  capturedAt: string;
}

export interface GameDebugSnapshotMessage {
  frame: GameDebugFrameInfo;
  snapshot: GameDebugSnapshot;
  control: GameDebugControlState;
}

export interface RuntimeContextDebugSnapshot {
  index: number;
  isDisposed: boolean;
  modules: ModuleDebugSnapshot[];
  procedureModules: ProcedureModuleDebugSnapshot[];
}

export interface ModuleDebugSnapshot {
  type: DebugTypeInfo;
  priority: number;
  isUpdateModule: boolean;
}

export interface ProcedureModuleDebugSnapshot {
  type: DebugTypeInfo;
  isInitialized: boolean;
  currentProcedure: string | null;
  currentProcedureTime: number;
  procedures: ProcedureDebugSnapshot[];
}

export interface ProcedureDebugSnapshot {
  type: DebugTypeInfo;
  isCurrent: boolean;
}

export interface WorldDebugSnapshot {
  name: string;
  sceneCount: number;
  scenes: SceneDebugSnapshot[];
}

export interface SceneDebugSnapshot {
  name: string;
  entityCount: number;
  systems: SystemDebugSnapshot[];
  componentStores: ComponentStoreDebugSnapshot[];
  entities: EntityDebugSnapshot[];
}

export interface SystemDebugSnapshot {
  type: DebugTypeInfo;
  order: number;
  enabled: boolean;
}

export interface ComponentStoreDebugSnapshot {
  type: DebugTypeInfo;
  count: number;
  entityIds: number[];
}

export interface EntityDebugSnapshot {
  id: number;
  version: number;
  components: ComponentDebugSnapshot[];
  skills: SkillDebugSnapshot[];
  buffs: BuffDebugSnapshot[];
}

export interface ComponentDebugSnapshot {
  type: DebugTypeInfo;
  value: ComponentValueDebugSnapshot;
  graph: ComponentGraphDebugSnapshot;
}

export interface ComponentGraphDebugSnapshot {
  id: string;
  parentId: string | null;
  parentType: DebugTypeInfo | null;
  group: string | null;
  order: number;
}

export interface ComponentValueDebugSnapshot {
  format: string;
  payload: string | null;
  error: string | null;
  structured: ComponentValueDebugNode | null;
}

export type ComponentValueDebugNodeKind =
  | 'boolean'
  | 'integer'
  | 'number'
  | 'string'
  | 'enum'
  | 'object'
  | 'list'
  | 'null'
  | 'reference'
  | 'unsupported';

export interface ComponentValueDebugNode {
  kind: ComponentValueDebugNodeKind;
  name: string | null;
  type: DebugTypeInfo;
  editable: boolean;
  value: string | null;
  children: ComponentValueDebugNode[];
  options: string[];
  elementType: DebugTypeInfo | null;
  elementTemplate: ComponentValueDebugNode | null;
  error: string | null;
}

export interface SkillDebugSnapshot {
  id: string;
  displayName: string | null;
  level: number;
  kind: string;
  releaseMode: string;
  costKind: string;
  cost: number;
  cooldownSeconds: number;
  cooldownRemainingSeconds: number;
  isCoolingDown: boolean;
  tags: string[];
  resourceLocations: string[];
  effectKeys: string[];
}

export interface BuffDebugSnapshot {
  id: string;
  displayName: string | null;
  level: number;
  stacks: number;
  state: string;
  kind: string;
  effectKey: string;
  remainingDurationSeconds: number | null;
  source: EntityRefDebugSnapshot;
  target: EntityRefDebugSnapshot;
  tags: string[];
}

export interface EntityRefDebugSnapshot {
  id: number;
  version: number;
  isAlive: boolean;
}

export interface DebugTypeInfo {
  name: string;
  fullName: string;
  assemblyName: string;
}

export interface GameDebugMutationRequest {
  worldName: string;
  sceneName: string;
  entityId: number;
  entityVersion: number | null;
  componentTypeFullName: string;
  componentAssemblyName: string;
  value: ComponentValueDebugSnapshot;
}

export interface GameDebugMutationResult {
  succeeded: boolean;
  message: string;
}

export type GameDebugControlCommand = 'play' | 'pause' | 'step';

export interface GameDebugControlRequest {
  command: GameDebugControlCommand;
  stepCount?: number | null;
}

export interface GameDebugControlState {
  isPaused: boolean;
  pendingStepCount: number;
  revision: number;
  lastCommand: string | null;
}

export interface GameDebugControlResult {
  succeeded: boolean;
  message: string;
  state: GameDebugControlState;
}

export interface GameDebugDumpDocument {
  format: string;
  version: number;
  name: string;
  createdAt: string;
  startedAt: string;
  endedAt: string;
  maxFrames?: number;
  droppedFrameCount?: number;
  frames: GameDebugSnapshotMessage[];
}

export type GameDebugDumpRecordingCommand = 'start' | 'stop';

export interface GameDebugDumpRecordingRequest {
  command: GameDebugDumpRecordingCommand;
  name?: string | null;
}

export interface GameDebugDumpRecordingState {
  isRecording: boolean;
  startedAt: string | null;
  frameCount: number;
  maxFrames: number;
  droppedFrameCount: number;
  lastDumpName: string | null;
  lastDumpPath: string | null;
}

export interface GameDebugDumpRecordingResult {
  succeeded: boolean;
  message: string;
  state: GameDebugDumpRecordingState;
  dump: GameDebugDumpDocument | null;
}
