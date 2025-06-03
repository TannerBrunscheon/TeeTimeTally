<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import type { Group, UpdateGroupRequest, GroupFinancialConfiguration } from '@/models';
import { useGroupsStore } from '@/stores/groups';
import { useAuthenticationStore } from '@/stores/authentication';
import { Permissions } from '@/models/auth/permissions';
import { AppError, ErrorType } from '@/primitives/error';

const props = defineProps<{
  group: Group;
}>();

const emit = defineEmits(['financialConfigUpdated']);

const groupsStore = useGroupsStore();
const authStore = useAuthenticationStore();

const isEditing = ref(false);
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

const canManageFinances = computed(() => authStore.hasPermission(Permissions.ManageGroupFinances));

// Initialize local state when editing starts or group prop changes
watch(() => props.group, (newGroup) => {
  if (newGroup && !isEditing.value) { // Only update if not in edit mode
    if (newGroup.activeFinancialConfiguration) {
      newBuyInAmount.value = newGroup.activeFinancialConfiguration.buyInAmount;
      newSkinValueFormula.value = newGroup.activeFinancialConfiguration.skinValueFormula;
      newCthPayoutFormula.value = newGroup.activeFinancialConfiguration.cthPayoutFormula;
    } else {
      // Set defaults if no active config
      newBuyInAmount.value = 6;
      newSkinValueFormula.value = '[roundPlayers] * 0.25';
      newCthPayoutFormula.value = '[roundPlayers] - 1';
    }
    isFormulasDirty.value = false; // Reset dirty state
    overallValidationFailed.value = false; // Reset validation state
    financialValidationErrors.value = [];
    validationResults.value = [];
    showValidationDrawer.value = false;
  }
}, { immediate: true });

function startEditing() {
  isEditing.value = true;
  // Re-initialize values from current group or defaults
  if (props.group.activeFinancialConfiguration) {
    newBuyInAmount.value = props.group.activeFinancialConfiguration.buyInAmount;
    newSkinValueFormula.value = props.group.activeFinancialConfiguration.skinValueFormula;
    newCthPayoutFormula.value = props.group.activeFinancialConfiguration.cthPayoutFormula;
  } else {
    newBuyInAmount.value = 6;
    newSkinValueFormula.value = '[roundPlayers] * 0.25';
    newCthPayoutFormula.value = '[roundPlayers] - 1';
  }
  isFormulasDirty.value = false; // Start editing with clean state
  overallValidationFailed.value = false;
  financialValidationErrors.value = [];
  validationResults.value = [];
  showValidationDrawer.value = false;
}

// Client-side formula evaluation function
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
  const numberOfHoles = 18; // Assuming 18 holes for total potential skins

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
  } else {
    financialValidationErrors.value = ["Financial configuration is valid for all player counts (6-30)."];
  }
}

async function saveChanges() {
  if (!props.group) return;

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

  const result = await groupsStore.updateGroup(props.group.id, updatePayload);
  if (result.isSuccess) {
    isEditing.value = false;
    financialValidationErrors.value = [];
    isFormulasDirty.value = false; // Reset dirty state after successful save
    emit('financialConfigUpdated'); // Notify parent to refresh group data
  } else {
    if (result.error?.errorType === ErrorType.Validation && result.error.message.includes("New Financial Configuration Validation Failed")) {
      financialValidationErrors.value = [result.error.message];
    } else {
      alert(`Error updating financial configuration: ${result.error?.message || 'Unknown error'}`);
    }
  }
}

function cancelEditing() {
  isEditing.value = false;
  // Reset to original values
  if (props.group.activeFinancialConfiguration) {
    newBuyInAmount.value = props.group.activeFinancialConfiguration.buyInAmount;
    newSkinValueFormula.value = props.group.activeFinancialConfiguration.skinValueFormula;
    newCthPayoutFormula.value = props.group.activeFinancialConfiguration.cthPayoutFormula;
  } else {
    newBuyInAmount.value = 6;
    newSkinValueFormula.value = '[roundPlayers] * 0.25';
    newCthPayoutFormula.value = '[roundPlayers] - 1';
  }
  financialValidationErrors.value = [];
  validationResults.value = [];
  showValidationDrawer.value = false;
  overallValidationFailed.value = false;
  isFormulasDirty.value = false;
}
</script>

<template>
  <div class="card shadow-sm mb-4">
    <div class="card-header d-flex justify-content-between align-items-center bg-info text-white">
      Financial Configuration
      <button v-if="!isEditing && canManageFinances" class="btn btn-sm btn-light rounded-pill" @click="startEditing">
        <i class="bi bi-pencil"></i> Edit
      </button>
    </div>
    <div class="card-body">
      <div v-if="!isEditing">
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
      <form v-else @submit.prevent="saveChanges">
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
          <button type="button" class="btn btn-secondary me-2 rounded-pill" @click="cancelEditing">Cancel</button>
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
</style>
