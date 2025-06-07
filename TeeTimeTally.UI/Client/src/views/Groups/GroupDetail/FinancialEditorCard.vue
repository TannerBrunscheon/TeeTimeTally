<script setup lang="ts">
import { ref, watch, computed, onMounted } from 'vue';
// Removed CreateGroupFinancialConfigurationResponseDTO as it's not directly used for local type annotations
// and its absence in models export causes an error. The type for group.activeFinancialConfiguration
// will be inferred from the Group prop. Ensure Group model is correctly typed.
import type { Group, UpdateGroupRequest, CreateGroupFinancialConfigurationInputDTO } from '@/models';
import { useGroupsStore } from '@/stores/groups';
import { useAuthenticationStore } from '@/stores/authentication';
import { Permissions } from '@/models/auth/permissions';
import { ErrorType } from '@/primitives/error';

interface Props {
  group?: Group; // Optional, only needed for 'view' and 'editForm' modes
  mode: 'view' | 'editForm' | 'createForm';
  initialValuesForCreate?: CreateGroupFinancialConfigurationInputDTO;
}

const props = defineProps<Props>();

const emit = defineEmits(['financialConfigUpdated', 'configChanged', 'cancelEdit', 'editClicked']); // Added 'editClicked'

const groupsStore = useGroupsStore();
const authStore = useAuthenticationStore();

// Internal state for form fields
const currentBuyInAmount = ref(0);
const currentSkinValueFormula = ref('');
const currentCthPayoutFormula = ref('');

// State related to validation and UI
const financialValidationErrors = ref<string[]>([]);
const isFormulasDirty = ref(false);
const isValidationRunning = ref(false);
const validationResults = ref<ValidationResult[]>([]);
const showValidationDrawer = ref(false);
const overallValidationFailed = ref(false);
const showTemplateInputs = ref(false);
const hasBeenSuccessfullyValidated = ref(false); // Client-side simulation validation

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

const templateInputs = [
  { variable: '[roundPlayers]', description: 'The total number of players participating in the round (integer).' }
];

const canManageFinances = computed(() => authStore.hasPermission(Permissions.ManageGroupFinances));

// Determine current editing state based on mode
const isEditingInternally = ref(props.mode === 'editForm' || props.mode === 'createForm');


function getInitialData(): CreateGroupFinancialConfigurationInputDTO {
    if (props.mode === 'createForm' && props.initialValuesForCreate) {
        return props.initialValuesForCreate;
    }
    if (props.group?.activeFinancialConfiguration) {
        return {
            buyInAmount: props.group.activeFinancialConfiguration.buyInAmount,
            skinValueFormula: props.group.activeFinancialConfiguration.skinValueFormula,
            cthPayoutFormula: props.group.activeFinancialConfiguration.cthPayoutFormula,
        };
    }
    // Default values if no initial data is provided
    return {
        buyInAmount: 6,
        skinValueFormula: '[roundPlayers] * 0.25',
        cthPayoutFormula: '[roundPlayers] - 1',
    };
}

function populateFormFields() {
  const data = getInitialData();
  currentBuyInAmount.value = data.buyInAmount;
  currentSkinValueFormula.value = data.skinValueFormula;
  currentCthPayoutFormula.value = data.cthPayoutFormula;

  // Reset validation/dirty states when populating
  isFormulasDirty.value = false;
  hasBeenSuccessfullyValidated.value = false;
  overallValidationFailed.value = false;
  financialValidationErrors.value = [];
  validationResults.value = [];
  showValidationDrawer.value = false;
}

onMounted(() => {
  if (isEditingInternally.value || props.mode === 'view') {
    populateFormFields();
  }
  // For createForm, emit initial state if needed, or let watch handle it
  if (props.mode === 'createForm') {
    emitConfigChanged(); // Emit initial state for create mode
  }
});

