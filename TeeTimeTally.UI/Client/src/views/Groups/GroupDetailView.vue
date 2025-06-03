<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useGroupsStore } from '@/stores/groups';
import { useAuthenticationStore } from '@/stores/authentication';
import { useCourseStore } from '@/stores/courses';
import { useGolfersStore } from '@/stores/golfers';
import { Permissions } from '@/models/auth/permissions';
import { AppError, ErrorType } from '@/primitives/error'; // Import ErrorType
import type { Group, GroupFinancialConfiguration, GroupMember, UpdateGroupRequest, Golfer, Course } from '@/models'; // Import all necessary models

const groupsStore = useGroupsStore();
const authStore = useAuthenticationStore();
const courseStore = useCourseStore();
const golfersStore = useGolfersStore();
const route = useRoute();
const router = useRouter();

const groupId = ref<string | string[]>(route.params.groupId);
const group = computed(() => groupsStore.currentGroup);
const groupMembers = ref<GroupMember[]>([]); // Initialize with empty array
const availableCourses = ref<Course[]>([]); // Initialize with empty array for dropdown
const defaultCourseName = ref<string>('N/A'); // To display the default course name
const availableGolfers = ref<Golfer[]>([]); // Initialize with empty array

// State for editing group details
const isEditingGroup = ref(false);
const newGroupName = ref('');
const newDefaultCourseId = ref<string | null>(null);

// State for editing financial configuration
const isEditingFinancials = ref(false);
const newBuyInAmount = ref(0);
const newSkinValueFormula = ref('');
const newCthPayoutFormula = ref('');
const financialValidationErrors = ref<string[]>([]);
const isFormulasDirty = ref(false); // Track if formulas have been changed since last save/validation
const isValidationRunning = ref(false);
const validationResults = ref<ValidationResult[]>([]);
const showValidationDrawer = ref(false);
const overallValidationFailed = ref(false); // For pop-up alert
const showTemplateInputs = ref(false); // To toggle visibility of template inputs table

interface ValidationResult {
  playerCount: number;
  totalPot: number;
  calculatedSkinValue: number;
  totalPotentialSkins: number;
  calculatedCthPayout: number;
  remainingForWinner: number;
  isValid: boolean;
  errorMessage?: string;
}

// Data for template inputs table - Updated for NCalc syntax
const templateInputs = [
  { variable: '[roundPlayers]', description: 'The total number of players participating in the round (integer).' }
];

// State for adding members
const showAddMembersModal = ref(false);
const golferSearchTerm = ref('');
const selectedGolfersToAdd = ref<string[]>([]);
const addMembersError = ref<string | null>(null);

// State for removing members confirmation
const showConfirmRemoveModal = ref(false);
const golferToRemoveId = ref<string | null>(null);
const golferToRemoveName = ref('');

// State for changing scorer status confirmation
const showConfirmScorerStatusModal = ref(false);
const golferToChangeScorerStatusId = ref<string | null>(null);
const golferToChangeScorerStatusName = ref('');
const newScorerStatus = ref(false); // true for promote, false for demote

// Permissions
const canEditGroup = computed(() => authStore.hasPermission(Permissions.ManageGroupSettings));
const canManageMembers = computed(() => authStore.hasPermission(Permissions.ManageGroupMembers));
const canManageScorers = computed(() => authStore.hasPermission(Permissions.ManageGroupScorers));
const canManageFinances = computed(() => authStore.hasPermission(Permissions.ManageGroupFinances));

// Watch for changes in financial formula inputs to mark them as dirty
watch([newBuyInAmount, newSkinValueFormula, newCthPayoutFormula], () => {
  if (isEditingFinancials.value) {
    isFormulasDirty.value = true;
    overallValidationFailed.value = false; // Reset pop-up on formula change
    financialValidationErrors.value = []; // Clear previous errors
    validationResults.value = []; // Clear previous results
  }
});

