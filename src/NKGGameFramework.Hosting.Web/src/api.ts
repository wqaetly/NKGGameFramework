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

const apiBase = import.meta.env.VITE_NKG_DEBUG_API_BASE ?? '';

export interface DebugSnapshotRequestOptions {
  worldName?: string;
  sceneName?: string;
  entityId?: number;
  entityOffset?: number;
  entityLimit?: number;
  includePayload?: boolean;
  includeStructured?: boolean;
}

export async function fetchDebugSnapshotMessage(
  signal?: AbortSignal,
  options?: DebugSnapshotRequestOptions,
): Promise<GameDebugSnapshotMessage> {
  const url = new URL(`${apiBase}/_nkg/debug/snapshot`, window.location.origin);
  appendQuery(url, 'worldName', options?.worldName);
  appendQuery(url, 'sceneName', options?.sceneName);
  appendQuery(url, 'entityId', options?.entityId);
  appendQuery(url, 'entityOffset', options?.entityOffset);
  appendQuery(url, 'entityLimit', options?.entityLimit);
  appendQuery(url, 'includePayload', options?.includePayload);
  appendQuery(url, 'includeStructured', options?.includeStructured);

  const response = await fetch(toFetchUrl(url), {
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
  const url = new URL(`${apiBase}/_nkg/debug/stream`, window.location.origin);
  appendQuery(url, 'worldName', options?.worldName);
  appendQuery(url, 'sceneName', options?.sceneName);
  appendQuery(url, 'entityId', options?.entityId);
  appendQuery(url, 'entityOffset', options?.entityOffset);
  appendQuery(url, 'entityLimit', options?.entityLimit);
  appendQuery(url, 'includePayload', options?.includePayload);
  appendQuery(url, 'includeStructured', options?.includeStructured);
  return new EventSource(toFetchUrl(url));
}

function appendQuery(url: URL, key: string, value: string | number | boolean | undefined) {
  if (value !== undefined) {
    url.searchParams.set(key, String(value));
  }
}

function toFetchUrl(url: URL) {
  return apiBase ? url.toString() : `${url.pathname}${url.search}`;
}

export async function postDebugMutation(
  request: GameDebugMutationRequest,
  signal?: AbortSignal,
): Promise<GameDebugMutationResult> {
  const response = await fetch(`${apiBase}/_nkg/debug/mutations`, {
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
  const response = await fetch(`${apiBase}/_nkg/debug/control`, {
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
  const response = await fetch(`${apiBase}/_nkg/debug/dump/recording`, {
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
  const response = await fetch(`${apiBase}/_nkg/debug/dump/recording`, {
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