// Watch for prop changes to re-initialize if necessary (e.g., group prop changes)
watch(() => [props.group, props.mode, props.initialValuesForCreate], () => {
    isEditingInternally.value = props.mode === 'editForm' || props.mode === 'createForm';
    if (isEditingInternally.value || props.mode === 'view') {
        populateFormFields();
    }
     if (props.mode === 'createForm') {
        emitConfigChanged();
    }
}, { deep: true });


// Watch for changes in financial input fields
watch([currentBuyInAmount, currentSkinValueFormula, currentCthPayoutFormula], () => {
  if (isEditingInternally.value) {
    isFormulasDirty.value = true;
    hasBeenSuccessfullyValidated.value = false;
    overallValidationFailed.value = false;
    if (props.mode === 'createForm') {
      emitConfigChanged();
    }
  }
});

function emitConfigChanged() {
    const currentConfigData: CreateGroupFinancialConfigurationInputDTO = {
        buyInAmount: currentBuyInAmount.value,
        skinValueFormula: currentSkinValueFormula.value,
        cthPayoutFormula: currentCthPayoutFormula.value,
    };
    const schemaValid = currentBuyInAmount.value > 0 && currentSkinValueFormula.value.trim() !== '' && currentCthPayoutFormula.value.trim() !== '';

    emit('configChanged', {
        data: currentConfigData,
        validationStatus: {
            isValidSchema: schemaValid, // Basic check
            allSimulationsPassed: hasBeenSuccessfullyValidated.value && !isFormulasDirty.value,
            isDirty: isFormulasDirty.value,
            errors: financialValidationErrors.value,
            detailedResults: validationResults.value
        }
    });
}


function startEditingMode() { // Only for 'view' -> 'editForm' transition
  if (props.mode === 'view') {
    // This component emits 'editClicked'. The parent (GroupDetailView)
    // is responsible for changing the 'mode' prop to 'editForm'.
    emit('editClicked');
  }
}


function safeEvaluateFormula(formula: string, playerCount: number): number | null {
  const expression = formula.replace(/\[roundPlayers\]/g, playerCount.toString());
  try {
    if (!/^[0-9\s\.\+\-\*\/\(\)]*$/.test(expression)) {
      console.error(`Invalid characters in formula expression: "${expression}"`);
      return null;
    }
    // eslint-disable-next-line no-new-func
    const evaluatedResult = new Function(`return ${expression}`)();
    if (typeof evaluatedResult === 'number' && !isNaN(evaluatedResult) && isFinite(evaluatedResult)) {
      return parseFloat(evaluatedResult.toFixed(2));
    }
    return null;
  } catch (e) {
    console.error(`Error evaluating formula: "${expression}"`, e);
    return null;
  }
}

