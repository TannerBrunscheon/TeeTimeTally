<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import type { Group, UpdateGroupRequest } from '@/models'; // Removed GroupFinancialConfiguration as it's handled internally now
import { useGroupsStore } from '@/stores/groups';
import { useAuthenticationStore } from '@/stores/authentication';
import { Permissions } from '@/models/auth/permissions';
import { ErrorType } from '@/primitives/error'; // Removed AppError as we are not creating AppError instances here

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
const isFormulasDirty = ref(false);
const isValidationRunning = ref(false);
const validationResults = ref<ValidationResult[]>([]);
const showValidationDrawer = ref(false);
const overallValidationFailed = ref(false);
const showTemplateInputs = ref(false);
const hasBeenSuccessfullyValidated = ref(false); // New state for successful validation

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

// Watch for changes in financial input fields to set dirty flag
watch([newBuyInAmount, newSkinValueFormula, newCthPayoutFormula], () => {
  if (isEditing.value) { // Only mark dirty if in edit mode
    isFormulasDirty.value = true;
    hasBeenSuccessfullyValidated.value = false; // Changes require re-validation
    overallValidationFailed.value = false; // Reset overall failure status
    // financialValidationErrors.value = []; // Optionally clear previous summary
    // validationResults.value = []; // Optionally clear detailed results
    // showValidationDrawer.value = false; // Optionally hide drawer
  }
});


watch(() => props.group, (newGroup) => {
  if (newGroup) {
    if (newGroup.activeFinancialConfiguration) {
      newBuyInAmount.value = newGroup.activeFinancialConfiguration.buyInAmount;
      newSkinValueFormula.value = newGroup.activeFinancialConfiguration.skinValueFormula;
      newCthPayoutFormula.value = newGroup.activeFinancialConfiguration.cthPayoutFormula;
    } else {
      newBuyInAmount.value = 6;
      newSkinValueFormula.value = '[roundPlayers] * 0.25';
      newCthPayoutFormula.value = '[roundPlayers] - 1';
    }
    isFormulasDirty.value = false;
    hasBeenSuccessfullyValidated.value = false; // Reset validation status on group change or edit cancellation
    overallValidationFailed.value = false;
    financialValidationErrors.value = [];
    validationResults.value = [];
    showValidationDrawer.value = false;
  }
}, { immediate: true, deep: true });


function startEditing() {
  isEditing.value = true;
  if (props.group.activeFinancialConfiguration) {
    newBuyInAmount.value = props.group.activeFinancialConfiguration.buyInAmount;
    newSkinValueFormula.value = props.group.activeFinancialConfiguration.skinValueFormula;
    newCthPayoutFormula.value = props.group.activeFinancialConfiguration.cthPayoutFormula;
  } else {
    newBuyInAmount.value = 6;
    newSkinValueFormula.value = '[roundPlayers] * 0.25';
    newCthPayoutFormula.value = '[roundPlayers] - 1';
  }
  isFormulasDirty.value = false;
  hasBeenSuccessfullyValidated.value = false;
  overallValidationFailed.value = false;
  financialValidationErrors.value = [];
  validationResults.value = [];
  showValidationDrawer.value = false;
}

// --- Safer Formula Evaluation ---
// This is a very basic parser for arithmetic expressions with one variable.
// It handles +, -, *, /, parentheses, and the [roundPlayers] variable.
// It does NOT handle functions, exponents, or complex operator precedence beyond left-to-right for */ and +-.
// For robust evaluation, a proper parsing library (e.g., math.js's expression parser) is highly recommended.
function safeEvaluateFormula(formula: string, playerCount: number): number | null {
  try {
    const expression = formula.replace(/\[roundPlayers\]/g, playerCount.toString());

    // Basic validation for allowed characters (numbers, operators, parentheses, spaces)
    if (!/^[0-9\s\.\+\-\*\/\(\)]*$/.test(expression)) {
      console.error(`Invalid characters in formula expression: "${expression}"`);
      return null;
    }

    // Attempt to use Function constructor for safer evaluation than direct eval
    // This is still not perfectly safe but better than direct eval.
    // A true parser/interpreter is the best solution for safety and features.
    // eslint-disable-next-line no-new-func
    const evaluatedResult = new Function(`return ${expression}`)();

    if (typeof evaluatedResult === 'number' && !isNaN(evaluatedResult) && isFinite(evaluatedResult)) {
      return parseFloat(evaluatedResult.toFixed(2));
    }
    console.error(`Formula "${expression}" did not evaluate to a valid number.`);
    return null;
  } catch (e) {
    console.error(`Error evaluating formula expression "${formula.replace(/\[roundPlayers\]/g, playerCount.toString())}":`, e);
    return null;
  }
}


