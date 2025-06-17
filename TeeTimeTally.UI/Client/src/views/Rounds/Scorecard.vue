<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import type { PropType } from 'vue';
import type { GetRoundByIdResponse, TeamHoleScoreRequest } from '@/models/round';

const props = defineProps({
  round: {
    type: Object as PropType<GetRoundByIdResponse>,
    required: true,
  },
  readonly: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits(['update-scores', 'update-ties']);

// --- LOCAL STATE ---
const localScores = ref<Record<string, Record<number, number | null>>>({});
const dirtyScores = ref<Record<string, Record<number, number>>>({});

// --- COMPUTED PROPERTIES ---
const isDirty = computed(() => Object.keys(dirtyScores.value).length > 0);

const teamScores = computed(() => {
    const scores: Record<string, { front: number, back: number, total: number }> = {};
    for (const team of props.round.teams) {
        let front = 0;
        let back = 0;
        for (let i = 1; i <= 9; i++) {
            front += localScores.value[team.teamId]?.[i] ?? 0;
        }
        for (let i = 10; i <= 18; i++) {
            back += localScores.value[team.teamId]?.[i] ?? 0;
        }
        scores[team.teamId] = { front, back, total: front + back };
    }
    return scores;
});

const tiedTeamIds = computed(() => {
    // Only calculate ties if all scores are in, otherwise it's premature.
    const isReadyForTieCheck = props.round.status === 'Completed' || props.round.status === 'Finalized';
    if (!isReadyForTieCheck) return new Set<string>();

    const totals = Object.values(teamScores.value).map(s => s.total);
    if (totals.length === 0) return new Set<string>();

    const minScore = Math.min(...totals.filter(t => t > 0)); // Only consider teams that have scored
    const tiedTeams = Object.entries(teamScores.value)
        .filter(([_, score]) => score.total > 0 && score.total === minScore)
        .map(([teamId, _]) => teamId);

    return tiedTeams.length > 1 ? new Set(tiedTeams) : new Set<string>();
});


// --- METHODS ---
const getScore = (teamId: string, holeNumber: number): number | null | undefined => {
    return localScores.value[teamId]?.[holeNumber];
}

function onScoreInput(teamId: string, holeNumber: number, event: Event) {
    if (props.readonly) return;

    const input = event.target as HTMLInputElement;
    const originalScore = props.round.scores.find(s => s.teamId === teamId && s.holeNumber === holeNumber)?.score;
    const value = input.value;

    if (value === '') {
        localScores.value[teamId][holeNumber] = null;
    } else {
        const score = parseInt(value, 10);
        if (!isNaN(score) && score > 0) {
            localScores.value[teamId][holeNumber] = score;

            if (score !== originalScore) {
                 if (!dirtyScores.value[teamId]) {
                    dirtyScores.value[teamId] = {};
                }
                dirtyScores.value[teamId][holeNumber] = score;
            } else {
                if (dirtyScores.value[teamId]?.[holeNumber]) {
                    delete dirtyScores.value[teamId][holeNumber];
                    if(Object.keys(dirtyScores.value[teamId]).length === 0) {
                        delete dirtyScores.value[teamId];
                    }
                }
            }
        }
    }
}

function saveAllChanges() {
    if (!isDirty.value) return;

    const payload: TeamHoleScoreRequest[] = [];
    for (const teamId in dirtyScores.value) {
        for (const holeNumberStr in dirtyScores.value[teamId]) {
            const holeNumber = parseInt(holeNumberStr, 10);
            payload.push({
                teamId,
                holeNumber: holeNumber,
                score: dirtyScores.value[teamId][holeNumber],
            });
        }
    }
    emit('update-scores', payload);
}

function markAsPristine() {
    dirtyScores.value = {};
}

// --- WATCHERS ---
watch(() => props.round, (newRound) => {
    const scoresMap: Record<string, Record<number, number | null>> = {};
    if (newRound && newRound.teams) {
        for (const team of newRound.teams) {
            scoresMap[team.teamId] = {};
        }
    }
    if (newRound && newRound.scores) {
        for (const score of newRound.scores) {
            scoresMap[score.teamId][score.holeNumber] = score.score;
        }
    }
    localScores.value = scoresMap;
    markAsPristine();
}, { immediate: true, deep: true });

watch(tiedTeamIds, (newTies) => {
    emit('update-ties', newTies);
}, { immediate: true });


// --- EXPOSE ---
defineExpose({
    isDirty,
    saveAllChanges,
    markAsPristine,
});

</script>

<template>
  <div class="card">
    <div class="card-header scorecard-header">
      <h3>Scorecard</h3>
    </div>
    <div class="card-body table-responsive">
      <table class="table table-bordered table-hover text-center" @keydown.enter.prevent="saveAllChanges">
        <thead class="scorecard-header">
          <tr>
            <th class="text-start">Team</th>
            <th v-for="n in 9" :key="n">{{ n }}</th>
            <th class="table-info">Out</th>
            <th v-for="n in 9" :key="n+9">{{ n + 9 }}</th>
            <th class="table-info">In</th>
            <th class="table-primary">Total</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(team, teamIndex) in round.teams" :key="team.teamId">
            <td class="text-start">
              <div class="fw-bold">{{ team.teamNameOrNumber }}</div>
              <small class="text-muted">{{ team.members.map(m => m.fullName).join(', ') }}</small>
            </td>
            <!-- Front 9 -->
            <td v-for="holeNumber in 9" :key="`f-${holeNumber}`">
              <input type="number" class="form-control form-control-sm text-center" :value="getScore(team.teamId, holeNumber)" @input="onScoreInput(team.teamId, holeNumber, $event)" :readonly="readonly" :tabindex="(teamIndex * 18) + holeNumber" min="1"/>
            </td>
            <td class="table-info fw-bold">{{ teamScores[team.teamId]?.front || 0 }}</td>
            <!-- Back 9 -->
            <td v-for="holeNumber in 9" :key="`b-${holeNumber}`">
              <input type="number" class="form-control form-control-sm text-center" :value="getScore(team.teamId, holeNumber + 9)" @input="onScoreInput(team.teamId, holeNumber + 9, $event)" :readonly="readonly" :tabindex="(teamIndex * 18) + holeNumber + 9" min="1"/>
            </td>
            <td class="table-info fw-bold">{{ teamScores[team.teamId]?.back || 0 }}</td>
            <!-- Total -->
            <td class="table-primary fw-bold" :class="{ 'bg-danger text-white': tiedTeamIds.has(team.teamId) }">
                {{ teamScores[team.teamId]?.total || 0 }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
th, td {
  min-width: 55px;
  vertical-align: middle;
}
th.text-start, td.text-start {
    min-width: 200px;
}
.scorecard-header {
    background-color: #198754; /* Green */
    color: #ffc107; /* Yellow */
}
input[type="number"] {
  -moz-appearance: textfield;
}
input::-webkit-outer-spin-button,
input::-webkit-inner-spin-button {
  -webkit-appearance: none;
  margin: 0;
}
</style>
