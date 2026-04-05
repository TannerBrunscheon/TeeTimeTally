<script setup lang="ts">
import { computed } from 'vue';
const props = defineProps<{
  label: string;
  sortKey: string;
  sortBy: string;
  sortDir: 'asc' | 'desc';
  toggleSort: (k: string) => void;
}>();

function onClick() {
  props.toggleSort(props.sortKey);
}

const ariaSort = computed(() => {
  if (props.sortBy === props.sortKey) return props.sortDir === 'asc' ? 'ascending' : 'descending';
  return 'none';
});
</script>

<template>
  <th
    role="button"
    :aria-label="`Sort by ${props.label}`"
    :aria-sort="ariaSort"
    @click="onClick"
    style="cursor:pointer; user-select:none"
  >
    <span>{{ props.label }}</span>
    <span aria-hidden="true" style="margin-left:6px; font-size:0.9em;">
      <span v-if="props.sortBy === props.sortKey">{{ props.sortDir === 'asc' ? '▲' : '▼' }}</span>
      <span v-else style="opacity:0.28">▲</span>
    </span>
  </th>
</template>

<style scoped>
/* keep minimal styles; arrow opacity handled inline for quick polish */
</style>
