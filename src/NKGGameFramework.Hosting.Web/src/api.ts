import type { GameDebugMutationRequest, GameDebugMutationResult, GameDebugSnapshot } from './types';

const apiBase = import.meta.env.VITE_NKG_DEBUG_API_BASE ?? '';

export async function fetchDebugSnapshot(signal?: AbortSignal): Promise<GameDebugSnapshot> {
  const response = await fetch(`${apiBase}/_nkg/debug/snapshot`, {
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