async function validateFinancialFormulas() {
  isValidationRunning.value = true;
  financialValidationErrors.value = [];
  validationResults.value = [];
  overallValidationFailed.value = false;
  hasBeenSuccessfullyValidated.value = false; // Reset on new validation attempt
  showValidationDrawer.value = true;

  let allValidationsPassed = true;
  const minPlayers = 6;
  const maxPlayers = 30;
  const numberOfHoles = 18;

  if (newBuyInAmount.value <= 0) {
    financialValidationErrors.value.push("Buy-in amount must be greater than zero.");
    allValidationsPassed = false;
  }
  if (!newSkinValueFormula.value.trim()) {
    financialValidationErrors.value.push("Skin value formula cannot be empty.");
    allValidationsPassed = false;
  }
  if (!newCthPayoutFormula.value.trim()) {
    financialValidationErrors.value.push("CTH payout formula cannot be empty.");
    allValidationsPassed = false;
  }

  if (!allValidationsPassed) {
    isValidationRunning.value = false;
    overallValidationFailed.value = true;
    return;
  }

  for (let playerCount = minPlayers; playerCount <= maxPlayers; playerCount++) {
    let isValidForCount = true;
    let errorMessageForCount: string | undefined;

    const totalPot = playerCount * newBuyInAmount.value;
    const calculatedSkinValue = safeEvaluateFormula(newSkinValueFormula.value, playerCount);
    const calculatedCthPayout = safeEvaluateFormula(newCthPayoutFormula.value, playerCount);

    if (calculatedSkinValue === null) {
      isValidForCount = false;
      errorMessageForCount = `Skin value formula is invalid or could not be calculated for ${playerCount} players.`;
    } else if (calculatedSkinValue < 0) {
      isValidForCount = false;
      errorMessageForCount = `Calculated skin value is negative ($${calculatedSkinValue.toFixed(2)}) for ${playerCount} players.`;
    }

    if (calculatedCthPayout === null) {
      isValidForCount = false;
      errorMessageForCount = (errorMessageForCount ? errorMessageForCount + " " : "") + `CTH payout formula is invalid or could not be calculated for ${playerCount} players.`;
    } else if (calculatedCthPayout < 0) {
      isValidForCount = false;
      errorMessageForCount = (errorMessageForCount ? errorMessageForCount + " " : "") + `Calculated CTH payout is negative ($${calculatedCthPayout.toFixed(2)}) for ${playerCount} players.`;
    }

    if (isValidForCount && calculatedSkinValue !== null && calculatedCthPayout !== null) {
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
      remainingForWinner: (isValidForCount && calculatedSkinValue !== null && calculatedCthPayout !== null) ? parseFloat((totalPot - (numberOfHoles * calculatedSkinValue) - calculatedCthPayout).toFixed(2)) : 0,
      isValid: isValidForCount,
      errorMessage: errorMessageForCount,
    });

    if (!isValidForCount) {
      allValidationsPassed = false;
    }
  }

  isValidationRunning.value = false;
  if (allValidationsPassed) {
    isFormulasDirty.value = false; // Mark as "clean" relative to this successful validation
    hasBeenSuccessfullyValidated.value = true;
    financialValidationErrors.value = ["Financial configuration is valid for all player counts (6-30)."];
    overallValidationFailed.value = false;
  } else {
    hasBeenSuccessfullyValidated.value = false;
    overallValidationFailed.value = true;
    financialValidationErrors.value.unshift("The financial configuration is invalid. See details below.");
  }
}

async function saveChanges() {
  if (!props.group) return;

  // Explicitly check if validation was successful and formulas haven't changed since
  if (!hasBeenSuccessfullyValidated.value || isFormulasDirty.value) {
    alert("Please ensure the financial formulas are validated successfully and have no pending changes before saving.");
    overallValidationFailed.value = true; // Ensure details are shown if they tried to bypass
    if (isFormulasDirty.value && !validationResults.value.length) {
      showValidationDrawer.value = true; // Prompt to validate if never done
    }
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
    isFormulasDirty.value = false;
    hasBeenSuccessfullyValidated.value = false; // Reset for next edit
    emit('financialConfigUpdated');
  } else {
    // Backend might still return errors (e.g., concurrency, server issues)
    // The client-side validation aims to catch formula logic errors,
    // but the backend has the final say.
    let backendErrorMessage = result.error?.message || 'Unknown error during save.';
    if (result.error?.errorType === ErrorType.Validation) {
      // This implies a backend validation caught something the frontend didn't,
      // or a non-formula related validation error.
      backendErrorMessage = `Backend validation failed: ${result.error.message}`;
    }
    alert(`Error updating financial configuration: ${backendErrorMessage}`);
    // Keep UI in a state where user can see errors and retry or adjust.
    // It might be good to clear `hasBeenSuccessfullyValidated` here too if backend fails.
    hasBeenSuccessfullyValidated.value = false;
    overallValidationFailed.value = true; // Indicate an issue
    financialValidationErrors.value.push(`Save failed: ${backendErrorMessage}`);


  }
}

function cancelEditing() {
  isEditing.value = false;
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
  hasBeenSuccessfullyValidated.value = false; // Reset on cancel
}

// Computed property to control "Validate" button's disabled state
const isValidateButtonDisabled = computed(() => {
  return isValidationRunning.value || !isFormulasDirty.value;
});