// --- Data Loading ---
async function loadGroupData() {
  if (!groupId.value) {
    router.push({ name: 'groups-index' });
    return;
  }

  const groupResult = await groupsStore.fetchGroupById(groupId.value as string);
  if (groupResult.isFailure) {
    console.error('Failed to load group:', groupResult.error?.message);
    return;
  }

  // Load default course name if available
  if (group.value?.defaultCourseId) {
    const defaultCourseResult = await courseStore.getCourseById(group.value.defaultCourseId);
    if (defaultCourseResult.isSuccess && defaultCourseResult.value !== undefined) {
      defaultCourseName.value = defaultCourseResult.value.name;
      // Add the default course to availableCourses so it appears in the dropdown initially
      // if it's not already there from a search.
      if (!availableCourses.value.some(c => c.id === defaultCourseResult.value!.id)) {
        availableCourses.value.push(defaultCourseResult.value);
      }
    } else {
      console.error('Failed to load default course:', defaultCourseResult.error?.message);
      defaultCourseName.value = 'N/A (Error)';
    }
  } else {
    defaultCourseName.value = 'N/A';
  }

  const membersResult = await groupsStore.fetchGroupMembers(groupId.value as string);
  if (membersResult.isSuccess && membersResult.value !== undefined) {
    groupMembers.value = membersResult.value;
  } else {
    console.error('Failed to load group members:', membersResult.error?.message);
    groupMembers.value = []; // Ensure it's an empty array on error
  }
}

// Function to load courses for the dropdown (on demand)
async function loadCoursesForDropdown() {
  // Only load if not already loaded or if a search term is present
  if (availableCourses.value.length === 0 || isEditingGroup.value) { // Reload if editing starts or if empty
    const coursesResult = await courseStore.searchCourses({ limit: 10 }); // Load first 10 courses
    if (coursesResult.isSuccess && coursesResult.value !== undefined) {
      availableCourses.value = coursesResult.value;
    } else {
      console.error('Failed to load courses for dropdown:', coursesResult.error?.message);
      availableCourses.value = [];
    }
  }
}

onMounted(loadGroupData);
watch(() => route.params.groupId, (newId) => {
  if (newId) {
    groupId.value = newId;
    loadGroupData();
  }
});

// --- Group Editing ---
function startEditingGroupDetails() {
  if (group.value) {
    newGroupName.value = group.value.name;
    newDefaultCourseId.value = group.value.defaultCourseId;
    isEditingGroup.value = true;
    loadCoursesForDropdown(); // Load courses when starting edit
  }
}

async function saveGroupChanges() {
  if (!group.value) return;

  const updatePayload: UpdateGroupRequest = {
    name: newGroupName.value,
    defaultCourseId: newDefaultCourseId.value === '' ? null : newDefaultCourseId.value,
  };

  const result = await groupsStore.updateGroup(group.value.id, updatePayload);
  if (result.isSuccess) {
    isEditingGroup.value = false;
    await loadGroupData(); // Reload data to update default course name display
  } else {
    alert(`Error updating group: ${result.error?.message || 'Unknown error'}`);
  }
}

function cancelEditingGroupDetails() {
  isEditingGroup.value = false;
  newGroupName.value = '';
  newDefaultCourseId.value = null;
}

// --- Financial Editing ---
function startEditingFinancialConfig() {
  if (group.value && group.value.activeFinancialConfiguration) {
    newBuyInAmount.value = group.value.activeFinancialConfiguration.buyInAmount;
    newSkinValueFormula.value = group.value.activeFinancialConfiguration.skinValueFormula;
    newCthPayoutFormula.value = group.value.activeFinancialConfiguration.cthPayoutFormula;
  } else {
    newBuyInAmount.value = 6; // Default Buy-in
    newSkinValueFormula.value = '[roundPlayers] * 0.25'; // New default using NCalc syntax
    newCthPayoutFormula.value = '[roundPlayers] - 1'; // New default using NCalc syntax
  }
  financialValidationErrors.value = [];
  validationResults.value = []; // Clear previous validation results
  showValidationDrawer.value = false; // Hide drawer initially
  overallValidationFailed.value = false; // Reset pop-up
  isFormulasDirty.value = false; // Mark clean initially
  isEditingFinancials.value = true;
}

// Client-side formula evaluation function - Updated for NCalc syntax
function evaluateFormula(formula: string, playerCount: number): number | null {
  try {
    // Replace placeholder with actual value
    const expression = formula.replace(/\[roundPlayers\]/g, playerCount.toString());
    // Basic evaluation: WARNING - this is a simplified eval.
    // For production, consider a safer expression parser or a web worker with sandboxed eval.
    // This is for demonstration based on the request to do it client-side without API.
    const result = eval(expression);
    if (typeof result === 'number' && !isNaN(result) && isFinite(result)) {
      return parseFloat(result.toFixed(2)); // Round to 2 decimal places
    }
    return null;
  } catch (e) {
    console.error(`Error evaluating formula "${formula}" for ${playerCount} players:`, e);
    return null;
  }
}

