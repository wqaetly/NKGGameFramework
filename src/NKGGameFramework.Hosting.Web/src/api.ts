import type {
  GameDebugControlRequest,
  GameDebugControlResult,
  ComponentDebugSnapshot,
  GameDebugDumpPlaybackComponentRequest,
  GameDebugDumpPlaybackManifest,
  GameDebugDumpPlaybackOpenRequest,
  GameDebugDumpAnalysisReport,
  GameDebugDumpRecordingRequest,
  GameDebugDumpRecordingResult,
  GameDebugDumpRecordingState,
  GameDebugMutationRequest,
  GameDebugMutationResult,
  GameDebugSnapshotMessage,
} from './types';

export type DebugApiConnection = {
  host: string;
  port: string;
};

export interface DebugSnapshotRequestOptions {
  profile?: DebugSnapshotCaptureProfile;
  worldName?: string;
  sceneName?: string;
  entityId?: number;
  componentTypeFullName?: string;
  componentAssemblyName?: string;
  entityOffset?: number;
  entityLimit?: number;
  includePayload?: boolean;
  includeStructured?: boolean;
  waitForFrame?: boolean;
}

export type DebugSnapshotCaptureProfile =
  | 'livePreview'
  | 'stepEditable'
  | 'singleFramePreview'
  | 'dumpRecording'
  | 'dumpPlaybackPreview';

export const DEFAULT_DEBUG_API_CONNECTION: DebugApiConnection = {
  host: '127.0.0.1',
  port: '5067',
};

let debugApiBaseUrl = createDebugApiBaseUrl(DEFAULT_DEBUG_API_CONNECTION);

export function setDebugApiBaseUrl(baseUrl: string) {
  debugApiBaseUrl = normalizeDebugApiBaseUrl(baseUrl);
}

export function createDebugApiBaseUrl(connection: DebugApiConnection) {
  const host = connection.host.trim() || DEFAULT_DEBUG_API_CONNECTION.host;
  const port = connection.port.trim() || DEFAULT_DEBUG_API_CONNECTION.port;
  return normalizeDebugApiBaseUrl(`http://${formatHostForUrl(host)}:${port}`);
}

