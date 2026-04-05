<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { useGroupsStore } from '@/stores/groups';
import type { GroupYearEndReportResponse } from '@/models/reports';
import { useReportTable } from '@/composables/useReportTable';
import ReportTableHeader from '@/components/ReportTableHeader.vue';

const route = useRoute();
const groupId = (route.params.groupId ?? '') as string;
const year = Number(route.params.year ?? new Date().getFullYear());
const groupsStore = useGroupsStore();

const report = ref<GroupYearEndReportResponse | null>(null);
const isLoading = ref(true);
const group = ref<any | null>(null);
const { sortBy, sortDir, toggleSort, displayedNet, sortedPlayers } = useReportTable(report, group);
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

// Sorting, displayedNet and sortedPlayers are provided by the useReportTable composable
</script>

<template>
  <div class="container">
    <div class="mb-3">
      <h2>Year End Report - {{ year }}</h2>
      <div v-if="isLoading">Loading...</div>
      <div v-else-if="report">
        <div class="report-summary p-3 rounded">
          <div class="summary-row">
            <div>
              <h4 class="summary-title">Group Summary</h4>
              <div class="summary-sub">Rounds: <strong>{{ report.groupSummary.roundsCount }}</strong></div>
            </div>
            <div class="summary-stats">
              <div class="stat-line">Avg score/round: <span class="stat-value">{{ formatNullableNumber(report.groupSummary.avgGroupVsPar) }}</span></div>
              <div class="stat-sub">Median: <span>{{ formatNullableNumber(report.groupSummary.medianGroupVsPar) }}</span></div>
            </div>
            <div class="summary-stats">
              <div class="stat-line">Total Pot: <span class="stat-value">{{ formatCurrency(report.groupSummary.totalPotSum) }}</span></div>
              <div class="stat-sub">Max Pot: {{ formatCurrency(report.groupSummary.maxPot) }}</div>
            </div>
          </div>
        </div>
        <div class="mt-3"></div>
      </div>
      <div v-else>No report available.</div>
    </div>
    <div v-if="report">

  <h4 class="mt-4 report-section-title">Players</h4>
    <table class="table table-striped table-hover report-table">
        <thead>
          <tr>
            <ReportTableHeader label="Player" sortKey="fullName" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" />
            <ReportTableHeader label="Times Played" sortKey="timesPlayed" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" />
            <ReportTableHeader label="CTH" sortKey="cth" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" />
            <ReportTableHeader label="Winnings" sortKey="winnings" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" cellClass="col-winnings" />
            <ReportTableHeader label="Skins Net" sortKey="net" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" cellClass="col-skins-net" />
            <ReportTableHeader label="Skins Net / Round" sortKey="netPerRound" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" />
            <ReportTableHeader label="Avg score/round" sortKey="avg" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" />
            <ReportTableHeader label="Median score/round" sortKey="median" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" cellClass="col-median" />
          </tr>
        </thead>
        <tbody>
          <tr v-for="p in sortedPlayers" :key="p.golferId">
            <td>{{ p.fullName }}</td>
            <td>{{ p.timesPlayed }}</td>
            <td class="col-cth">{{ p.closestToHoleCount ?? 0 }}</td>
            <td class="col-winnings">{{ formatCurrency(p.totalWinnings) }}</td>
            <td class="col-skins-net">{{ formatCurrency(displayedNet(p)) }}</td>
            <td class="col-skins-net-per">{{ p.timesPlayed ? formatCurrency(displayedNet(p) / p.timesPlayed) : '—' }}</td>
            <td>{{ formatNullableNumber(p.avgVsParPerRound) }}</td>
            <td>{{ formatNullableNumber(p.medianVsParPerRound) }}</td>
          </tr>
        </tbody>
      </table>

      <div class="row mt-4 mb-5">
        <div class="col-md-6">
          <h5 class="accent-heading">Top Players <span class="badge trophy">🏆</span></h5>
          <p><strong>Best (Avg):</strong> {{ report.bestPlayerByAvgVsPar ? report.bestPlayerByAvgVsPar.fullName + ' — ' + formatNullableNumber(report.bestPlayerByAvgVsPar.avgVsParPerRound) : '—' }}</p>
          <p><strong>Best (Median):</strong> {{ report.bestPlayerByMedian ? report.bestPlayerByMedian.fullName + ' — ' + formatNullableNumber(report.bestPlayerByMedian.medianVsParPerRound) : '—' }}</p>
          <p><strong>Most Closest to the Pins:</strong> {{ report.bestPlayerByCth ? report.bestPlayerByCth.fullName + ' — ' + (report.bestPlayerByCth.closestToHoleCount ?? 0) + ' CTH' : '—' }}</p>
        </div>
        <div class="col-md-6">
          <h5 class="accent-heading">Top Teams <span class="badge trophy">🏆</span></h5>
          <p>
            <strong>Best Team (Avg):</strong>
            <span v-if="report.bestTeamsByAvg && report.bestTeamsByAvg.length">
              <ul class="mb-0">
                <li v-for="(t, idx) in report.bestTeamsByAvg" :key="idx">
                  {{ (t.members || []).map(m=>m.fullName).join(', ') }} — {{ t.roundsPlayedTogether ?? '0' }} games — {{ formatNullableNumber(t.avgScorePerRound) }}
                </li>
              </ul>
            </span>
            <span v-else-if="report.bestTeamByAvg">
              {{ (report.bestTeamByAvg.members || []).map(m=>m.fullName).join(', ') }}
              — {{ report.bestTeamByAvg.roundsPlayedTogether ?? '0' }} games —
              {{ formatNullableNumber(report.bestTeamByAvg.avgScorePerRound) }}
            </span>
            <span v-else>—</span>
          </p>
          <p>
            <strong>Best Team (Single Round):</strong>
            <span v-if="report.bestTeamsByBestRound && report.bestTeamsByBestRound.length">
              <ul class="mb-0">
                <li v-for="(t, idx) in report.bestTeamsByBestRound" :key="idx">
                  {{ (t.members || []).map(m=>m.fullName).join(', ') }} — {{ t.roundsPlayedTogether ?? '0' }} games — {{ formatNullableNumber(t.bestRoundScore) }}
                </li>
              </ul>
            </span>
            <span v-else-if="report.bestTeamBestRound">
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

