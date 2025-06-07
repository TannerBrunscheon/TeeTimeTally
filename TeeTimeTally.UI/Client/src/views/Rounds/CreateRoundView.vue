<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useGroupsStore } from '@/stores/groups'
import { useCoursesStore } from '@/stores/courses'
import { useRoundsStore } from '@/stores/rounds'
import type { Group, GroupMember } from '@/models/group'
import type { CourseSummary } from '@/models/course'
import type { TeamDefinitionRequest } from '@/models/round'

// --- HOOKS and STORES ---
const router = useRouter()
const groupsStore = useGroupsStore()
const coursesStore = useCoursesStore()
const roundsStore = useRoundsStore()

// --- STATE ---
const isLoading = ref(true)
const errorMessage = ref<string | null>(null)

// Data for dropdowns
const userGroups = ref<Group[]>([])
const groupMembers = ref<GroupMember[]>([])
const availableCourses = ref<CourseSummary[]>([])

// Form state
const selectedGroupId = ref<string>('')
const selectedCourseId = ref<string>('')
const roundDate = ref(new Date().toISOString().split('T')[0])
const selectedGolferIds = ref<string[]>([])
const teams = ref<TeamDefinitionRequest[]>([])

// --- COMPUTED PROPERTIES ---
const selectedGroup = computed((): Group | undefined => {
  return userGroups.value.find((g) => g.id === selectedGroupId.value)
})

const selectedGolfers = computed((): GroupMember[] => {
  return groupMembers.value.filter((member) => selectedGolferIds.value.includes(member.golferId))
})

const unassignedGolfers = computed((): GroupMember[] => {
    const assignedGolferIds = new Set(teams.value.flatMap(t => t.golferIdsInTeam));
    return selectedGolfers.value.filter(g => !assignedGolferIds.has(g.golferId));
});

const isFormValid = computed(() => {
  // Basic checks first
  if (!selectedGroupId.value || !selectedCourseId.value) return false;
  if (selectedGolfers.value.length < 6) return false;

  // Must have prepared teams
  if (teams.value.length === 0) return false;

  const playersOnTeams = teams.value.flatMap((t) => t.golferIdsInTeam);

  // Every selected player must be assigned, and no team slot can be left empty.
  return playersOnTeams.length === selectedGolfers.value.length && !playersOnTeams.includes('');
})


// --- METHODS ---

/**
 * Gets the list of available golfers for a specific dropdown slot.
 * It includes all unassigned golfers plus the golfer currently in that slot.
 * This ensures that when a player is selected, they are removed from other
 * lists but remain selectable in their current dropdown.
 * @param currentGolferIdInSlot The ID of the golfer currently assigned to this team slot.
 */
function availableOptionsFor(currentGolferIdInSlot: string): GroupMember[] {
  const options = [...unassignedGolfers.value];

  // If a golfer is already selected in this slot, and they are NOT in the unassigned list
  // (which they shouldn't be), add them to the options so they can be re-selected or appear as the current value.
  if (currentGolferIdInSlot && !options.some(g => g.golferId === currentGolferIdInSlot)) {
    const currentGolfer = selectedGolfers.value.find(g => g.golferId === currentGolferIdInSlot);
    if (currentGolfer) {
      options.push(currentGolfer);
      // Sort for a consistent order in the dropdown
      options.sort((a, b) => a.fullName.localeCompare(b.fullName));
    }
  }

  return options;
}

/**
 * Resets the form state when the group changes.
 */
function resetRoundDetails() {
  selectedCourseId.value = selectedGroup.value?.defaultCourseId || ''
  selectedGolferIds.value = []
  teams.value = []
  errorMessage.value = null
}

/**
 * Creates empty team structures based on the number of selected players.
 * This is for manual assignment with dropdowns.
 */
