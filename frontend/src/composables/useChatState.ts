import { computed, ref } from "vue";
import { getJson, postJson } from "@/services/pocketApi";
import type { ChatEvent, ChatFilterMode, ChatFilterSettings, ChatTypeOption, ConnectionMode } from "@/types";

type SetConnection = (text: string, mode?: ConnectionMode) => void;

const maxChatRows = 500;
const defaultMode: ChatFilterMode = {
  id: "all",
  name: "全部消息",
  isBuiltIn: true,
  enabledTypes: [],
  includeKeywords: [],
  excludeKeywords: []
};

export function useChatState(setConnection: SetConnection) {
  const chats = ref<ChatEvent[]>([]);
  const filterModes = ref<ChatFilterMode[]>([]);
  const currentModeId = ref("all");
  const allChatTypes = ref<ChatTypeOption[]>([]);

  const currentMode = computed(() => {
    return filterModes.value.find((mode) => mode.id === currentModeId.value) || filterModes.value[0] || defaultMode;
  });

  const filteredChats = computed(() => {
    const mode = currentMode.value;
    return chats.value.filter((item) => matchesMode(item, mode));
  });

  function addChat(item: ChatEvent | null | undefined) {
    if (!item) {
      return;
    }

    chats.value.push(item);
    if (chats.value.length > maxChatRows) {
      chats.value.splice(0, chats.value.length - maxChatRows);
    }
  }

  function setHistory(items: unknown) {
    chats.value = Array.isArray(items) ? (items as ChatEvent[]) : [];
  }

  async function loadHistory() {
    setHistory(await getJson<ChatEvent[]>("/api/chat/history"));
  }

  async function loadFilterSettings() {
    setFilterSettings(await getJson<ChatFilterSettings>("/api/chat/modes"));
  }

  async function selectMode(modeId: string) {
    currentModeId.value = modeId;
    await saveFilterSettings(filterModes.value, modeId, false);
  }

  async function saveMode(mode: ChatFilterMode) {
    const modes = [...filterModes.value];
    const index = modes.findIndex((item) => item.id === mode.id);
    if (index >= 0) {
      modes[index] = mode;
    } else {
      modes.push(mode);
    }

    await saveFilterSettings(modes, mode.id);
  }

  async function deleteCurrentMode() {
    const mode = currentMode.value;
    if (mode.isBuiltIn) {
      return;
    }

    const modes = filterModes.value.filter((item) => item.id !== mode.id);
    await saveFilterSettings(modes, "all");
  }

  async function saveFilterSettings(modes: ChatFilterMode[], modeId: string, showSaved = true) {
    try {
      const settings = await postJson<ChatFilterSettings>("/api/chat/modes", {
        currentModeId: modeId,
        modes,
        allTypes: allChatTypes.value.map((type) => type.id)
      });

      setFilterSettings(settings);
      if (showSaved) {
        setConnection("筛选已保存", "online");
      }
    } catch (error) {
      setConnection(String(error), "offline");
    }
  }

  function setFilterSettings(settings: ChatFilterSettings) {
    filterModes.value = Array.isArray(settings.modes) ? settings.modes : [];
    allChatTypes.value = normalizeTypeOptions(settings);
    currentModeId.value = settings.currentModeId || "all";

    if (!filterModes.value.some((mode) => mode.id === currentModeId.value)) {
      currentModeId.value = filterModes.value[0]?.id || "all";
    }
  }

  return {
    allChatTypes,
    chats,
    currentMode,
    currentModeId,
    filteredChats,
    filterModes,
    addChat,
    deleteCurrentMode,
    loadFilterSettings,
    loadHistory,
    saveMode,
    selectMode,
    setFilterSettings,
    setHistory
  };
}

function normalizeTypeOptions(settings: ChatFilterSettings): ChatTypeOption[] {
  if (Array.isArray(settings.allTypeOptions) && settings.allTypeOptions.length > 0) {
    return settings.allTypeOptions.map((type) => ({
      id: type.id,
      displayName: type.displayName || type.id,
      rowId: type.rowId || 0
    }));
  }

  return (Array.isArray(settings.allTypes) ? settings.allTypes : []).map((id) => ({
    id,
    displayName: id,
    rowId: 0
  }));
}

function matchesMode(item: ChatEvent, mode: ChatFilterMode) {
  const enabledTypes = mode.enabledTypes || [];
  if (enabledTypes.length > 0 && !enabledTypes.some((type) => equalsText(type, item.channel))) {
    return false;
  }

  const haystack = `${item.channel || ""} ${item.sender || ""} ${item.message || ""}`.toLocaleLowerCase();
  const includeKeywords = normalizeKeywords(mode.includeKeywords || []);
  if (includeKeywords.length > 0 && !includeKeywords.some((keyword) => haystack.includes(keyword.toLocaleLowerCase()))) {
    return false;
  }

  const excludeKeywords = normalizeKeywords(mode.excludeKeywords || []);
  return !excludeKeywords.some((keyword) => haystack.includes(keyword.toLocaleLowerCase()));
}

function normalizeKeywords(values: string[]) {
  return values
    .map((value) => value.trim())
    .filter((value, index, array) => value && array.findIndex((other) => equalsText(other, value)) === index);
}

function equalsText(left: string, right: string) {
  return left.toLocaleLowerCase() === right.toLocaleLowerCase();
}
