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
        <div class="card mb-4">
          <div class="card-header d-flex justify-content-between align-items-center">
            <h4 class="mb-0">Group Summary</h4>
            <div class="text-end small text-muted">Year: {{ year }}</div>
          </div>
          <div class="card-body">
            <div class="d-flex justify-content-between align-items-center">
              <div>
                <div class="h5 mb-0">{{ group?.name || '' }}</div>
                <div class="text-muted">Rounds: <strong>{{ report.groupSummary.roundsCount }}</strong></div>
              </div>
              <div class="text-end">
                <div class="fw-semibold text-success">Avg score/round: <span class="ms-2">{{ formatNullableNumber(report.groupSummary.avgGroupVsPar) }}</span></div>
                <div class="text-muted">Median: <span class="ms-2">{{ formatNullableNumber(report.groupSummary.medianGroupVsPar) }}</span></div>
              </div>
              <div class="text-end">
                <div class="fw-semibold">Total Pot: <span class="ms-2">{{ formatCurrency(report.groupSummary.totalPotSum) }}</span></div>
                <div class="text-muted">Max Pot: <span class="ms-2">{{ formatCurrency(report.groupSummary.maxPot) }}</span></div>
              </div>
            </div>
          </div>
        </div>
        <div class="mt-3"></div>
      </div>
      <div v-else>No report available.</div>
    </div>
    <div v-if="report">

  <div class="card mb-4">
    <div class="card-header d-flex justify-content-between align-items-center">
      <h4 class="mb-0">Players</h4>
      <div class="small text-muted">{{ report.groupSummary.roundsCount }} rounds</div>
    </div>
    <div class="card-body p-0">
      <table class="table table-striped table-hover mb-0">
        <thead>
          <tr>
            <ReportTableHeader label="Player" sortKey="fullName" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" />
            <ReportTableHeader label="Times Played" sortKey="timesPlayed" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" />
            <ReportTableHeader label="CTH" sortKey="cth" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" />
            <ReportTableHeader label="Winnings" sortKey="winnings" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" cellClass="col-winnings" />
            <ReportTableHeader label="Skins" sortKey="net" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" cellClass="col-skins-net" />
            <ReportTableHeader label="Skins / Round" sortKey="netPerRound" :sortBy="sortBy" :sortDir="sortDir" :toggleSort="toggleSort" />
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
    </div>
  </div>

      <div class="row mt-4 mb-5">
        <div class="col-md-6">
          <div class="card accent-card mb-3">
            <div class="card-body">
              <h5 class="mb-2">Top Players <span class="badge bg-warning text-dark">🏆</span></h5>
              <p class="mb-1"><strong>Best (Avg):</strong>
                <span v-if="report.bestPlayerByAvgVsPar">{{ report.bestPlayerByAvgVsPar.fullName + ' — ' + formatNullableNumber(report.bestPlayerByAvgVsPar.avgVsParPerRound) }}</span>
                <span v-else>No eligible players (minimum 3 rounds)</span>
              </p>
              <p class="mb-0"><strong>Most Closest to the Pins:</strong> {{ report.bestPlayerByCth ? report.bestPlayerByCth.fullName + ' — ' + (report.bestPlayerByCth.closestToHoleCount ?? 0) + ' CTH' : '—' }}</p>
            </div>
          </div>
        </div>
        <div class="col-md-6">
          <div class="card accent-card mb-3">
            <div class="card-body">
              <h5 class="mb-2">Top Teams <span class="badge bg-warning text-dark">🏆</span></h5>
              <p class="mb-1">
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
                <span v-else>No eligible teams (minimum 3 rounds)</span>
              </p>
              <p class="mb-1">
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

/* Small tweaks to align with Overview styling while preserving table responsiveness */
.report-section-title { margin-top: 1rem; }

/* Ensure striped rows still readable */
.table.table-striped tbody tr:nth-of-type(odd) { background-color: rgba(0,0,0,0.02); }

/* Accent card matching overview style but with a tasteful green left border */
.accent-card { border-left: 6px solid #065f46; background: linear-gradient(180deg,#fff,#f7fdf8); }
.accent-card .card-body { padding: 0.9rem; }

</style>