function prepareTeamSlots() {
  errorMessage.value = null
  const numPlayers = selectedGolfers.value.length
  if (numPlayers < 6) {
    errorMessage.value = 'Please select at least 6 players to form teams.'
    return
  }

  const newTeams: TeamDefinitionRequest[] = []
  let teamNumber = 1

  const numTeams = Math.floor(numPlayers / 2);
  const hasTeamOfThree = numPlayers % 2 !== 0;

  for (let i = 0; i < numTeams; i++) {
    const isFirstAndOdd = i === 0 && hasTeamOfThree;
    const teamSize = isFirstAndOdd ? 3 : 2;
    newTeams.push({
      teamNameOrNumber: `Team ${teamNumber++}`,
      golferIdsInTeam: Array(teamSize).fill('') // Create empty slots
    })
  }

  teams.value = newTeams
}


/**
 * Handles the final submission to create the round.
 */
async function handleCreateRound() {
  if (!isFormValid.value) {
    errorMessage.value = 'Please ensure all fields are complete and all selected players are assigned to a team.'
    return
  }
  errorMessage.value = null

  const result = await roundsStore.startRound(selectedGroupId.value, {
    courseId: selectedCourseId.value,
    roundDate: roundDate.value,
    allParticipatingGolferIds: selectedGolferIds.value,
    teams: teams.value
  })

  if (result.isSuccess && result.value) {
    router.push({ name: 'round-overview', params: { roundId: result.value.roundId } })
  } else {
    errorMessage.value = result.error?.message || 'An unknown error occurred while creating the round.'
  }
}

// --- WATCHERS ---
watch(selectedGroupId, async (newGroupId) => {
  if (newGroupId) {
    isLoading.value = true
    resetRoundDetails()
    const membersResult = await groupsStore.fetchGroupMembers(newGroupId)
    if (membersResult.isSuccess && membersResult.value) {
      groupMembers.value = membersResult.value
    } else {
      errorMessage.value = membersResult.error?.message || 'Failed to load group members.'
    }
    isLoading.value = false
  } else {
    groupMembers.value = []
    resetRoundDetails()
  }
})

watch(selectedGolferIds, () => {
  // Reset teams if the player selection changes
  teams.value = []
})

// --- LIFECYCLE HOOKS ---
onMounted(async () => {
  isLoading.value = true
  errorMessage.value = null

  const [groupsResult, coursesResult] = await Promise.all([
    groupsStore.fetchAllGroups(),
    coursesStore.fetchAllCourses()
  ])

  if (groupsResult.isSuccess && groupsResult.value) {
    userGroups.value = groupsResult.value
  } else {
    errorMessage.value = groupsResult.error?.message || 'Failed to load your groups.'
  }

  if (coursesResult.isSuccess && coursesResult.value) {
    availableCourses.value = coursesResult.value
  } else {
    errorMessage.value = coursesResult.error?.message || 'Failed to load available courses.'
  }

  isLoading.value = false
})

/**
 * Helper to get golfer name from ID for display purposes.
 */
function getGolferName(golferId: string): string {
  return groupMembers.value.find((g) => g.golferId === golferId)?.fullName || 'Unknown Golfer'
}
</script>

