<template>
  <div class="home-view py-4">
    <div class="container">
      <div class="p-5 mb-4 bg-light rounded-3 shadow-sm">
        <div class="container-fluid py-3">
          <h1 class="display-5 fw-bold">Welcome to TeeTimeTally!</h1>
          <p class="col-md-10 fs-5">
            Manage your group golf rounds, track scores, and automate payouts with ease.
          </p>
          <hr class="my-4">
          <div v-if="authenticationStore.isAuthenticated">
            <p class="lead mb-3">Hello, {{ authenticationStore.user?.fullName }}!</p>
            <RouterLink
              :to="{ name: 'create-round' }"
              class="btn btn-primary btn-lg me-2"
              role="button"
            >
              <i class="bi bi-plus-circle-fill me-2"></i>Create New Round
            </RouterLink>
             <RouterLink
              :to="{ name: 'groups-index' }"
              class="btn btn-outline-secondary btn-lg"
              role="button"
            >
              View My Groups
            </RouterLink>
          </div>
          <div v-else>
            <p class="lead">Log in or register to get started.</p>
            <button @click="authenticationStore.login()" class="btn btn-primary btn-lg me-2">Login</button>
            <button @click="authenticationStore.register()" class="btn btn-secondary btn-lg">Register</button>
          </div>
        </div>
      </div>

      <div v-if="authenticationStore.isAuthenticated" class="mt-5">
        <h2 class="mb-3 border-bottom pb-2">Your Open Rounds</h2>

        <div v-if="roundsStore.isLoadingOpenRounds" class="text-center my-5">
          <div class="spinner-border text-primary" role="status" style="width: 3rem; height: 3rem;">
            <span class="visually-hidden">Loading open rounds...</span>
          </div>
          <p class="mt-2">Fetching your open rounds...</p>
        </div>

        <div v-else-if="roundsStore.openRoundsError" class="alert alert-danger" role="alert">
          <strong>Error:</strong> {{ roundsStore.openRoundsError.message || 'Could not load open rounds.' }}
          <button @click="roundsStore.fetchOpenRounds()" class="btn btn-sm btn-danger-outline ms-2">Try Again</button>
        </div>

        <div v-else-if="roundsStore.openRounds.length > 0" class="list-group shadow-sm">
          <div
            v-for="round in roundsStore.openRounds"
            :key="round.roundId"
            class="list-group-item list-group-item-action flex-column align-items-start mb-2 rounded"
            @click="navigateToRound(round.roundId)"
            style="cursor: pointer;"
            >
            <div class="d-flex w-100 justify-content-between">
              <h5 class="mb-1 text-primary">{{ round.groupName }} at {{ round.courseName }}</h5>
              <small class="text-muted">{{ formatDate(round.roundDate) }}</small>
            </div>
            <p class="mb-1">
              Status: <span :class="statusBadgeClass(round.status)">{{ formatStatus(round.status) }}</span>
              <span v-if="round.numPlayers !== null && round.numPlayers > 0" class="ms-3">
                Players: {{ round.numPlayers }}
              </span>
            </p>
            <small class="text-muted">Round ID: {{ round.roundId }}</small>
          </div>
        </div>

        <div v-else class="alert alert-info" role="alert">
          You have no open rounds currently. Why not <RouterLink v-if="authenticationStore.hasPermission(Permissions.CreateRounds)" :to="{ name: 'create-round' }">create one</RouterLink><span v-else>start one</span>?
        </div>
      </div>
       <div v-else-if="authenticationStore.isAuthenticated && !authenticationStore.hasPermission(Permissions.ReadGroupRounds)" class="mt-5 alert alert-warning">
        You do not have permission to view open rounds.
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, computed } from 'vue';
import { RouterLink, useRouter } from 'vue-router';
import { useAuthenticationStore } from '@/stores/authentication';
import { useRoundsStore } from '@/stores/rounds'; // Import the new rounds store
import { Permissions } from '@/models/auth/permissions';

const authenticationStore = useAuthenticationStore();
const roundsStore = useRoundsStore();
const router = useRouter();

const user = computed(() => authenticationStore.user);

onMounted(() => {
  if (authenticationStore.isAuthenticated) {
    roundsStore.fetchOpenRounds();
  }
});

function formatDate(dateString: string): string {
  if (!dateString) return 'Date N/A';
  try {
    return new Date(dateString).toLocaleDateString(undefined, {
      year: 'numeric', month: 'long', day: 'numeric'
    });
  } catch (e) {
    return dateString; // fallback
  }
}

function formatStatus(status: string): string {
  // Add more human-friendly status names if needed
  const statusMap: { [key: string]: string } = {
    'PendingSetup': 'Pending Setup',
    'SetupComplete': 'Setup Complete',
    'InProgress': 'In Progress',
    'Completed': 'Completed (Awaiting Finalization)',
    // 'Finalized': 'Finalized' // Not typically an "open" round
  };
  return statusMap[status] || status;
}

function statusBadgeClass(status: string): string {
  const baseClass = 'badge rounded-pill';
  switch (status) {
    case 'PendingSetup': return `${baseClass} bg-secondary`;
    case 'SetupComplete': return `${baseClass} bg-info text-dark`;
    case 'InProgress': return `${baseClass} bg-warning text-dark`;
    case 'Completed': return `${baseClass} bg-success`;
    default: return `${baseClass} bg-light text-dark`;
  }
}

function navigateToRound(roundId: string) {
  // Placeholder for navigation. You'll need a route like '/rounds/:id'
  // For now, it can log or do nothing.
  // Example: router.push({ name: 'round-details', params: { id: roundId } });
  console.log('Navigate to round:', roundId);
  // Replace with actual navigation once the round detail page/route exists
  // e.g., router.push({ name: 'round-scoring', params: { roundId: roundId } });
  // Or if you have a generic round detail view:
  // router.push({ name: 'round-view', params: { id: roundId } });
  // For now, let's assume you might want to go to a scoring page for open rounds.
  // If your "StartRoundRequest" endpoint is what you use to setup, that's different.
  // This page is for *viewing* open rounds.
  // A common action for an "InProgress" or "Completed" round might be to go to its scoring page.
  // A "SetupComplete" round might also go to a scoring/management page.
  // A "PendingSetup" round might go to a setup page.
  // Let's assume a generic 'round-overview' or similar route for now.
  router.push({ name: 'round-overview', params: { roundId } }); // Adjust route name as needed
}
</script>

<style scoped>
.home-view .list-group-item-action:hover {
  background-color: #f8f9fa; /* Light hover effect */
}
.badge {
  font-size: 0.85em;
}
/* Add Bootstrap Icons CSS if you use them, e.g., in index.html or main.ts */
/* @import url("https://cdn.jsdelivr.net/npm/bootstrap-icons@1.10.0/font/bootstrap-icons.css"); */
</style>
