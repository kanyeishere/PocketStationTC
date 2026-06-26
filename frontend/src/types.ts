export type TabKey = "chat" | "state" | "screen" | "live" | "commands";
export type ConnectionMode = "" | "online" | "offline";

export interface Envelope<TPayload = unknown> {
  v: number;
  id: string;
  type: string;
  payload: TPayload;
  timestamp?: number;
}

export interface CommandResult<TData = unknown> {
  ok: boolean;
  message: string;
  data?: TData;
}

export interface ChatEvent {
  sequence: number;
  channel: string;
  sender: string;
  message: string;
  timestamp: number;
}

export interface StatusEvent {
  statusId: number;
  remainingTime: number;
  param: number;
  sourceId: number;
}

export interface Vector3Like {
  x?: number;
  y?: number;
  z?: number;
  X?: number;
  Y?: number;
  Z?: number;
}

export interface CharacterState {
  name: string;
  objectId: number;
  entityId: number;
  classJobId: number;
  level: number;
  currentHp: number;
  maxHp: number;
  currentMp: number;
  maxMp: number;
  position?: Vector3Like;
  isDead: boolean;
  statuses: StatusEvent[];
}

export interface PlayerSnapshot {
  isLoggedIn: boolean;
  territoryType: number;
  mapId: number;
  localPlayer?: CharacterState | null;
  target?: CharacterState | null;
  party: CharacterState[];
  timestamp: number;
}

export interface ScreenshotReadyEvent {
  url: string;
  width: number;
  height: number;
  capturedAt: number;
  contentType: string;
}

export interface ChatFilterMode {
  id: string;
  name: string;
  isBuiltIn: boolean;
  enabledTypes: string[];
  includeKeywords: string[];
  excludeKeywords: string[];
}

export interface ChatFilterSettings {
  currentModeId: string;
  modes: ChatFilterMode[];
  allTypes: string[];
}

export interface HealthInfo {
  ok: boolean;
  lanEnabled: boolean;
  port: number;
  clients: number;
  urls: string[];
}

export interface StreamConfig {
  fps: number;
  running: boolean;
}
