<script setup lang="ts">
import CharacterPanel from "@/components/CharacterPanel.vue";
import type { CharacterState } from "@/types";

defineProps<{
  party: CharacterState[];
}>();
</script>

<template>
  <div class="section-title">小队</div>
  <div class="list">
    <CharacterPanel
      v-for="member in party"
      :key="`${member.objectId}-${member.entityId}`"
      :character="member"
      empty-text="未知成员"
    />
  </div>
</template>
