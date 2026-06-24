import type {
  GameDebugControlRequest,
  GameDebugControlResult,
  GameDebugControlState,
  GameDebugMutationRequest,
  GameDebugMutationResult,
  GameDebugSnapshot,
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

export async function fetchDebugSnapshot(
  signal?: AbortSignal,
  options?: DebugSnapshotRequestOptions,
): Promise<GameDebugSnapshot> {
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

export async function fetchDebugControl(signal?: AbortSignal): Promise<GameDebugControlState> {
  const response = await fetch(`${apiBase}/_nkg/debug/control`, {
    signal,
    headers: {
      Accept: 'application/json',
    },
  });

  if (!response.ok) {
    throw new Error(`Control request failed: ${response.status} ${response.statusText}`);
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