async function validateFinancialFormulas() {
  isValidationRunning.value = true;
  financialValidationErrors.value = [];
  validationResults.value = [];
  overallValidationFailed.value = false;
  showValidationDrawer.value = true; // Always show drawer after validation attempt

  let allValid = true;
  const minPlayers = 6;
  const maxPlayers = 30;
  const numberOfHoles = 18;

  if (newBuyInAmount.value <= 0) {
    financialValidationErrors.value.push("Buy-in amount must be greater than zero.");
    allValid = false;
  }
  if (!newSkinValueFormula.value.trim()) {
    financialValidationErrors.value.push("Skin value formula cannot be empty.");
    allValid = false;
  }
  if (!newCthPayoutFormula.value.trim()) {
    financialValidationErrors.value.push("CTH payout formula cannot be empty.");
    allValid = false;
  }

  if (!allValid) {
    isValidationRunning.value = false;
    overallValidationFailed.value = true;
    return;
  }

  for (let playerCount = minPlayers; playerCount <= maxPlayers; playerCount++) {
    let isValidForCount = true;
    let errorMessageForCount: string | undefined;

    const totalPot = playerCount * newBuyInAmount.value;
    const calculatedSkinValue = evaluateFormula(newSkinValueFormula.value, playerCount);
    const calculatedCthPayout = evaluateFormula(newCthPayoutFormula.value, playerCount);

    if (calculatedSkinValue === null || calculatedSkinValue < 0) {
      isValidForCount = false;
      errorMessageForCount = `Skin value formula results in invalid value (${calculatedSkinValue}) for ${playerCount} players.`;
    } else if (calculatedCthPayout === null || calculatedCthPayout < 0) {
      isValidForCount = false;
      errorMessageForCount = `CTH payout formula results in invalid value (${calculatedCthPayout}) for ${playerCount} players.`;
    } else {
      const totalPotentialSkins = numberOfHoles * calculatedSkinValue;
      const remainingForWinner = totalPot - totalPotentialSkins - calculatedCthPayout;

      if (remainingForWinner <= 0) {
        isValidForCount = false;
        errorMessageForCount = `Does not guarantee a positive payout for the overall winner (Remaining: $${remainingForWinner.toFixed(2)}) for ${playerCount} players.`;
      }
    }

    validationResults.value.push({
      playerCount,
      totalPot: parseFloat(totalPot.toFixed(2)),
      calculatedSkinValue: calculatedSkinValue !== null ? parseFloat(calculatedSkinValue.toFixed(2)) : 0,
      totalPotentialSkins: calculatedSkinValue !== null ? parseFloat((numberOfHoles * calculatedSkinValue).toFixed(2)) : 0,
      calculatedCthPayout: calculatedCthPayout !== null ? parseFloat(calculatedCthPayout.toFixed(2)) : 0,
      remainingForWinner: calculatedSkinValue !== null && calculatedCthPayout !== null ? parseFloat((totalPot - (numberOfHoles * calculatedSkinValue) - calculatedCthPayout).toFixed(2)) : 0,
      isValid: isValidForCount,
      errorMessage: errorMessageForCount,
    });

    if (!isValidForCount) {
      allValid = false;
    }
  }

  isValidationRunning.value = false;
  isFormulasDirty.value = false; // Formulas are now "clean" relative to this validation
  overallValidationFailed.value = !allValid; // Trigger pop-up if any validation failed

  if (!allValid) {
    financialValidationErrors.value.unshift("The financial configuration is invalid. See details below.");
    // Optionally trigger a Bootstrap alert/toast here
  } else {
    financialValidationErrors.value = ["Financial configuration is valid for all player counts (6-30)."];
  }
}