async function validateFinancialFormulas() {
  isValidationRunning.value = true;
  financialValidationErrors.value = [];
  validationResults.value = [];
  overallValidationFailed.value = false;
  hasBeenSuccessfullyValidated.value = false;
  showValidationDrawer.value = true;

  let allSimulationsPassed = true;
  const minPlayers = 6;
  const maxPlayers = 30;
  const numberOfHoles = 18;

  if (currentBuyInAmount.value <= 0) {
    financialValidationErrors.value.push("Buy-in amount must be greater than zero.");
    allSimulationsPassed = false;
  }
  if (!currentSkinValueFormula.value.trim()) {
    financialValidationErrors.value.push("Skin value formula cannot be empty.");
    allSimulationsPassed = false;
  }
  if (!currentCthPayoutFormula.value.trim()) {
    financialValidationErrors.value.push("CTH payout formula cannot be empty.");
    allSimulationsPassed = false;
  }

  if (!allSimulationsPassed) { // Basic schema validation failed
    isValidationRunning.value = false;
    overallValidationFailed.value = true;
    if (props.mode === 'createForm') emitConfigChanged();
    return;
  }

  for (let playerCount = minPlayers; playerCount <= maxPlayers; playerCount++) {
    let isValidForCount = true;
    let errorMessageForCount: string | undefined;

    const totalPot = playerCount * currentBuyInAmount.value;
    const calculatedSkinValue = safeEvaluateFormula(currentSkinValueFormula.value, playerCount);
    const calculatedCthPayout = safeEvaluateFormula(currentCthPayoutFormula.value, playerCount);

    if (calculatedSkinValue === null) {
      isValidForCount = false;
      errorMessageForCount = `Skin value formula is invalid for ${playerCount} players.`;
    } else if (calculatedSkinValue < 0) {
      isValidForCount = false;
      errorMessageForCount = `Skin value is negative ($${calculatedSkinValue.toFixed(2)}) for ${playerCount} players.`;
    }

    if (calculatedCthPayout === null) {
      isValidForCount = false;
      errorMessageForCount = (errorMessageForCount ? errorMessageForCount + " " : "") + `CTH payout formula is invalid for ${playerCount} players.`;
    } else if (calculatedCthPayout < 0) {
      isValidForCount = false;
      errorMessageForCount = (errorMessageForCount ? errorMessageForCount + " " : "") + `CTH payout is negative ($${calculatedCthPayout.toFixed(2)}) for ${playerCount} players.`;
    }

    if (isValidForCount && calculatedSkinValue !== null && calculatedCthPayout !== null) {
      const totalPotentialSkins = numberOfHoles * calculatedSkinValue;
      const remainingForWinner = totalPot - totalPotentialSkins - calculatedCthPayout;
      if (remainingForWinner <= 0) {
        isValidForCount = false;
        errorMessageForCount = `No positive payout for winner (Remaining: $${remainingForWinner.toFixed(2)}) for ${playerCount} players.`;
      }
    }

    validationResults.value.push({
      playerCount,
      totalPot: parseFloat(totalPot.toFixed(2)),
      calculatedSkinValue: calculatedSkinValue !== null ? parseFloat(calculatedSkinValue.toFixed(2)) : 0,
      totalPotentialSkins: calculatedSkinValue !== null ? parseFloat((numberOfHoles * calculatedSkinValue).toFixed(2)) : 0,
      calculatedCthPayout: calculatedCthPayout !== null ? parseFloat(calculatedCthPayout.toFixed(2)) : 0,
      remainingForWinner: (isValidForCount && calculatedSkinValue !== null && calculatedCthPayout !== null) ? parseFloat((totalPot - (numberOfHoles * calculatedSkinValue) - calculatedCthPayout).toFixed(2)) : 0,
      isValid: isValidForCount,
      errorMessage: errorMessageForCount,
    });
    if (!isValidForCount) allSimulationsPassed = false;
  }

  isValidationRunning.value = false;
  if (allSimulationsPassed) {
    isFormulasDirty.value = false;
    hasBeenSuccessfullyValidated.value = true;
    financialValidationErrors.value = ["Financial configuration is valid for all player counts (6-30)."];
    overallValidationFailed.value = false;
  } else {
    hasBeenSuccessfullyValidated.value = false;
    overallValidationFailed.value = true;
    financialValidationErrors.value.unshift("The financial configuration is invalid. See details below.");
  }
  if (props.mode === 'createForm') emitConfigChanged();
}

async function handleSaveChanges() { // Only for 'editForm' mode
  if (props.mode !== 'editForm' || !props.group) return;

  if (!hasBeenSuccessfullyValidated.value || isFormulasDirty.value) {
    alert("Please ensure the financial formulas are validated successfully and have no pending changes before saving.");
    overallValidationFailed.value = true;
    if (isFormulasDirty.value && !validationResults.value.length) showValidationDrawer.value = true;
    return;
  }

  const updatePayload: UpdateGroupRequest = {
    newFinancials: {
      buyInAmount: currentBuyInAmount.value,
      skinValueFormula: currentSkinValueFormula.value,
      cthPayoutFormula: currentCthPayoutFormula.value,
    },
  };

  const result = await groupsStore.updateGroup(props.group.id, updatePayload);
  if (result.isSuccess) {
    // isEditingInternally.value = false; // Parent will change mode to 'view'
    financialValidationErrors.value = [];
    isFormulasDirty.value = false;
    hasBeenSuccessfullyValidated.value = false;
    emit('financialConfigUpdated'); // Notify parent
  } else {
    let backendErrorMessage = result.error?.message || 'Unknown error during save.';
    if (result.error?.errorType === ErrorType.Validation) {
      backendErrorMessage = `Backend validation failed: ${result.error.message}`;
    }
    alert(`Error updating financial configuration: ${backendErrorMessage}`);
    hasBeenSuccessfullyValidated.value = false;
    overallValidationFailed.value = true;
    financialValidationErrors.value.push(`Save failed: ${backendErrorMessage}`);
  }
}

