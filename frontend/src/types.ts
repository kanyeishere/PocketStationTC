export type TabKey = "chat" | "state" | "shortcuts" | "live";
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
  classJobName: string;
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
  territoryName: string;
  worldName: string;
  dataCenterName: string;
  localPlayer?: CharacterState | null;
  target?: CharacterState | null;
  party: CharacterState[];
  timestamp: number;
  currencies?: CurrencyInfo[] | null;
}

export interface CurrencyInfo {
  itemId: number;
  name: string;
  count: number;
  iconId: string;
  weeklyAcquired?: number | null;
  weeklyLimit?: number | null;
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
  allTypeOptions?: ChatTypeOption[];
}

export interface ChatTypeOption {
  id: string;
  displayName: string;
  rowId: number;
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

export interface CommandShortcut {
  id: string;
  label: string;
  command: string;
}

export interface PluginInfo {
  internalName: string;
  name: string;
  version: string;
  isLoaded: boolean;
}

export interface DailyRoutinesModule {
  name: string;
  enabled: boolean;
  displayName: string;
}

export interface DailyRoutinesSnapshot {
  modules: DailyRoutinesModule[];
}