async function saveFinancialChanges() {
  if (!group.value) return;

  // Ensure validation has been run and passed before saving
  if (isFormulasDirty.value || overallValidationFailed.value) {
    alert("Please validate the financial formulas first and resolve any issues before saving.");
    return;
  }

  const updatePayload: UpdateGroupRequest = {
    newFinancials: {
      buyInAmount: newBuyInAmount.value,
      skinValueFormula: newSkinValueFormula.value,
      cthPayoutFormula: newCthPayoutFormula.value,
    },
  };

  const result = await groupsStore.updateGroup(group.value.id, updatePayload);
  if (result.isSuccess) {
    isEditingFinancials.value = false;
    financialValidationErrors.value = [];
    isFormulasDirty.value = false; // Reset dirty state after successful save
  } else {
    // Safely access result.error and its properties
    if (result.error?.errorType === ErrorType.Validation && result.error.message.includes("New Financial Configuration Validation Failed")) {
      financialValidationErrors.value = [result.error.message];
    } else {
      alert(`Error updating financial configuration: ${result.error?.message || 'Unknown error'}`);
    }
  }
}

function cancelEditingFinancials() {
  isEditingFinancials.value = false;
  financialValidationErrors.value = [];
  validationResults.value = [];
  showValidationDrawer.value = false;
  overallValidationFailed.value = false;
  isFormulasDirty.value = false;
}

// --- Member Management ---
async function openAddMembersModal() {
  showAddMembersModal.value = true;
  golferSearchTerm.value = '';
  selectedGolfersToAdd.value = [];
  addMembersError.value = null;
  await searchAvailableGolfers();
  const addMembersModal = new (window as any).bootstrap.Modal(document.getElementById('addMembersModal'));
  addMembersModal.show();
}

async function searchAvailableGolfers() {
  const result = await golfersStore.searchGolfers({ search: golferSearchTerm.value, limit: 50 });
  if (result.isSuccess && result.value !== undefined) {
    const currentMemberIds = new Set(groupMembers.value.map(m => m.golferId));
    availableGolfers.value = result.value.filter(g => !currentMemberIds.has(g.id));
  } else {
    addMembersError.value = result.error?.message || 'Failed to search golfers.';
    availableGolfers.value = [];
  }
}

function toggleGolferSelection(golferId: string) {
  const index = selectedGolfersToAdd.value.indexOf(golferId);
  if (index > -1) {
    selectedGolfersToAdd.value.splice(index, 1);
  } else {
    selectedGolfersToAdd.value.push(golferId);
  }
}

async function addMembersToGroup() {
  if (!group.value || selectedGolfersToAdd.value.length === 0) return;

  const result = await groupsStore.addGolfersToGroup(group.value.id, selectedGolfersToAdd.value);
  if (result.isSuccess) {
    showAddMembersModal.value = false;
    const addMembersModal = (window as any).bootstrap.Modal.getInstance(document.getElementById('addMembersModal'));
    addMembersModal.hide();
  } else {
    addMembersError.value = result.error?.message || 'Failed to add members.';
  }
}

function confirmRemoveMember(golferId: string, fullName: string) {
  golferToRemoveId.value = golferId;
  golferToRemoveName.value = fullName;
  showConfirmRemoveModal.value = true;
  const removeMemberModal = new (window as any).bootstrap.Modal(document.getElementById('removeMemberModal'));
  removeMemberModal.show();
}

async function removeMember() {
  if (!group.value || !golferToRemoveId.value) return;

  const result = await groupsStore.removeGolfersFromGroup(group.value.id, [golferToRemoveId.value]);
  if (result.isSuccess) {
    // Success, members list will be refreshed by store action
  } else {
    alert(`Error removing member: ${result.error?.message || 'Unknown error'}`);
  }
  golferToRemoveId.value = null;
  golferToRemoveName.value = '';
  showConfirmRemoveModal.value = false;
  const removeMemberModal = (window as any).bootstrap.Modal.getInstance(document.getElementById('removeMemberModal'));
  removeMemberModal.hide();
}

function confirmScorerStatusChange(golferId: string, fullName: string, currentStatus: boolean) {
  golferToChangeScorerStatusId.value = golferId;
  golferToChangeScorerStatusName.value = fullName;
  newScorerStatus.value = !currentStatus; // Toggle status
  showConfirmScorerStatusModal.value = true;
  const scorerStatusModal = new (window as any).bootstrap.Modal(document.getElementById('scorerStatusModal'));
  scorerStatusModal.show();
}

