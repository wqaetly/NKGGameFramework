import type {
  GameDebugControlRequest,
  GameDebugControlResult,
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
  return new EventSource(`${url.pathname}${url.search}`);
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