function handleCancelEditing() { // Only for 'editForm' mode
  if (props.mode === 'editForm') {
    populateFormFields(); // Reset to initial/group data
    emit('cancelEdit'); // Notify parent to change mode to 'view'
  }
}

const isValidateButtonDisabled = computed(() => isValidationRunning.value || !isFormulasDirty.value);
const isSaveButtonDisabled = computed(() => {
    if (props.mode !== 'editForm') return true; // Save only in editForm mode
    return groupsStore.isUpdatingGroup || isFormulasDirty.value || !hasBeenSuccessfullyValidated.value || overallValidationFailed.value;
});

const displayConfig = computed(() => { // Type for displayConfig.value will be inferred
    if (props.mode === 'view' && props.group?.activeFinancialConfiguration) {
        return props.group.activeFinancialConfiguration;
    }
    return null;
});

</script>

<template>
  <div class="card shadow-sm mb-4">
    <div class="card-header d-flex justify-content-between align-items-center bg-info text-white">
      Financial Configuration
      <button
        v-if="props.mode === 'view' && canManageFinances"
        class="btn btn-sm btn-light rounded-pill"
        @click="startEditingMode"
      >
        <i class="bi bi-pencil"></i> Edit
      </button>
    </div>
    <div class="card-body">
      <!-- View Mode -->
      <div v-if="props.mode === 'view'">
        <p v-if="displayConfig">
          <strong>Buy-in Amount:</strong> ${{ displayConfig.buyInAmount.toFixed(2) }}
        </p>
        <p v-else class="text-warning">No active financial configuration set.</p>
        <p><strong>Skin Value Formula:</strong>
          <code>{{ displayConfig?.skinValueFormula || 'N/A' }}</code></p>
        <p><strong>CTH Payout Formula:</strong>
          <code>{{ displayConfig?.cthPayoutFormula || 'N/A' }}</code></p>
        <p v-if="displayConfig">
          <strong>Validated:</strong> {{ displayConfig.isValidated ? 'Yes' : 'No' }}
          <span v-if="displayConfig.validatedAt" class="text-muted">
            ({{ new Date(displayConfig.validatedAt).toLocaleDateString() }})
          </span>
        </p>
      </div>

      <!-- Edit or Create Form Mode -->
      <form v-else @submit.prevent="handleSaveChanges">
        <div class="mb-3">
          <label for="buyInAmount" class="form-label">Buy-in Amount ($)</label>
          <input type="number" step="0.01" class="form-control" id="buyInAmount" v-model.number="currentBuyInAmount" required>
        </div>
        <div class="mb-3">
          <label for="skinValueFormula" class="form-label">Skin Value Formula</label>
          <input type="text" class="form-control" id="skinValueFormula" v-model="currentSkinValueFormula" required>
        </div>
        <div class="mb-3">
          <label for="cthPayoutFormula" class="form-label">Closest to the Hole Payout Formula</label>
          <input type="text" class="form-control" id="cthPayoutFormula" v-model="currentCthPayoutFormula" required>
        </div>

        <div class="mb-3">
          <button type="button" class="btn btn-link btn-sm p-0 mb-2" @click="showTemplateInputs = !showTemplateInputs">
            {{ showTemplateInputs ? 'Hide' : 'Show' }} available formula inputs
          </button>
          <div v-if="showTemplateInputs" class="table-responsive">
            <table class="table table-sm table-bordered">
              <thead><tr><th>Variable</th><th>Description</th></tr></thead>
              <tbody>
                <tr v-for="input in templateInputs" :key="input.variable">
                  <td><code>{{ input.variable }}</code></td><td>{{ input.description }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="d-flex justify-content-end mb-3">
          <button type="button" class="btn btn-warning me-2 rounded-pill" @click="validateFinancialFormulas" :disabled="isValidateButtonDisabled">
            <span v-if="isValidationRunning" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            {{ hasBeenSuccessfullyValidated && !isFormulasDirty ? 'Re-Validate' : 'Validate Formulas' }}
          </button>
          <button v-if="props.mode === 'editForm'" type="button" class="btn btn-secondary me-2 rounded-pill" @click="handleCancelEditing">Cancel</button>
          <button v-if="props.mode === 'editForm'" type="submit" class="btn btn-primary rounded-pill" :disabled="isSaveButtonDisabled">
            <span v-if="groupsStore.isUpdatingGroup" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            Save Financials
          </button>
        </div>

        <div v-if="overallValidationFailed && financialValidationErrors.length > 0 && financialValidationErrors[0] !== 'Financial configuration is valid for all player counts (6-30).'" class="alert alert-danger" role="alert">
          The financial configuration is invalid. Please check the validation results below and re-validate.
        </div>
        <div v-else-if="hasBeenSuccessfullyValidated && financialValidationErrors.length > 0 && !overallValidationFailed" class="alert alert-success" role="alert">
          {{ financialValidationErrors[0] }}
        </div>

        <div class="accordion" id="validationAccordionFinancialEditor">
          <div class="accordion-item">
            <h2 class="accordion-header" id="headingValidationFinancialEditor">
              <button class="accordion-button" type="button" data-bs-toggle="collapse"
                data-bs-target="#collapseValidationFinancialEditor" :aria-expanded="showValidationDrawer"
                aria-controls="collapseValidationFinancialEditor" :class="{ 'collapsed': !showValidationDrawer }"
                @click="showValidationDrawer = !showValidationDrawer">
                Validation Details (6-30 Players)
              </button>
            </h2>
            <div id="collapseValidationFinancialEditor" class="accordion-collapse collapse" :class="{ 'show': showValidationDrawer }"
              aria-labelledby="headingValidationFinancialEditor" data-bs-parent="#validationAccordionFinancialEditor">
              <div class="accordion-body">
                <div v-if="isValidationRunning" class="text-center">
                  <div class="spinner-border text-warning" role="status"><span class="visually-hidden">Validating...</span></div>
                  <p>Running simulations...</p>
                </div>
                <div v-else-if="validationResults.length === 0" class="alert alert-info">
                  Enter financial details and click 'Validate Formulas' to run simulations.
                </div>
                <div v-else class="table-responsive">
                  <table class="table table-bordered table-sm">
                    <thead><tr><th>Players</th><th>Total Pot</th><th>Skin Value</th><th>Total Skins</th><th>CTH Payout</th><th>Remaining for Winner</th><th>Status</th></tr></thead>
                    <tbody>
                      <tr v-for="result in validationResults" :key="result.playerCount" :class="{ 'table-danger text-danger': !result.isValid, 'table-success text-success': result.isValid }">
                        <td>{{ result.playerCount }}</td><td>${{ result.totalPot.toFixed(2) }}</td><td>${{ result.calculatedSkinValue.toFixed(2) }}</td><td>${{ result.totalPotentialSkins.toFixed(2) }}</td><td>${{ result.calculatedCthPayout.toFixed(2) }}</td><td>${{ result.remainingForWinner.toFixed(2) }}</td>
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
.card-header { font-weight: bold; }
.rounded-pill { border-radius: 50rem !important; }
.btn-light { color: #000; }
.btn-light:hover { color: #000; background-color: #e2e6ea; }
.table-danger td, .table-danger .badge, .table-danger i { color: var(--bs-danger-text-emphasis) !important; }
.table-success td, .table-success .badge { color: var(--bs-success-text-emphasis) !important; }
.accordion-button:not(.collapsed) { color: #0c63e4; background-color: #e7f1ff; }
</style>