async function changeScorerStatus() {
  if (!group.value || !golferToChangeScorerStatusId.value) return;

  const result = await groupsStore.setGroupMemberScorerStatus(
    group.value.id,
    golferToChangeScorerStatusId.value,
    newScorerStatus.value
  );
  if (result.isSuccess) {
    // Success, members list will be refreshed by store action
  } else {
    alert(`Error changing scorer status: ${result.error?.message || 'Unknown error'}`);
  }
  golferToChangeScorerStatusId.value = null;
  golferToChangeScorerStatusName.value = '';
  showConfirmScorerStatusModal.value = false;
  const scorerStatusModal = (window as any).bootstrap.Modal.getInstance(document.getElementById('scorerStatusModal'));
  scorerStatusModal.hide();
}
</script>

<template>
  <div class="container mt-4">
    <button @click="router.back()" class="btn btn-secondary mb-3 rounded-pill">
      <i class="bi bi-arrow-left-circle"></i> Back to Groups
    </button>

    <div v-if="groupsStore.isLoadingGroupDetail" class="text-center">
      <div class="spinner-border text-primary" role="status">
        <span class="visually-hidden">Loading...</span>
      </div>
      <p>Loading group details...</p>
    </div>

    <div v-else-if="groupsStore.groupDetailError" class="alert alert-danger" role="alert">
      Error: {{ groupsStore.groupDetailError.message }}
    </div>

    <div v-else-if="!group" class="alert alert-warning" role="alert">
      Group not found.
    </div>

    <div v-else>
      <h2 class="mb-4">{{ group.name }} Details</h2>

      <div class="card shadow-sm mb-4">
        <div class="card-header d-flex justify-content-between align-items-center bg-primary text-white">
          Group Information
          <button v-if="!isEditingGroup && canEditGroup" class="btn btn-sm btn-light rounded-pill" @click="startEditingGroupDetails">
            <i class="bi bi-pencil"></i> Edit
          </button>
        </div>
        <div class="card-body">
          <div v-if="!isEditingGroup">
            <p><strong>Name:</strong> {{ group.name }}</p>
            <p><strong>Default Course:</strong> {{ defaultCourseName }}</p>
            <p><strong>Created:</strong> {{ new Date(group.createdAt).toLocaleDateString() }}</p>
            <p><strong>Last Updated:</strong> {{ new Date(group.updatedAt).toLocaleString() }}</p>
          </div>
          <form v-else @submit.prevent="saveGroupChanges">
            <div class="mb-3">
              <label for="groupName" class="form-label">Group Name</label>
              <input type="text" class="form-control" id="groupName" v-model="newGroupName" required>
            </div>
            <div class="mb-3">
              <label for="defaultCourse" class="form-label">Default Course</label>
              <select class="form-select" id="defaultCourse" v-model="newDefaultCourseId" @focus="loadCoursesForDropdown">
                <option :value="null">None</option>
                <option v-for="course in availableCourses" :key="course.id" :value="course.id">{{ course.name }}</option>
              </select>
            </div>
            <div class="d-flex justify-content-end">
              <button type="button" class="btn btn-secondary me-2 rounded-pill" @click="cancelEditingGroupDetails">Cancel</button>
              <button type="submit" class="btn btn-primary rounded-pill" :disabled="groupsStore.isUpdatingGroup">
                <span v-if="groupsStore.isUpdatingGroup" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
                Save Changes
              </button>
            </div>
          </form>
        </div>
      </div>

      <div class="card shadow-sm mb-4">
        <div class="card-header d-flex justify-content-between align-items-center bg-info text-white">
          Financial Configuration
          <button v-if="!isEditingFinancials && canManageFinances" class="btn btn-sm btn-light rounded-pill" @click="startEditingFinancialConfig">
            <i class="bi bi-pencil"></i> Edit
          </button>
        </div>
        <div class="card-body">
          <div v-if="!isEditingFinancials">
            <p v-if="group.activeFinancialConfiguration">
              <strong>Buy-in Amount:</strong> ${{ group.activeFinancialConfiguration.buyInAmount.toFixed(2) }}
            </p>
            <p v-else class="text-warning">No active financial configuration set.</p>
            <p><strong>Skin Value Formula:</strong> <code>{{ group.activeFinancialConfiguration?.skinValueFormula || 'N/A' }}</code></p>
            <p><strong>CTH Payout Formula:</strong> <code>{{ group.activeFinancialConfiguration?.cthPayoutFormula || 'N/A' }}</code></p>
            <p v-if="group.activeFinancialConfiguration">
              <strong>Validated:</strong> {{ group.activeFinancialConfiguration.isValidated ? 'Yes' : 'No' }}
              <span v-if="group.activeFinancialConfiguration.validatedAt" class="text-muted"> ({{ new Date(group.activeFinancialConfiguration.validatedAt).toLocaleDateString() }})</span>
            </p>
          </div>
          <form v-else @submit.prevent="saveFinancialChanges">
            <div class="mb-3">
              <label for="buyInAmount" class="form-label">Buy-in Amount ($)</label>
              <input type="number" step="0.01" class="form-control" id="buyInAmount" v-model="newBuyInAmount" required>
            </div>
            <div class="mb-3">
              <label for="skinValueFormula" class="form-label">Skin Value Formula</label>
              <input type="text" class="form-control" id="skinValueFormula" v-model="newSkinValueFormula" required>
            </div>
            <div class="mb-3">
              <label for="cthPayoutFormula" class="form-label">Closest to the Hole Payout Formula</label>
              <input type="text" class="form-control" id="cthPayoutFormula" v-model="newCthPayoutFormula" required>
            </div>

            <div class="mb-3">
              <button type="button" class="btn btn-link btn-sm p-0 mb-2" @click="showTemplateInputs = !showTemplateInputs">
                {{ showTemplateInputs ? 'Hide' : 'Show' }} available formula inputs
              </button>
              <div v-if="showTemplateInputs" class="table-responsive">
                <table class="table table-sm table-bordered">
                  <thead>
                    <tr>
                      <th>Variable</th>
                      <th>Description</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="input in templateInputs" :key="input.variable">
                      <td><code>{{ input.variable }}</code></td>
                      <td>{{ input.description }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>

            <div class="d-flex justify-content-end mb-3">
              <button type="button" class="btn btn-warning me-2 rounded-pill" @click="validateFinancialFormulas" :disabled="isValidationRunning || !isFormulasDirty">
                <span v-if="isValidationRunning" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
                Validate
              </button>
              <button type="button" class="btn btn-secondary me-2 rounded-pill" @click="cancelEditingFinancials">Cancel</button>
              <button type="submit" class="btn btn-primary rounded-pill" :disabled="groupsStore.isUpdatingGroup || isFormulasDirty || overallValidationFailed">
                <span v-if="groupsStore.isUpdatingGroup" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
                Save Financials
              </button>
            </div>

            <div v-if="overallValidationFailed" class="alert alert-danger" role="alert">
              The financial configuration is invalid. Please check the validation results below for details.
            </div>

            <div v-if="financialValidationErrors.length" :class="{ 'alert alert-warning': !overallValidationFailed, 'alert alert-danger': overallValidationFailed }">
              <strong>Validation Summary:</strong>
              <ul>
                <li v-for="(error, index) in financialValidationErrors" :key="index">{{ error }}</li>
              </ul>
            </div>

            <div class="accordion" id="validationAccordion">
              <div class="accordion-item">
                <h2 class="accordion-header" id="headingValidation">
                  <button class="accordion-button" type="button" data-bs-toggle="collapse" data-bs-target="#collapseValidation" aria-expanded="false" aria-controls="collapseValidation" :class="{ 'collapsed': !showValidationDrawer }">
                    Validation Details (6-30 Players)
                  </button>
                </h2>
                <div id="collapseValidation" class="accordion-collapse collapse" :class="{ 'show': showValidationDrawer }" aria-labelledby="headingValidation" data-bs-parent="#validationAccordion">
                  <div class="accordion-body">
                    <div v-if="isValidationRunning" class="text-center">
                      <div class="spinner-border text-warning" role="status">
                        <span class="visually-hidden">Validating...</span>
                      </div>
                      <p>Running simulations...</p>
                    </div>
                    <div v-else-if="validationResults.length === 0" class="alert alert-info">
                      Click 'Validate' to run simulations.
                    </div>
                    <div v-else class="table-responsive">
                      <table class="table table-bordered table-sm">
                        <thead>
                          <tr>
                            <th>Players</th>
                            <th>Total Pot</th>
                            <th>Skin Value</th>
                            <th>Total Skins</th>
                            <th>CTH Payout</th>
                            <th>Remaining for Winner</th>
                            <th>Status</th>
                          </tr>
                        </thead>
                        <tbody>
                          <tr v-for="result in validationResults" :key="result.playerCount" :class="{ 'table-danger': !result.isValid, 'table-success': result.isValid }">
                            <td>{{ result.playerCount }}</td>
                            <td>${{ result.totalPot.toFixed(2) }}</td>
                            <td>${{ result.calculatedSkinValue.toFixed(2) }}</td>
                            <td>${{ result.totalPotentialSkins.toFixed(2) }}</td>
                            <td>${{ result.calculatedCthPayout.toFixed(2) }}</td>
                            <td>${{ result.remainingForWinner.toFixed(2) }}</td>
                            <td>
                              <span v-if="result.isValid" class="badge bg-success">Valid</span>
                              <span v-else class="badge bg-danger">Invalid</span>
                              <i v-if="!result.isValid" class="bi bi-exclamation-triangle-fill text-danger ms-2" :title="result.errorMessage"></i>
                            </td>
                          </tr>
                        </tbody>
                      </table>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </form>
        </div>
      </div>

      <div class="card shadow-sm mb-4">
        <div class="card-header d-flex justify-content-between align-items-center bg-success text-white">
          Group Members
          <button v-if="canManageMembers" class="btn btn-sm btn-light rounded-pill" @click="openAddMembersModal">
            <i class="bi bi-person-plus"></i> Add Members
          </button>
        </div>
        <div class="card-body">
          <div v-if="groupsStore.isManagingMembers" class="text-center">
            <div class="spinner-border spinner-border-sm text-success" role="status">
              <span class="visually-hidden">Loading...</span>
            </div>
            <p>Updating members...</p>
          </div>
          <div v-else-if="groupMembers.length === 0" class="alert alert-info" role="alert">
            No members in this group.
          </div>
          <ul v-else class="list-group list-group-flush">
            <li v-for="member in groupMembers" :key="member.golferId" class="list-group-item d-flex justify-content-between align-items-center">
              <div>
                <strong>{{ member.fullName }}</strong> ({{ member.email }})
                <span v-if="member.isScorer" class="badge bg-primary ms-2">Scorer</span>
              </div>
              <div>
                <button
                  v-if="canManageScorers && member.golferId !== authStore.user?.id"
                  class="btn btn-sm btn-outline-primary me-2 rounded-pill"
                  @click="confirmScorerStatusChange(member.golferId, member.fullName, member.isScorer)"
                >
                  <i :class="member.isScorer ? 'bi bi-person-dash' : 'bi bi-person-plus'"></i>
                  {{ member.isScorer ? 'Demote Scorer' : 'Promote Scorer' }}
                </button>
                <button
                  v-if="canManageMembers && member.golferId !== authStore.user?.id"
                  class="btn btn-sm btn-outline-danger rounded-pill"
                  @click="confirmRemoveMember(member.golferId, member.fullName)"
                >
                  <i class="bi bi-person-x"></i> Remove
                </button>
              </div>
            </li>
          </ul>
        </div>
      </div>
    </div>

    <div class="modal fade" id="addMembersModal" tabindex="-1" aria-labelledby="addMembersModalLabel" aria-hidden="true">
      <div class="modal-dialog modal-lg">
        <div class="modal-content">
          <div class="modal-header bg-success text-white">
            <h5 class="modal-title" id="addMembersModalLabel">Add Members to {{ group?.name }}</h5>
            <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal" aria-label="Close"></button>
          </div>
          <div class="modal-body">
            <div class="mb-3">
              <label for="golferSearch" class="form-label">Search Golfers (by name or email)</label>
              <div class="input-group">
                <input type="text" class="form-control" id="golferSearch" v-model="golferSearchTerm" @keyup.enter="searchAvailableGolfers" placeholder="Enter name or email">
                <button class="btn btn-outline-secondary" type="button" @click="searchAvailableGolfers">Search</button>
              </div>
            </div>

            <div v-if="golfersStore.isLoadingGolfers" class="text-center">
              <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
              <p>Searching golfers...</p>
            </div>
            <div v-else-if="addMembersError" class="alert alert-danger">{{ addMembersError }}</div>
            <div v-else-if="availableGolfers.length === 0" class="alert alert-info">No golfers found or all are already members.</div>
            <div v-else>
              <h6>Available Golfers:</h6>
              <ul class="list-group">
                <li v-for="golfer in availableGolfers" :key="golfer.id" class="list-group-item d-flex justify-content-between align-items-center">
                  <div>
                    <input class="form-check-input me-2" type="checkbox" :value="golfer.id" :id="`golfer-${golfer.id}`"
                           @change="toggleGolferSelection(golfer.id)" :checked="selectedGolfersToAdd.includes(golfer.id)">
                    <label :for="`golfer-${golfer.id}`">
                      {{ golfer.fullName }} ({{ golfer.email }})
                    </label>
                  </div>
                </li>
              </ul>
            </div>
          </div>
          <div class="modal-footer">
            <button type="button" class="btn btn-secondary rounded-pill" data-bs-dismiss="modal">Close</button>
            <button type="button" class="btn btn-success rounded-pill" @click="addMembersToGroup" :disabled="selectedGolfersToAdd.length === 0 || groupsStore.isManagingMembers">
              <span v-if="groupsStore.isManagingMembers" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
              Add Selected ({{ selectedGolfersToAdd.length }})
            </button>
          </div>
        </div>
      </div>
    </div>

    <div class="modal fade" id="removeMemberModal" tabindex="-1" aria-labelledby="removeMemberModalLabel" aria-hidden="true">
      <div class="modal-dialog">
        <div class="modal-content">
          <div class="modal-header bg-danger text-white">
            <h5 class="modal-title" id="removeMemberModalLabel">Confirm Removal</h5>
            <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal" aria-label="Close"></button>
          </div>
          <div class="modal-body">
            Are you sure you want to remove <strong>{{ golferToRemoveName }}</strong> from this group?
          </div>
          <div class="modal-footer">
            <button type="button" class="btn btn-secondary rounded-pill" data-bs-dismiss="modal">Cancel</button>
            <button type="button" class="btn btn-danger rounded-pill" @click="removeMember" :disabled="groupsStore.isManagingMembers">
              <span v-if="groupsStore.isManagingMembers" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
              Remove
            </button>
          </div>
        </div>
      </div>
    </div>

    <div class="modal fade" id="scorerStatusModal" tabindex="-1" aria-labelledby="scorerStatusModalLabel" aria-hidden="true">
      <div class="modal-dialog">
        <div class="modal-content">
          <div class="modal-header bg-primary text-white">
            <h5 class="modal-title" id="scorerStatusModalLabel">Confirm Scorer Status Change</h5>
            <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal" aria-label="Close"></button>
          </div>
          <div class="modal-body">
            Are you sure you want to {{ newScorerStatus ? 'promote' : 'demote' }} <strong>{{ golferToChangeScorerStatusName }}</strong> to {{ newScorerStatus ? 'a scorer' : 'a regular member' }} in this group?
          </div>
          <div class="modal-footer">
            <button type="button" class="btn btn-secondary rounded-pill" data-bs-dismiss="modal">Cancel</button>
            <button type="button" class="btn btn-primary rounded-pill" @click="changeScorerStatus" :disabled="groupsStore.isManagingMembers">
              <span v-if="groupsStore.isManagingMembers" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
              Confirm
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.card-header {
  font-weight: bold;
}
.rounded-pill {
  border-radius: 50rem !important;
}
.btn-light {
  color: #000;
}
.btn-light:hover {
  color: #000;
  background-color: #e2e6ea;
}
.btn-outline-primary {
  color: #007bff;
  border-color: #007bff;
}
.btn-outline-primary:hover {
  background-color: #007bff;
  color: #fff;
}
.btn-outline-danger {
  color: #dc3545;
  border-color: #dc3545;
}
.btn-outline-danger:hover {
  background-color: #dc3545;
  color: #fff;
}
.btn-close-white {
  filter: invert(1) grayscale(100%) brightness(200%);
}
.accordion-button:not(.collapsed) {
  background-color: #e9ecef; /* Light grey for open state */
  color: #495057; /* Darker text for open state */
}
.table-danger {
  background-color: #f8d7da; /* Light red for invalid rows */
}
.table-success {
  background-color: #d4edda; /* Light green for valid rows */
}
</style>
