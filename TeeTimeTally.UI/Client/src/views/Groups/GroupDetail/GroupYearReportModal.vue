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

function formatCurrency(value: number | null | undefined) {
  if (value === null || value === undefined) return '';
  return `$${value.toFixed(2)}`;
}

async function load() {
  loading.value = true;
  const res = await reportsStore.fetchGroupYearEndReport(props.groupId, year);
  loading.value = false;
  if (res.isSuccess) report.value = res.value!;
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
            <div class="mb-3 row">
              <div class="col-md-4">
                <strong>Rounds:</strong> {{ report.groupSummary.roundsCount }}
              </div>
              <div class="col-md-4">
                <strong>Total Pot:</strong> {{ formatCurrency(report.groupSummary.totalPotSum) }}
              </div>
              <div class="col-md-4">
                <strong>Max Pot:</strong> {{ formatCurrency(report.groupSummary.maxPot) }}
              </div>
            </div>
            <div class="mb-3 row">
              <div class="col-md-6"><strong>Avg Group vs Par:</strong> {{ report.groupSummary.avgGroupVsPar.toFixed(2) }}</div>
              <div class="col-md-6"><strong>Median Group vs Par:</strong> {{ report.groupSummary.medianGroupVsPar.toFixed(2) }}</div>
            </div>

            <h6>Members</h6>
            <div class="table-responsive">
              <table class="table table-sm">
                <thead>
                  <tr>
                    <th>Player</th>
                    <th>Times Played</th>
                    <th>Net</th>
                    <th>Avg vs Par</th>
                    <th>Median vs Par</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="p in report.players" :key="p.golferId">
                    <td>{{ p.fullName }}</td>
                    <td>{{ p.timesPlayed }}</td>
                    <td>{{ formatCurrency(p.netWinnings) }}</td>
                    <td>{{ p.avgVsParPerRound.toFixed(2) }}</td>
                    <td>{{ p.medianVsParPerRound.toFixed(2) }}</td>
                  </tr>
                </tbody>
              </table>
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