export async function fetchDebugSnapshotMessage(
  signal?: AbortSignal,
  options?: DebugSnapshotRequestOptions,
): Promise<GameDebugSnapshotMessage> {
  const url = createDebugApiUrl('/_nkg/debug/snapshot');
  appendQuery(url, 'profile', options?.profile);
  appendQuery(url, 'worldName', options?.worldName);
  appendQuery(url, 'sceneName', options?.sceneName);
  appendQuery(url, 'entityId', options?.entityId);
  appendQuery(url, 'componentTypeFullName', options?.componentTypeFullName);
  appendQuery(url, 'componentAssemblyName', options?.componentAssemblyName);
  appendQuery(url, 'entityOffset', options?.entityOffset);
  appendQuery(url, 'entityLimit', options?.entityLimit);
  appendQuery(url, 'includePayload', options?.includePayload);
  appendQuery(url, 'includeStructured', options?.includeStructured);
  appendQuery(url, 'waitForFrame', options?.waitForFrame);

  const response = await fetch(url.toString(), {
    signal,
    headers: {
      Accept: 'application/json',
    },
  });

  if (!response.ok) {
    throw new Error(`Snapshot request failed: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

export function createDebugSnapshotStream(options?: DebugSnapshotRequestOptions): EventSource {
  const url = createDebugApiUrl('/_nkg/debug/stream');
  appendQuery(url, 'profile', options?.profile);
  appendQuery(url, 'worldName', options?.worldName);
  appendQuery(url, 'sceneName', options?.sceneName);
  appendQuery(url, 'entityId', options?.entityId);
  appendQuery(url, 'componentTypeFullName', options?.componentTypeFullName);
  appendQuery(url, 'componentAssemblyName', options?.componentAssemblyName);
  appendQuery(url, 'entityOffset', options?.entityOffset);
  appendQuery(url, 'entityLimit', options?.entityLimit);
  appendQuery(url, 'includePayload', options?.includePayload);
  appendQuery(url, 'includeStructured', options?.includeStructured);
  appendQuery(url, 'waitForFrame', options?.waitForFrame);
  return new EventSource(url.toString());
}

export interface DebugSnapshotStreamWaiter {
  opened: Promise<void>;
  message: Promise<GameDebugSnapshotMessage>;
  close: () => void;
}

export function waitForDebugSnapshotStreamMessage(
  signal?: AbortSignal,
  options?: DebugSnapshotRequestOptions,
): DebugSnapshotStreamWaiter {
  const stream = createDebugSnapshotStream(options);
  let openedSettled = false;
  let messageSettled = false;
  let resolveOpened!: () => void;
  let rejectOpened!: (reason?: unknown) => void;
  let resolveMessage!: (message: GameDebugSnapshotMessage) => void;
  let rejectMessage!: (reason?: unknown) => void;

  const opened = new Promise<void>((resolve, reject) => {
    resolveOpened = resolve;
    rejectOpened = reject;
  });
  const message = new Promise<GameDebugSnapshotMessage>((resolve, reject) => {
    resolveMessage = resolve;
    rejectMessage = reject;
  });

  const cleanup = () => {
    stream.removeEventListener('open', handleOpen);
    stream.removeEventListener('snapshot', handleSnapshot);
    stream.removeEventListener('error', handleError);
    signal?.removeEventListener('abort', handleAbort);
  };
  const close = () => {
    cleanup();
    stream.close();
  };
  const rejectPending = (reason: unknown) => {
    if (!openedSettled) {
      openedSettled = true;
      rejectOpened(reason);
    }

    if (!messageSettled) {
      messageSettled = true;
      rejectMessage(reason);
    }
  };

  function handleOpen() {
    if (!openedSettled) {
      openedSettled = true;
      resolveOpened();
    }
  }

  function handleSnapshot(event: Event) {
    if (messageSettled) {
      return;
    }

    messageSettled = true;
    resolveMessage(JSON.parse((event as MessageEvent<string>).data) as GameDebugSnapshotMessage);
    close();
  }

  function handleError() {
    close();
    rejectPending(new Error('Debug frame stream disconnected before a snapshot was received.'));
  }

  function handleAbort() {
    close();
    rejectPending(new DOMException('Debug frame stream wait was aborted.', 'AbortError'));
  }

  stream.addEventListener('open', handleOpen);
  stream.addEventListener('snapshot', handleSnapshot);
  stream.addEventListener('error', handleError);

  if (signal?.aborted) {
    handleAbort();
  } else {
    signal?.addEventListener('abort', handleAbort, { once: true });
  }

  return { opened, message, close };
}

function appendQuery(url: URL, key: string, value: string | number | boolean | undefined) {
  if (value !== undefined) {
    url.searchParams.set(key, String(value));
  }
}

export async function postDebugMutation(
  request: GameDebugMutationRequest,
  signal?: AbortSignal,
): Promise<GameDebugMutationResult> {
  const response = await fetch(createDebugApiUrl('/_nkg/debug/mutations').toString(), {
    method: 'POST',
    signal,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`Mutation request failed: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

export async function postDebugControl(
  request: GameDebugControlRequest,
  signal?: AbortSignal,
): Promise<GameDebugControlResult> {
  const response = await fetch(createDebugApiUrl('/_nkg/debug/control').toString(), {
    method: 'POST',
    signal,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`Control request failed: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

export async function fetchDumpRecordingState(
  signal?: AbortSignal,
): Promise<GameDebugDumpRecordingState> {
  const response = await fetch(createDebugApiUrl('/_nkg/debug/dump/recording').toString(), {
    signal,
    headers: {
      Accept: 'application/json',
    },
  });

  if (!response.ok) {
    throw new Error(`Dump recording state request failed: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

export async function postDumpRecording(
  request: GameDebugDumpRecordingRequest,
  signal?: AbortSignal,
): Promise<GameDebugDumpRecordingResult> {
  const response = await fetch(createDebugApiUrl('/_nkg/debug/dump/recording').toString(), {
    method: 'POST',
    signal,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`Dump recording request failed: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

export async function openDumpPlayback(
  request: GameDebugDumpPlaybackOpenRequest,
  signal?: AbortSignal,
): Promise<GameDebugDumpPlaybackManifest> {
  const response = await fetch(createDebugApiUrl('/_nkg/debug/dump/playback').toString(), {
    method: 'POST',
    signal,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`Dump playback request failed: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

export async function uploadDumpPlayback(
  payload: ArrayBuffer,
  signal?: AbortSignal,
): Promise<GameDebugDumpPlaybackManifest> {
  const response = await fetch(createDebugApiUrl('/_nkg/debug/dump/playback/upload').toString(), {
    method: 'POST',
    signal,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/octet-stream',
    },
    body: payload,
  });

  if (!response.ok) {
    throw new Error(`Dump playback upload failed: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

export async function openDumpAnalysis(
  request: GameDebugDumpPlaybackOpenRequest,
  signal?: AbortSignal,
): Promise<GameDebugDumpAnalysisReport> {
  const response = await fetch(createDebugApiUrl('/_nkg/debug/dump/analysis').toString(), {
    method: 'POST',
    signal,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`Dump analysis request failed: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

export async function uploadDumpAnalysis(
  payload: ArrayBuffer,
  signal?: AbortSignal,
): Promise<GameDebugDumpAnalysisReport> {
  const response = await fetch(createDebugApiUrl('/_nkg/debug/dump/analysis/upload').toString(), {
    method: 'POST',
    signal,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/octet-stream',
    },
    body: payload,
  });

  if (!response.ok) {
    throw new Error(`Dump analysis upload failed: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

export async function fetchDumpPlaybackFrame(
  playbackId: string,
  frameIndex: number,
  signal?: AbortSignal,
): Promise<GameDebugSnapshotMessage> {
  const url = createDebugApiUrl('/_nkg/debug/dump/playback/frame');
  appendQuery(url, 'playbackId', playbackId);
  appendQuery(url, 'frameIndex', frameIndex);

  const response = await fetch(url.toString(), {
    signal,
    headers: {
      Accept: 'application/json',
    },
  });

  if (!response.ok) {
    throw new Error(`Dump playback frame request failed: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

export async function fetchDumpPlaybackComponent(
  request: GameDebugDumpPlaybackComponentRequest,
  signal?: AbortSignal,
): Promise<ComponentDebugSnapshot> {
  const url = createDebugApiUrl('/_nkg/debug/dump/playback/component');
  appendQuery(url, 'playbackId', request.playbackId ?? undefined);
  appendQuery(url, 'frameIndex', request.frameIndex);
  appendQuery(url, 'worldName', request.worldName);
  appendQuery(url, 'sceneName', request.sceneName);
  appendQuery(url, 'entityId', request.entityId);
  appendQuery(url, 'componentTypeFullName', request.componentTypeFullName);
  appendQuery(url, 'componentAssemblyName', request.componentAssemblyName ?? undefined);

  const response = await fetch(url.toString(), {
    signal,
    headers: {
      Accept: 'application/json',
    },
  });

  if (!response.ok) {
    throw new Error(`Dump playback component request failed: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

function createDebugApiUrl(path: string) {
  return new URL(path, debugApiBaseUrl);
}

function normalizeDebugApiBaseUrl(value: string) {
  const url = new URL(value);
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new Error('Debug host URL must use http or https.');
  }

  url.pathname = '/';
  url.search = '';
  url.hash = '';
  return url.toString();
}

function formatHostForUrl(host: string) {
  if (host.includes(':') && !host.startsWith('[') && !host.endsWith(']')) {
    return `[${host}]`;
  }

  return host;
}