<template>
  <div class="container mt-4">
    <h1 class="mb-4">Create a New Round</h1>

    <!-- Step 1: Select Group -->
    <div class="card mb-4">
      <div class="card-header">
        <h3>Step 1: Select a Group</h3>
      </div>
      <div class="card-body">
        <div v-if="isLoading && userGroups.length === 0" class="text-center">
          <div class="spinner-border" role="status"><span class="visually-hidden">Loading...</span></div>
        </div>
        <div v-else>
          <label for="group-select" class="form-label">Group</label>
          <select id="group-select" class="form-select" v-model="selectedGroupId">
            <option disabled value="">Please select a group to start</option>
            <option v-for="group in userGroups" :key="group.id" :value="group.id">
              {{ group.name }}
            </option>
          </select>
        </div>
      </div>
    </div>

    <!-- The rest of the form is conditional on a group being selected -->
    <div v-if="selectedGroupId">
      <div v-if="isLoading" class="text-center">
         <div class="spinner-border text-primary" role="status"><span class="visually-hidden">Loading group details...</span></div>
      </div>
       <div v-else>
        <!-- Step 2: Round Details -->
        <div class="card mb-4">
          <div class="card-header">
            <h3>Step 2: Round Details</h3>
          </div>
          <div class="card-body">
            <div class="row">
              <div class="col-md-6 mb-3">
                <label for="course-select" class="form-label">Golf Course</label>
                <select id="course-select" class="form-select" v-model="selectedCourseId">
                  <option disabled value="">Please select a course</option>
                  <option v-for="course in availableCourses" :key="course.id" :value="course.id">
                    {{ course.name }}
                  </option>
                </select>
              </div>
              <div class="col-md-6 mb-3">
                <label for="round-date" class="form-label">Date</label>
                <input type="date" id="round-date" class="form-control" v-model="roundDate" />
              </div>
            </div>
          </div>
        </div>

        <!-- Step 3: Select Players -->
        <div class="card mb-4">
          <div class="card-header d-flex justify-content-between align-items-center">
             <h3>Step 3: Select Players ({{ selectedGolferIds.length }} selected)</h3>
             <router-link :to="{ name: 'group-detail', params: { groupId: selectedGroupId } }" class="btn btn-outline-secondary btn-sm">
                <i class="bi bi-pencil-square me-1"></i> Edit Group Members
             </router-link>
          </div>
          <div class="card-body">
            <p class="text-muted">A minimum of 6 players are required. (REQ-RS-008)</p>
            <div class="row row-cols-2 row-cols-md-3 row-cols-lg-4 g-2">
              <div v-for="member in groupMembers" :key="member.golferId" class="col">
                <div class="form-check form-check-inline">
                  <input class="form-check-input" type="checkbox" :id="`golfer-${member.golferId}`" :value="member.golferId" v-model="selectedGolferIds" />
                  <label class="form-check-label" :for="`golfer-${member.golferId}`">{{ member.fullName }}</label>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Step 4: Form Teams -->
        <div class="card mb-4">
          <div class="card-header">
            <h3>Step 4: Form Teams</h3>
          </div>
          <div class="card-body">
            <button class="btn btn-secondary mb-3" @click="prepareTeamSlots" :disabled="selectedGolferIds.length < 6">
              <i class="bi bi-people-fill me-2"></i>Prepare Team Slots
            </button>

            <div v-if="teams.length > 0" class="row g-3">
              <div v-for="(team, teamIndex) in teams" :key="teamIndex" class="col-md-6 col-lg-4">
                <div class="card h-100">
                  <div class="card-header">
                    <input type="text" class="form-control form-control-sm" v-model="team.teamNameOrNumber" />
                  </div>
                  <ul class="list-group list-group-flush">
                    <li v-for="(_, playerIndex) in team.golferIdsInTeam" :key="playerIndex" class="list-group-item">
                      <select class="form-select form-select-sm" v-model="team.golferIdsInTeam[playerIndex]">
                        <option value="">-- Select Player --</option>
                        <option v-for="golfer in availableOptionsFor(team.golferIdsInTeam[playerIndex])" :key="golfer.golferId" :value="golfer.golferId">
                            {{ golfer.fullName }}
                        </option>
                      </select>
                    </li>
                  </ul>
                </div>
              </div>
            </div>
             <div v-else class="text-center text-muted p-3">
              <p>Select players and click "Prepare Team Slots" to begin forming teams.</p>
            </div>
          </div>
        </div>

        <!-- Submission Area -->
        <div class="d-grid gap-2">
          <div v-if="errorMessage" class="alert alert-danger">
              {{ errorMessage }}
          </div>
          <button class="btn btn-primary btn-lg" @click="handleCreateRound" :disabled="!isFormValid || roundsStore.isLoadingStartRound">
            <span v-if="roundsStore.isLoadingStartRound" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            <i v-else class="bi bi-flag-fill me-2"></i>
            Create Round
          </button>
        </div>
      </div>
    </div>
     <div v-else-if="!isLoading" class="text-center text-muted p-5 border rounded">
      <h2>Select a Group</h2>
      <p>Please select a group from the dropdown above to begin creating a new round.</p>
    </div>
  </div>
</template>

<style scoped>
.card-header h3 {
  margin-bottom: 0;
  font-size: 1.25rem;
}
</style>
