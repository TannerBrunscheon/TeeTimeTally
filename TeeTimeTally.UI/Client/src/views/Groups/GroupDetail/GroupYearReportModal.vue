<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { useRouter } from 'vue-router';
import { useReportsStore } from '@/stores/reports';
import type { GroupYearEndReportDto } from '@/types/reports';

const props = defineProps({
  groupId: { type: String, required: true },
  year: { type: Number, required: false }
});

const emit = defineEmits(['close']);
const visible = ref(true);
const loading = ref(false);
const report = ref<GroupYearEndReportDto | null>(null);
const reportsStore = useReportsStore();
const router = useRouter();

const selectedYear = computed(() => props.year ?? new Date().getFullYear());
const year = selectedYear.value;

const group = ref<any | null>(null);

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

async function load() {
  loading.value = true;
  const res = await reportsStore.fetchGroupYearEndReport(props.groupId, year);
  loading.value = false;
  if (res.isSuccess) report.value = res.value!;
  // fetch group details for buy-in lookup
  try {
    const { useGroupsStore } = await import('@/stores/groups');
    const groupsStore = useGroupsStore();
    const gres = await groupsStore.fetchGroupById(props.groupId);
    if (gres.isSuccess) group.value = gres.value;
  } catch (e) {
    // ignore
  }
}

onMounted(load);

function close() {
  visible.value = false;
  emit('close');
}

function viewFullReport() {
  close();
  router.push({ name: 'group-year-report', params: { groupId: props.groupId, year } });
}
</script>

<template>
  <div v-if="visible" class="modal-backdrop">
    <div class="modal-dialog modal-lg">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title">Yearly Report — {{ year }}</h5>
          <button type="button" class="btn-close" @click="close"></button>
        </div>
  <div class="modal-body">
          <div v-if="loading" class="text-center py-4">
            <div class="spinner-border text-primary" role="status"></div>
            <div class="mt-2">Loading report...</div>
          </div>

          <div v-else-if="report">
            <div class="p-2 rounded" style="background:linear-gradient(90deg,#fff7ed,#fff1f2);border:1px solid #fde68a">
              <div class="row">
                <div class="col-md-4"><strong>Rounds:</strong> {{ report.groupSummary.roundsCount }}</div>
                <div class="col-md-4"><strong>Total Pot:</strong> {{ formatCurrency(report.groupSummary.totalPotSum) }}</div>
                <div class="col-md-4"><strong>Max Pot:</strong> {{ formatCurrency(report.groupSummary.maxPot) }}</div>
              </div>
              <div class="row mt-2">
                <div class="col-md-6"><strong>Avg:</strong> {{ formatNullableNumber(report.groupSummary.avgGroupVsPar) }}</div>
                <div class="col-md-6"><strong>Median:</strong> {{ formatNullableNumber(report.groupSummary.medianGroupVsPar) }}</div>
              </div>
            </div>

            <h6 class="mt-3">Members</h6>
            <div class="table-responsive">
              <table class="table table-sm table-striped">
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
            </div>

            <div class="mt-3">
              <h6>Top Teams</h6>
              <p>
                <strong>Best Team (Avg):</strong>
                <span v-if="report.bestTeamByAvg">{{ (report.bestTeamByAvg.members || []).map(m=>m.fullName).join(', ') }} — {{ report.bestTeamByAvg.roundsPlayedTogether ?? 0 }} games — {{ formatNullableNumber(report.bestTeamByAvg.avgScorePerRound) }}</span>
                <span v-else>—</span>
              </p>
              <p>
                <strong>Best Team (Single Round):</strong>
                <span v-if="report.bestTeamBestRound">{{ (report.bestTeamBestRound.members || []).map(m=>m.fullName).join(', ') }} — {{ report.bestTeamBestRound.roundsPlayedTogether ?? 0 }} games — {{ formatNullableNumber(report.bestTeamBestRound.bestRoundScore) }}</span>
                <span v-else>—</span>
              </p>

              <div>
                <strong>Most played together:</strong>
                <div v-if="report.mostPlayedTeams && report.mostPlayedTeams.length">
                  <ul>
                    <li v-for="(t, idx) in report.mostPlayedTeams" :key="idx">{{ t.members.map(m=>m.fullName).join(', ') }} — {{ t.count }} games</li>
                  </ul>
                </div>
                <div v-else>—</div>
              </div>
            </div>
          </div>

          <div v-else class="text-muted">No report available for this year.</div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="close">Close</button>
          <button class="btn btn-primary" @click="viewFullReport">View Full Report</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0,0,0,0.4);
  z-index: 1050;
}
.modal-dialog { max-width: 900px; }
</style>