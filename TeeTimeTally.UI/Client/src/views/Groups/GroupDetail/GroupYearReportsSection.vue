<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import * as groupsApi from '@/services/groupsApi';
import { AppError } from '@/primitives/error';

const props = defineProps({ groupId: { type: String, required: true } });
const router = useRouter();

const years = ref<number[]>([]);
const isLoading = ref(false);
const error = ref<AppError | null>(null);

async function loadYears() {
  isLoading.value = true;
  error.value = null;
  try {
    const data = await groupsApi.getGroupReportYears(props.groupId);
    years.value = data;
  } catch (e: any) {
    error.value = AppError.failure(e?.message || 'Failed to load report years');
  } finally {
    isLoading.value = false;
  }
}

function viewYear(year: number) {
  router.push({ name: 'group-year-report', params: { groupId: props.groupId, year } });
}

onMounted(loadYears);
</script>

<template>
  <div class="card mt-4">
    <div class="card-header bg-light">
      <h3 class="h5 mb-0">Year End Reports</h3>
    </div>
    <div class="card-body">
      <div v-if="isLoading" class="text-center py-3">
        <div class="spinner-border" role="status"></div>
      </div>

      <div v-else-if="error" class="alert alert-danger">{{ error.message }}</div>

      <div v-else>
        <div v-if="years.length === 0" class="text-muted">No year-end reports available.</div>
        <div v-else class="table-responsive">
          <table class="table table-sm">
            <thead>
              <tr>
                <th>Year</th>
                <th class="text-end">Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="y in years" :key="y">
                <td>{{ y }}</td>
                <td class="text-end">
                  <button class="btn btn-sm btn-primary" @click="viewYear(y)">View Report</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.card-header h3 { font-weight: 500; }
</style>
