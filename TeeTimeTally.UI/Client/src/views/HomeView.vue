<script setup lang="ts">
import { onMounted, computed, watchEffect } from 'vue'; // Import watchEffect
import { RouterLink, useRouter } from 'vue-router';
import { useAuthenticationStore } from '@/stores/authentication';
import { useRoundsStore } from '@/stores/rounds';
import { useStatusBadges } from '@/composables/useStatusBadges';
import { Permissions } from '@/models/auth/permissions';


const authenticationStore = useAuthenticationStore();
const roundsStore = useRoundsStore();
const router = useRouter();
const { getStatusBadgeClass, formatStatusText } = useStatusBadges(); // Get both functions

// user computed property is not strictly needed here if only used in template via store directly
// const user = computed(() => authenticationStore.user);

// Use watchEffect to react to changes in authentication state
watchEffect(() => {
  // Fetch open rounds only when:
  // 1. Authentication is not loading
  // 2. User is authenticated
  // 3. User has permission to read group rounds
  if (!authenticationStore.isLoading &&
      authenticationStore.isAuthenticated &&
      authenticationStore.hasPermission(Permissions.ReadGroupRounds)) {
    roundsStore.fetchOpenRounds();
  } else if (!authenticationStore.isLoading && authenticationStore.isAuthenticated && !authenticationStore.hasPermission(Permissions.ReadGroupRounds)) {
    // Optional: Log or handle the case where user is authenticated but lacks permission
    console.warn("User is authenticated but lacks permission to read group rounds.");
    // You might want to clear rounds or show a specific message if roundsStore.openRounds could have stale data
    roundsStore.openRounds = []; // Clear rounds if permission is lost or not granted
  } else if (!authenticationStore.isLoading && !authenticationStore.isAuthenticated) {
    // Optional: Clear rounds if user logs out or session expires
     roundsStore.openRounds = [];
  }
});

// Helper function to format date strings for display
function formatDate(dateString: string): string {
  if (!dateString) return 'Date N/A';
  try {
    return new Date(dateString).toLocaleDateString(undefined, {
      year: 'numeric', month: 'long', day: 'numeric'
    });
  } catch (e) {
    console.warn("Error formatting date:", dateString, e);
    return dateString; // fallback to original string if formatting fails
  }
}


// Function to navigate to a specific round's overview page
function navigateToRound(roundId: string) {
  if (roundId) {
    router.push({ name: 'round-overview', params: { roundId } });
  } else {
    console.error("navigateToRound called with invalid roundId:", roundId);
  }
}
</script>

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
              v-if="authenticationStore.hasPermission(Permissions.CreateRounds)"
              :to="{ name: 'create-round' }"
              class="btn btn-primary btn-lg me-2"
              role="button"
            >
              <i class="bi bi-plus-circle-fill me-2"></i>Create New Round
            </RouterLink>
             <RouterLink
              v-if="authenticationStore.hasPermission(Permissions.ReadGroups)"
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
              Status: <span :class="getStatusBadgeClass(round.status)">{{ formatStatusText(round.status) }}</span>
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

<style scoped>
.home-view .list-group-item-action:hover {
  background-color: #f8f9fa; /* Light hover effect */
}
.badge {
  font-size: 0.85em;
}
</style>