// Computed property to control "Save Financials" button's disabled state
const isSaveButtonDisabled = computed(() => {
  return groupsStore.isUpdatingGroup || isFormulasDirty.value || !hasBeenSuccessfullyValidated.value || overallValidationFailed.value;
});

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
        <p><strong>Skin Value Formula:</strong>
          <code>{{ group.activeFinancialConfiguration?.skinValueFormula || 'N/A' }}</code></p>
        <p><strong>CTH Payout Formula:</strong>
          <code>{{ group.activeFinancialConfiguration?.cthPayoutFormula || 'N/A' }}</code></p>
        <p v-if="group.activeFinancialConfiguration">
          <strong>Validated:</strong> {{ group.activeFinancialConfiguration.isValidated ? 'Yes' : 'No' }}
          <span v-if="group.activeFinancialConfiguration.validatedAt" class="text-muted"> ({{ new
            Date(group.activeFinancialConfiguration.validatedAt).toLocaleDateString() }})</span>
        </p>
      </div>
      <form v-else @submit.prevent="saveChanges">
        <div class="mb-3">
          <label for="buyInAmount" class="form-label">Buy-in Amount ($)</label>
          <input type="number" step="0.01" class="form-control" id="buyInAmount" v-model.number="newBuyInAmount"
            required>
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
          <button type="button" class="btn btn-warning me-2 rounded-pill" @click="validateFinancialFormulas"
            :disabled="isValidateButtonDisabled">
            <span v-if="isValidationRunning" class="spinner-border spinner-border-sm" role="status"
              aria-hidden="true"></span>
            {{ hasBeenSuccessfullyValidated && !isFormulasDirty ? 'Re-Validate' : 'Validate' }}
          </button>
          <button type="button" class="btn btn-secondary me-2 rounded-pill" @click="cancelEditing">Cancel</button>
          <button type="submit" class="btn btn-primary rounded-pill" :disabled="isSaveButtonDisabled">
            <span v-if="groupsStore.isUpdatingGroup" class="spinner-border spinner-border-sm" role="status"
              aria-hidden="true"></span>
            Save Financials
          </button>
        </div>

        <div
          v-if="overallValidationFailed && financialValidationErrors.length > 0 && financialValidationErrors[0] !== 'Financial configuration is valid for all player counts (6-30).'"
          class="alert alert-danger" role="alert">
          The financial configuration is invalid. Please check the validation results below and re-validate after making
          changes.
        </div>
        <div v-else-if="hasBeenSuccessfullyValidated && financialValidationErrors.length > 0"
          class="alert alert-success" role="alert">
          {{ financialValidationErrors[0] }}
        </div>


        <div class="accordion" id="validationAccordion">
          <div class="accordion-item">
            <h2 class="accordion-header" id="headingValidation">
              <button class="accordion-button" type="button" data-bs-toggle="collapse"
                data-bs-target="#collapseValidation" :aria-expanded="showValidationDrawer.toString()"
                aria-controls="collapseValidation" :class="{ 'collapsed': !showValidationDrawer }"
                @click="showValidationDrawer = !showValidationDrawer">
                Validation Details (6-30 Players)
              </button>
            </h2>
            <div id="collapseValidation" class="accordion-collapse collapse" :class="{ 'show': showValidationDrawer }"
              aria-labelledby="headingValidation" data-bs-parent="#validationAccordion">
              <div class="accordion-body">
                <div v-if="isValidationRunning" class="text-center">
                  <div class="spinner-border text-warning" role="status">
                    <span class="visually-hidden">Validating...</span>
                  </div>
                  <p>Running simulations...</p>
                </div>
                <div v-else-if="validationResults.length === 0" class="alert alert-info">
                  Enter financial details and click 'Validate' to run simulations.
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
                      <tr v-for="result in validationResults" :key="result.playerCount"
                        :class="{ 'table-danger text-danger': !result.isValid, 'table-success text-success': result.isValid }">
                        <td>{{ result.playerCount }}</td>
                        <td>${{ result.totalPot.toFixed(2) }}</td>
                        <td>${{ result.calculatedSkinValue.toFixed(2) }}</td>
                        <td>${{ result.totalPotentialSkins.toFixed(2) }}</td>
                        <td>${{ result.calculatedCthPayout.toFixed(2) }}</td>
                        <td>${{ result.remainingForWinner.toFixed(2) }}</td>
                        <td>
                          <span v-if="result.isValid" class="badge bg-success">Valid</span>
                          <span v-else class="badge bg-danger">Invalid</span>
                          <i v-if="!result.isValid" class="bi bi-exclamation-triangle-fill text-danger ms-2"
                            :title="result.errorMessage"></i>
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

/* Ensure error messages in the table are visible */
.table-danger td,
.table-danger .badge,
.table-danger i {
  color: var(--bs-danger-text-emphasis) !important;
  /* Or a specific dark red */
}

.table-success td,
.table-success .badge {
  color: var(--bs-success-text-emphasis) !important;
  /* Or a specific dark green */
}
</style>
