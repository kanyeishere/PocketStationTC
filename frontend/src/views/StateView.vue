<script setup lang="ts">
import CharacterPanel from "@/components/CharacterPanel.vue";
import type { PlayerSnapshot } from "@/types";

defineProps<{
  snapshot: PlayerSnapshot | null;
}>();
</script>

<template>
  <section id="tab-state" class="view">
    <div class="section-title">自身</div>
    <CharacterPanel :character="snapshot?.localPlayer" empty-text="未登录" />

    <div class="section-title">目标</div>
    <CharacterPanel :character="snapshot?.target" empty-text="无目标" />

    <div class="section-title">小队</div>
    <div class="list">
      <CharacterPanel
        v-for="member in snapshot?.party || []"
        :key="`${member.objectId}-${member.entityId}`"
        :character="member"
        empty-text="未知成员"
      />
    </div>
  </section>
</template>
