<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { useGroupsStore } from '@/stores/groups';
import type { GroupYearEndReportResponse } from '@/models/reports';

const route = useRoute();
const groupId = (route.params.groupId ?? '') as string;
const year = Number(route.params.year ?? new Date().getFullYear());
const groupsStore = useGroupsStore();

const report = ref<GroupYearEndReportResponse | null>(null);
const isLoading = ref(true);
onMounted(async () => {
  isLoading.value = true;
  const res = await groupsStore.fetchGroupYearEndReport(groupId, year);
  if (res.isSuccess) report.value = res.value!;
  isLoading.value = false;
});

function formatCurrency(value: number | null | undefined) {
  if (value === null || value === undefined) return '';
  return `$${value.toFixed(2)}`;
}
</script>

<template>
  <div class="container">
    <h2>Year End Report - {{ year }}</h2>
    <div v-if="isLoading">Loading...</div>
    <div v-else-if="report">
      <h4>Group Summary</h4>
      <p>
        Rounds: {{ report.groupSummary.roundsCount }} — Avg group vs par: {{ report.groupSummary.avgGroupVsPar.toFixed(2) }}
        — Median: {{ report.groupSummary.medianGroupVsPar.toFixed(2) }}
      </p>
      <p>
        Total Pot: {{ formatCurrency(report.groupSummary.totalPotSum) }} — Max Pot: {{ formatCurrency(report.groupSummary.maxPot) }}
      </p>

      <h4>Players</h4>
      <table class="table">
        <thead><tr><th>Player</th><th>Times Played</th><th>Net</th><th>Avg vs Par/round</th></tr></thead>
        <tbody>
          <tr v-for="p in report.players" :key="p.golferId">
            <td>{{ p.fullName }}</td>
            <td>{{ p.timesPlayed }}</td>
            <td>{{ formatCurrency(p.netWinnings) }}</td>
            <td>{{ p.avgVsParPerRound.toFixed(2) }} (median: {{ p.medianVsParPerRound.toFixed(2) }})</td>
          </tr>
        </tbody>
      </table>

      <h4>Best Player</h4>
      <div v-if="report.bestPlayerByAvgVsPar">
        {{ report.bestPlayerByAvgVsPar.fullName }} — {{ report.bestPlayerByAvgVsPar.avgVsParPerRound.toFixed(2) }} avg vs par/round
      </div>
    </div>
    <div v-else>No report available.</div>
  </div>
</template>