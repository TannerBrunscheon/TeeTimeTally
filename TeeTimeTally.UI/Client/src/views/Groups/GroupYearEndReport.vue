<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
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
const sortBy = ref<string>('timesPlayed');
const sortDir = ref<'asc' | 'desc'>('desc');
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

const sortedPlayers = computed(() => {
  if (!report.value) return [] as GroupYearEndReportResponse['players'];
  const arr = [...report.value.players];
  const dir = sortDir.value === 'asc' ? 1 : -1;
  arr.sort((a: any, b: any) => {
    switch (sortBy.value) {
      case 'fullName': return a.fullName.localeCompare(b.fullName) * dir;
      case 'timesPlayed': return (a.timesPlayed - b.timesPlayed) * dir;
      case 'winnings': return ((a.netWinnings ?? 0) - (b.netWinnings ?? 0)) * dir;
  case 'cth': return ((a.closestToHoleCount ?? 0) - (b.closestToHoleCount ?? 0)) * dir;
      case 'net': return (displayedNet(a) - displayedNet(b)) * dir;
      case 'avg': return ((a.avgVsParPerRound ?? Number.MAX_SAFE_INTEGER) - (b.avgVsParPerRound ?? Number.MAX_SAFE_INTEGER)) * dir;
      case 'median': return ((a.medianVsParPerRound ?? Number.MAX_SAFE_INTEGER) - (b.medianVsParPerRound ?? Number.MAX_SAFE_INTEGER)) * dir;
      default: return 0;
    }
  });
  return arr;
});

function toggleSort(key: string) {
  if (sortBy.value === key) {
    sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc';
  } else {
    sortBy.value = key;
    sortDir.value = 'desc';
  }
}
</script>

<template>
  <div class="container">
    <div class="mb-3">
      <h2>Year End Report - {{ year }}</h2>
      <div v-if="isLoading">Loading...</div>
      <div v-else-if="report">
        <div class="p-3 rounded" style="background: linear-gradient(90deg,#f8fafc,#eef2ff); border: 1px solid #e6eefc;">
          <div style="display:flex;align-items:center;justify-content:space-between;gap:1rem;">
            <div>
              <h4 style="margin:0">Group Summary</h4>
              <div style="font-size:1rem; color:#0f172a;">Rounds: <strong>{{ report.groupSummary.roundsCount }}</strong></div>
            </div>
            <div style="text-align:right">
              <div style="font-weight:600;color:#065f46">Avg score/round: <span style="font-size:1.1rem">{{ formatNullableNumber(report.groupSummary.avgGroupVsPar) }}</span></div>
              <div style="color:#0f172a">Median: <span>{{ formatNullableNumber(report.groupSummary.medianGroupVsPar) }}</span></div>
            </div>
            <div style="text-align:right">
              <div style="font-weight:600;color:#0f172a">Total Pot: <span style="font-size:1.1rem">{{ formatCurrency(report.groupSummary.totalPotSum) }}</span></div>
              <div>Max Pot: {{ formatCurrency(report.groupSummary.maxPot) }}</div>
            </div>
          </div>
        </div>
        <div class="mt-3"></div>
      </div>
      <div v-else>No report available.</div>
    </div>
    <div v-if="report">
  <h4 class="mt-4">Players</h4>
      <table class="table table-striped table-hover">
        <thead>
          <tr>
            <th @click="toggleSort('fullName')" style="cursor:pointer">Player</th>
            <th @click="toggleSort('timesPlayed')" style="cursor:pointer">Times Played</th>
            <th @click="toggleSort('cth')" style="cursor:pointer">CTH</th>
            <th @click="toggleSort('winnings')" style="cursor:pointer">Winnings</th>
            <th @click="toggleSort('net')" style="cursor:pointer">Net</th>
            <th @click="toggleSort('avg')" style="cursor:pointer">Avg score/round</th>
            <th @click="toggleSort('median')" style="cursor:pointer">Median score/round</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="p in sortedPlayers" :key="p.golferId">
            <td>{{ p.fullName }}</td>
            <td>{{ p.timesPlayed }}</td>
            <td>{{ p.closestToHoleCount ?? 0 }}</td>
            <td>{{ formatCurrency(p.netWinnings) }}</td>
            <td>{{ formatCurrency(displayedNet(p)) }}</td>
            <td>{{ formatNullableNumber(p.avgVsParPerRound) }}</td>
            <td>{{ formatNullableNumber(p.medianVsParPerRound) }}</td>
          </tr>
        </tbody>
      </table>

      <div class="row mt-4 mb-5">
        <div class="col-md-6">
          <h5>Top Players 🏆</h5>
          <p><strong>Best (Avg):</strong> {{ report.bestPlayerByAvgVsPar ? report.bestPlayerByAvgVsPar.fullName + ' — ' + formatNullableNumber(report.bestPlayerByAvgVsPar.avgVsParPerRound) : '—' }}</p>
          <p><strong>Best (Median):</strong> {{ report.bestPlayerByMedian ? report.bestPlayerByMedian.fullName + ' — ' + formatNullableNumber(report.bestPlayerByMedian.medianVsParPerRound) : '—' }}</p>
          <p><strong>Most Closest to the Pins:</strong> {{ report.bestPlayerByCth ? report.bestPlayerByCth.fullName + ' — ' + (report.bestPlayerByCth.closestToHoleCount ?? 0) + ' CTH' : '—' }}</p>
        </div>
        <div class="col-md-6">
          <h5>Top Teams 🏆</h5>
          <p>
            <strong>Best Team (Avg):</strong>
            <span v-if="report.bestTeamByAvg">
              {{ (report.bestTeamByAvg.members || []).map(m=>m.fullName).join(', ') }}
              — {{ report.bestTeamByAvg.roundsPlayedTogether ?? '0' }} games —
              {{ formatNullableNumber(report.bestTeamByAvg.avgScorePerRound) }}
            </span>
            <span v-else>—</span>
          </p>
          <p>
            <strong>Best Team (Single Round):</strong>
            <span v-if="report.bestTeamBestRound">
              {{ (report.bestTeamBestRound.members || []).map(m=>m.fullName).join(', ') }}
              — {{ report.bestTeamBestRound.roundsPlayedTogether ?? '0' }} games —
              {{ formatNullableNumber(report.bestTeamBestRound.bestRoundScore) }}
            </span>
            <span v-else>—</span>
          </p>

          <div class="mt-2">
            <strong>Most played together:</strong>
            <div v-if="report.mostPlayedTeams && report.mostPlayedTeams.length">
              <ul>
                <li v-for="(t, idx) in report.mostPlayedTeams" :key="idx">
                  {{ t.members.map(m=>m.fullName).join(', ') }} — {{ t.count }} games
                </li>
              </ul>
            </div>
            <div v-else>—</div>
          </div>
        </div>
      </div>
    </div>
    
  </div>
</template>