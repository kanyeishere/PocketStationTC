import type { Envelope } from "@/types";

const tokenStorageKey = "pocket.station.token";

export const token =
  new URLSearchParams(window.location.search).get("token") ||
  window.localStorage.getItem(tokenStorageKey) ||
  "";

if (token) {
  window.localStorage.setItem(tokenStorageKey, token);
}

export function apiUrl(path: string): string {
  const url = new URL(path, window.location.origin);
  if (token) {
    url.searchParams.set("token", token);
  }

  return url.toString();
}

export function imageApiUrl(path: string): string {
  const url = new URL(apiUrl(path));
  url.searchParams.set("t", String(Date.now()));
  return url.toString();
}

export function websocketUrl(): string {
  const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  const url = new URL(`${protocol}//${window.location.host}/ws`);
  if (token) {
    url.searchParams.set("token", token);
  }

  return url.toString();
}

export async function apiFetch(path: string, options: RequestInit = {}): Promise<Response> {
  const headers = new Headers(options.headers || {});
  if (token) {
    headers.set("X-Pocket-Token", token);
  }

  return fetch(apiUrl(path), { ...options, headers });
}

export async function getJson<T>(path: string): Promise<T> {
  const response = await apiFetch(path);
  return readJsonResponse<T>(response);
}

export async function postJson<T>(path: string, body: unknown): Promise<T> {
  const response = await apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });

  return readJsonResponse<T>(response);
}

export function createEnvelope(type: string, payload: unknown = {}): Envelope {
  return {
    v: 1,
    id: createId(),
    type,
    payload
  };
}

async function readJsonResponse<T>(response: Response): Promise<T> {
  const data = await response.json();
  if (!response.ok) {
    const message = data?.message || data?.error || response.statusText || "Request failed";
    throw new Error(message);
  }

  return data as T;
}

function createId(): string {
  if (window.crypto?.randomUUID) {
    return window.crypto.randomUUID();
  }

  return `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
