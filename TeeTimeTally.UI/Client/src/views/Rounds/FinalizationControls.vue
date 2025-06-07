<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import type { PropType } from 'vue';
import type { GetRoundByIdResponse } from '@/models/round';

const props = defineProps({
    round: {
        type: Object as PropType<GetRoundByIdResponse>,
        required: true
    },
    tiedTeamIds: {
        type: Set as PropType<Set<string>>,
        required: true
    }
});

const emit = defineEmits(['finalize-round']);

const cthWinnerGolferId = ref('');
const overallWinnerTeamIdOverride = ref<string>('');

const allParticipants = computed(() => {
    return props.round.teams.flatMap(team => team.members);
});

const tiedTeams = computed(() => {
    return props.round.teams.filter(team => props.tiedTeamIds.has(team.teamId));
});

const isTieDetected = computed(() => props.tiedTeamIds.size > 1);

const canFinalize = computed(() => {
    if (!cthWinnerGolferId.value) return false;
    if (isTieDetected.value && !overallWinnerTeamIdOverride.value) return false;
    return true;
});


function finalize() {
    if (!canFinalize.value) {
        alert('Please select a CTH winner and resolve any ties before finalizing.');
        return;
    }
    emit('finalize-round', {
        cthWinnerGolferId: cthWinnerGolferId.value,
        overallWinnerTeamIdOverride: isTieDetected.value ? overallWinnerTeamIdOverride.value : undefined
    });
}

// Reset tie-breaker selection if the ties change
watch(() => props.tiedTeamIds, () => {
    overallWinnerTeamIdOverride.value = '';
});

</script>

<template>
    <div class="card bg-light">
        <div class="card-header">
            <h3>Finalize Round</h3>
        </div>
        <div class="card-body">
            <div class="row align-items-end">
                <div class="col-md-5 mb-3">
                    <label for="cth-winner" class="form-label fw-bold">Closest to the Hole Winner (Hole {{ round.courseCthHoleNumber }})</label>
                    <select id="cth-winner" class="form-select" v-model="cthWinnerGolferId">
                        <option disabled value="">Select a player...</option>
                        <option v-for="player in allParticipants" :key="player.golferId" :value="player.golferId">
                            {{ player.fullName }}
                        </option>
                    </select>
                </div>

                <!-- Tie Breaker UI -->
                <div v-if="isTieDetected" class="col-md-5 mb-3">
                     <label for="tie-breaker-winner" class="form-label fw-bold text-danger">Resolve Tie for 1st Place</label>
                     <select id="tie-breaker-winner" class="form-select border-danger" v-model="overallWinnerTeamIdOverride">
                        <option disabled value="">Select a winning team...</option>
                        <option v-for="team in tiedTeams" :key="team.teamId" :value="team.teamId">
                            {{ team.teamNameOrNumber }}
                        </option>
                    </select>
                </div>

                <div class="col-md-2 mb-3 d-grid">
                    <button class="btn btn-success" @click="finalize" :disabled="!canFinalize">
                        <i class="bi bi-check-circle-fill me-2"></i>Finalize
                    </button>
                </div>
            </div>
        </div>
    </div>
</template>
