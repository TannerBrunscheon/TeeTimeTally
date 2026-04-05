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
const group = ref<any | null>(null);
onMounted(async () => {
  isLoading.value = true;
  const res = await groupsStore.fetchGroupYearEndReport(groupId, year);
  if (res.isSuccess) report.value = res.value!;
  // Ensure group details (for buy-in amount)
  const gres = await groupsStore.fetchGroupById(groupId);
  if (gres.isSuccess) group.value = gres.value;
  isLoading.value = false;
});

function formatCurrency(value: number | null | undefined) {
  if (value === null || value === undefined) return '—';
  return `$${value.toFixed(2)}`;
}

function formatNullableNumber(value?: number | null) {
  if (value === null || value === undefined) return '—';
  return value.toFixed(2);
}

function displayedNet(p: any) {
  const buyIn = group.value?.activeFinancialConfiguration?.buyInAmount ?? 0;
  const contributed = (p.timesPlayed ?? 0) * buyIn;
  const net = (p.netWinnings ?? 0) - contributed;
  return net;
}
</script>

<template>
  <div class="container">
    <h2 class="mb-3">Year End Report - {{ year }}</h2>
    <div v-if="isLoading">Loading...</div>
    <div v-else-if="report">
      <h4>Group Summary</h4>
      <p>
        Rounds: {{ report.groupSummary.roundsCount }} —
        <span v-if="report.groupSummary.avgGroupVsPar === null || report.groupSummary.avgGroupVsPar === undefined">Avg score/round</span>
        <span v-else>Avg vs Par/round</span>
        : {{ formatNullableNumber(report.groupSummary.avgGroupVsPar) }} — Median: {{ formatNullableNumber(report.groupSummary.medianGroupVsPar) }}
      </p>
      <p>
        Total Pot: {{ formatCurrency(report.groupSummary.totalPotSum) }} — Max Pot: {{ formatCurrency(report.groupSummary.maxPot) }}
      </p>
      <h4 class="mt-4">Players</h4>
      <table class="table table-striped table-hover">
        <thead>
          <tr>
            <th>Player</th>
            <th>Times Played</th>
            <th>Net</th>
            <th>Avg score/round</th>
            <th>Median score/round</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="p in report.players" :key="p.golferId">
            <td>{{ p.fullName }}</td>
            <td>{{ p.timesPlayed }}</td>
            <td>{{ formatCurrency(displayedNet(p)) }}</td>
            <td>{{ formatNullableNumber(p.avgVsParPerRound) }}</td>
            <td>{{ formatNullableNumber(p.medianVsParPerRound) }}</td>
          </tr>
        </tbody>
      </table>

      <div class="row mt-4 mb-5">
        <div class="col-md-6">
          <h5>Top Players</h5>
          <p><strong>Best (Avg):</strong> {{ report.bestPlayerByAvgVsPar ? report.bestPlayerByAvgVsPar.fullName + ' — ' + formatNullableNumber(report.bestPlayerByAvgVsPar.avgVsParPerRound) : '—' }}</p>
          <p><strong>Best (Median):</strong> {{ report.bestPlayerByMedian ? report.bestPlayerByMedian.fullName + ' — ' + formatNullableNumber(report.bestPlayerByMedian.medianVsParPerRound) : '—' }}</p>
        </div>
        <div class="col-md-6">
          <h5>Top Teams</h5>
          <p><strong>Best Team (Avg):</strong> {{ report.bestTeamByAvg ? report.bestTeamByAvg.teamName + ' — ' + formatNullableNumber(report.bestTeamByAvg.avgScorePerRound) : '—' }}</p>
          <p><strong>Best Team (Single Round):</strong> {{ report.bestTeamBestRound ? report.bestTeamBestRound.teamName + ' — ' + formatNullableNumber(report.bestTeamBestRound.bestRoundScore) : '—' }}</p>
        </div>
      </div>
    </div>
    <div v-else>No report available.</div>
  </div>
</template>