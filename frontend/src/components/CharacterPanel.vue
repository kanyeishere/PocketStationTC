<script setup lang="ts">
import { computed } from "vue";
import type { CharacterState } from "@/types";

const props = defineProps<{
  character?: CharacterState | null;
  emptyText: string;
}>();

const hpPct = computed(() => pct(props.character?.currentHp || 0, props.character?.maxHp || 0));
const mpPct = computed(() => pct(props.character?.currentMp || 0, props.character?.maxMp || 0));
const position = computed(() => props.character?.position || {});

function pct(value: number, max: number) {
  if (!max) {
    return 0;
  }

  return Math.max(0, Math.min(100, Math.round((value / max) * 100)));
}
</script>

<template>
  <div class="panel">
    <template v-if="character">
      <div class="stat-name">{{ character.name || "Unknown" }}</div>
      <div class="bars">
        <div class="bar"><span :style="{ width: `${hpPct}%` }" /></div>
        <div class="bar mp"><span :style="{ width: `${mpPct}%` }" /></div>
      </div>
      <div class="meta-grid">
        <div>HP {{ character.currentHp || 0 }}/{{ character.maxHp || 0 }}</div>
        <div>MP {{ character.currentMp || 0 }}/{{ character.maxMp || 0 }}</div>
        <div>Job {{ character.classJobId || 0 }} Lv.{{ character.level || 0 }}</div>
        <div>
          {{ Number(position.x ?? position.X ?? 0).toFixed(1) }},
          {{ Number(position.y ?? position.Y ?? 0).toFixed(1) }},
          {{ Number(position.z ?? position.Z ?? 0).toFixed(1) }}
        </div>
      </div>
    </template>
    <template v-else>
      {{ emptyText }}
    </template>
  </div>
</template>
