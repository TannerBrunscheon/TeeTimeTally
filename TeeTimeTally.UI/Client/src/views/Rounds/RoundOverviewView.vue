<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { useRoute } from 'vue-router';
import { useRoundsStore } from '@/stores/rounds';
import { useStatusBadges } from '@/composables/useStatusBadges';
import type { GetRoundByIdResponse, TeamHoleScoreRequest } from '@/models/round';
import Scorecard from '@/views/Rounds/Scorecard.vue';
import FinalizationControls from '@/views/Rounds/FinalizationControls.vue';
import PayoutSummary from '@/views/Rounds/PayoutSummary.vue';
import { ErrorType } from '@/primitives/error';

// --- PROPS, REFS, & STORE ---
const props = defineProps<{
  roundId: string;
}>();

const roundsStore = useRoundsStore();
const scorecardRef = ref<InstanceType<typeof Scorecard> | null>(null);
  const { getStatusBadgeClass, formatStatusText } = useStatusBadges(); // Get both functions

// --- STATE ---
const isLoading = ref(true);
const errorMessage = ref<string | null>(null);
const successMessage = ref<string | null>(null);
const tiedTeamIds = ref<Set<string>>(new Set());

// --- COMPUTED ---
const round = computed(() => roundsStore.currentRound);

const isDirty = computed(() => scorecardRef.value?.isDirty || false);

const canEditScores = computed(() => {
    return round.value?.status === 'SetupComplete' || round.value?.status === 'InProgress' || round.value?.status === 'Completed';
});

const canFinalize = computed(() => {
    return round.value?.status === 'Completed';
});

const isFinalized = computed(() => {
    return round.value?.status === 'Finalized';
});


// --- METHODS ---
function handleTiesUpdated(newTiedTeamIds: Set<string>) {
    tiedTeamIds.value = newTiedTeamIds;
}

async function handleBatchScoreUpdate(scoresToSubmit: TeamHoleScoreRequest[]) {
    if (!round.value || scoresToSubmit.length === 0) return;

    errorMessage.value = null;
    successMessage.value = null;

    const result = await roundsStore.submitScores(round.value.roundId, scoresToSubmit);

    if (result.isFailure) {
        errorMessage.value = result.error?.message || "Failed to save scores.";
    } else {
        successMessage.value = result.value?.message || "Scores saved successfully!";
        await roundsStore.fetchRoundById(props.roundId);
        scorecardRef.value?.markAsPristine();
        setTimeout(() => successMessage.value = null, 3000);
    }
}

function triggerSave() {
    scorecardRef.value?.saveAllChanges();
}

async function handleFinalize(payload: { cthWinnerGolferId: string; overallWinnerTeamIdOverride?: string }) {
     if (!round.value) return;
     errorMessage.value = null;
     const result = await roundsStore.completeRound(round.value.roundId, payload);

     if (result.isFailure) {
        errorMessage.value = result.error?.message || "An unknown error occurred during finalization.";
        if (result.error?.errorType === ErrorType.Conflict) {
             console.error("Tie detected:", result.error.details);
             errorMessage.value = `Tie for overall winner detected. Please select a winner to break the tie.`;
             // The FinalizationControls component will now show the tie-breaker UI.
        }
     } else {
        await roundsStore.fetchRoundById(props.roundId);
     }
}

// --- LIFECYCLE ---
onMounted(async () => {
  isLoading.value = true;
  errorMessage.value = null;
  const result = await roundsStore.fetchRoundById(props.roundId);
  if (result.isFailure) {
    errorMessage.value = result.error?.message || `Failed to load round with ID ${props.roundId}.`;
  }
  isLoading.value = false;
});
</script>

<template>
  <div class="container mt-4">
    <div v-if="isLoading" class="text-center p-5">
      <div class="spinner-border" role="status">
        <span class="visually-hidden">Loading Round...</span>
      </div>
    </div>

    <div v-else-if="errorMessage" class="alert alert-danger">
      <p class="mb-0">
        <i class="bi bi-exclamation-triangle-fill me-2"></i>{{ errorMessage }}
      </p>
    </div>

    <div v-else-if="round">
      <!-- Round Header -->
      <div class="card mb-4">
        <div class="card-header d-flex justify-content-between align-items-center">
            <h2 class="mb-0">{{ round.courseName }} - {{ new Date(round.roundDate).toLocaleDateString() }}</h2>
            <span :class="getStatusBadgeClass(round.status)" class="fs-5">{{ formatStatusText(round.status) }}</span>
        </div>
        <div class="card-body">
            <h5 class="card-title">{{ round.groupName }}</h5>
            <p class="card-text">
                {{ round.numPlayers }} Players |
                Total Pot: <span class="fw-bold">{{ round.totalPot.toLocaleString('en-US', { style: 'currency', currency: 'USD' }) }}</span>
            </p>
        </div>
      </div>

       <!-- Dirty State Save Bar -->
       <div v-if="isDirty" class="alert alert-info d-flex justify-content-between align-items-center sticky-top mb-4">
            <span>You have unsaved changes.</span>
            <button class="btn btn-primary" @click="triggerSave" :disabled="roundsStore.isLoadingSubmitScores">
                <span v-if="roundsStore.isLoadingSubmitScores" class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
                Save Changes
            </button>
       </div>
        <div v-if="successMessage" class="alert alert-success">
            {{ successMessage }}
        </div>


      <!-- Finalization Controls (Only for 'Completed' status) -->
      <FinalizationControls
        v-if="canFinalize"
        :round="round"
        :tied-team-ids="tiedTeamIds"
        @finalize-round="handleFinalize"
        class="mb-4"
      />

      <!-- Payout Summary (Only for 'Finalized' status) -->
       <PayoutSummary
        v-if="isFinalized"
        :round="round"
        class="mb-4"
       />

      <!-- Scorecard -->
      <Scorecard
        ref="scorecardRef"
        :round="round"
        :readonly="!canEditScores"
        @update-scores="handleBatchScoreUpdate"
        @update-ties="handleTiesUpdated"
      />

    </div>

    <div v-else class="alert alert-warning">
        Round data could not be loaded.
    </div>
  </div>
</template>

<style scoped>
.badge {
    text-transform: uppercase;
    letter-spacing: .5px;
}
.sticky-top {
    top: 1rem;
    z-index: 1020;
}
</style>
