import type {
  GameDebugControlRequest,
  GameDebugControlResult,
  GameDebugDumpPlaybackManifest,
  GameDebugDumpPlaybackOpenRequest,
  GameDebugDumpRecordingRequest,
  GameDebugDumpRecordingResult,
  GameDebugDumpRecordingState,
  GameDebugMutationRequest,
  GameDebugMutationResult,
  GameDebugSnapshotMessage,
} from './types';

export interface DebugSnapshotRequestOptions {
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

export async function fetchDebugSnapshotMessage(
  signal?: AbortSignal,
  options?: DebugSnapshotRequestOptions,
): Promise<GameDebugSnapshotMessage> {
  const url = new URL('/_nkg/debug/snapshot', window.location.origin);
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

  const response = await fetch(`${url.pathname}${url.search}`, {
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
  const url = new URL('/_nkg/debug/stream', window.location.origin);
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
  return new EventSource(`${url.pathname}${url.search}`);
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
  const response = await fetch('/_nkg/debug/mutations', {
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
  const response = await fetch('/_nkg/debug/control', {
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
  const response = await fetch('/_nkg/debug/dump/recording', {
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
  const response = await fetch('/_nkg/debug/dump/recording', {
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
  const response = await fetch('/_nkg/debug/dump/playback', {
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
  const response = await fetch('/_nkg/debug/dump/playback/upload', {
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

export async function fetchDumpPlaybackFrame(
  playbackId: string,
  frameIndex: number,
  signal?: AbortSignal,
): Promise<GameDebugSnapshotMessage> {
  const url = new URL('/_nkg/debug/dump/playback/frame', window.location.origin);
  appendQuery(url, 'playbackId', playbackId);
  appendQuery(url, 'frameIndex', frameIndex);

  const response = await fetch(`${url.pathname}${url.search}`, {
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