<style scoped>
/* Responsive tweaks: hide lower-priority financial columns on small screens */
@media (max-width: 768px) {
  /* hide gross winnings and skins net and median columns to keep table readable */
  th.col-winnings, td.col-winnings,
  th.col-skins-net, td.col-skins-net,
  th.col-median, td.col-median {
    display: none !important;
  }

  /* tighten padding for table */
  table.table td, table.table th {
    padding: 0.35rem 0.5rem;
  }
}

/* Color palette inspired by golf greens and yellows. Ensure good contrast for legibility. */
.report-summary {
  background: linear-gradient(90deg, #ecfdf5 0%, #bbf7d0 100%); /* very light green to pale mint */
  border: 1px solid #bbf7d0;
  color: #064e3b; /* dark green text */
}
.summary-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}
.summary-title {
  margin: 0;
  color: #065f46;
}
.summary-sub { color: #064e3b; font-size: 1rem; }
.summary-stats { text-align: right; }
.stat-line { font-weight: 600; color: #064e3b; }
.stat-value { font-size: 1.05rem; margin-left: 0.25rem; }
.stat-sub { color: #064e3b; }

/* Table header uses deeper green with light text for strong contrast */
.report-table thead th {
  background: linear-gradient(180deg,#065f46 0%, #047857 100%);
  color: #f8fafc !important; /* near-white */
  border-color: rgba(0,0,0,0.08) !important;
}

/* Hover rows get a soft yellow accent */
.report-table.table-hover tbody tr:hover {
  background: linear-gradient(90deg, rgba(251,191,36,0.08), rgba(255,250,204,0.06));
}

/* Accent headings for side panels */
.accent-heading { color: #064e3b; display:flex; align-items:center; gap:0.5rem; }
.badge.trophy { background: #fbbf24; color: #0f172a; padding: 0.1rem 0.4rem; border-radius: 0.25rem; font-size:0.9rem; }

/* Ensure stripped rows still readable on small screens */
.report-table.table-striped tbody tr:nth-of-type(odd) { background-color: rgba(6,95,70,0.02); }

</style>