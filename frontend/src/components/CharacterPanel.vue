<script setup lang="ts">
import type { CharacterState } from "@/types";

defineProps<{
  character?: CharacterState | null;
  emptyText: string;
  territoryName?: string;
  worldName?: string;
  dataCenterName?: string;
}>();
</script>

<template>
  <div class="panel">
    <template v-if="character">
      <div class="stat-name">{{ character.name || "Unknown" }}</div>
      <div class="meta-grid">
        <div class="job-line">
          {{ character.classJobName || `Job ${character.classJobId || 0}` }}
          Lv.{{ character.level || 0 }}
        </div>
        <div>HP {{ character.currentHp || 0 }}/{{ character.maxHp || 0 }}</div>
        <div>MP {{ character.currentMp || 0 }}/{{ character.maxMp || 0 }}</div>
        <div v-if="territoryName || worldName || dataCenterName">
          {{ dataCenterName || '?' }} / {{ worldName || '?' }} / {{ territoryName || '?' }}
        </div>
        <div v-else class="muted">
          {{ character.position?.x != null
            ? `${Number(character.position.x).toFixed(1)}, ${Number(character.position.y).toFixed(1)}, ${Number(character.position.z).toFixed(1)}`
            : `${Number(character.position?.X ?? 0).toFixed(1)}, ${Number(character.position?.Y ?? 0).toFixed(1)}, ${Number(character.position?.Z ?? 0).toFixed(1)}` }}
        </div>
      </div>
    </template>
    <template v-else>
      {{ emptyText }}
    </template>
  </div>
</template>
