<template>
  <div class="card mt-4">
    <div class="card-header">
      <h3>Round History</h3>
    </div>
    <div class="card-body">
      <div v-if="isLoadingGroupRoundHistory" class="text-center">
        <div class="spinner-border" role="status">
          <span class="visually-hidden">Loading...</span>
        </div>
      </div>
      <div v-else-if="groupRoundHistoryError" class="alert alert-danger">
        {{ groupRoundHistoryError.message }}
      </div>
      <div v-else-if="groupRoundHistory.length === 0" class="text-center text-muted">
        No round history found for this group.
      </div>
      <div v-else class="table-responsive">
        <table class="table table-hover">
          <thead>
            <tr>
              <th scope="col">Date</th>
              <th scope="col">Course</th>
              <th scope="col">Players</th>
              <th scope="col">Pot</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="round in groupRoundHistory" :key="round.roundId" @click="navigateToRound(round.roundId)"
              style="cursor: pointer;">
              <td>{{ new Date(round.roundDate).toLocaleDateString() }}</td>
              <td>{{ round.courseName }}</td>
              <td>{{ round.numPlayers }}</td>
              <td>{{ formatCurrency(round.totalPot) }}</td>
              <td><span :class="statusBadgeClass(round.status)">{{ round.status }}</span></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { storeToRefs } from 'pinia';
import { useRoundsStore } from '@/stores/rounds';

const props = defineProps({
  groupId: {
    type: String,
    required: true,
  },
});

const router = useRouter();
const roundsStore = useRoundsStore();
const { groupRoundHistory, isLoadingGroupRoundHistory, groupRoundHistoryError } = storeToRefs(roundsStore);

onMounted(() => {
  roundsStore.fetchGroupRoundHistory(props.groupId);
});

const navigateToRound = (roundId: string) => {
  router.push({ name: 'round-overview', params: { roundId } });
};

const formatCurrency = (value: number) => {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
  }).format(value);
};

const statusBadgeClass = (status: string) => {
  switch (status) {
    case 'Finalized':
      return 'badge bg-success';
    case 'Completed':
      return 'badge bg-info';
    case 'InProgress':
      return 'badge bg-primary';
    case 'SetupComplete':
      return 'badge bg-secondary';
    case 'PendingSetup':
      return 'badge bg-warning text-dark';
    default:
      return 'badge bg-light text-dark';
  }
};
</script>

<style scoped>
.table-hover tbody tr:hover {
  background-color: #f5f5f5;
}
</style>
