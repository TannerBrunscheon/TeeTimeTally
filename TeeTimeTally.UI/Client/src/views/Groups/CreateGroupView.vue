<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue';
import { useRouter } from 'vue-router';
import { useGroupsStore } from '@/stores/groups';
import { useCoursesStore } from '@/stores/courses';
import type { CourseSummary } from '@/models'; // Assuming CourseSummary for course list
import type { CreateGroupRequest, CreateGroupFinancialConfigurationInputDTO } from '@/models'; // Adjust DTO import path
import FinancialEditorCard from './GroupDetail/FinancialEditorCard.vue'; // Import the reusable editor card

const router = useRouter();
const groupsStore = useGroupsStore();
const courseStore = useCoursesStore();

// Form state
const groupName = ref('');
const defaultCourseId = ref<string | null>(null);
const includeFinancials = ref(false);

// Financial config state from FinancialEditorCard
const currentFinancialInput = ref<CreateGroupFinancialConfigurationInputDTO | null>(null);
const financialValidationStatus = ref<{
  isValidSchema: boolean;
  allSimulationsPassed: boolean;
  isDirty: boolean;
  errors: string[];
  detailedResults: any[]; // Or specific ValidationResult[] type
} | null>(null);

// Default values for FinancialEditorCard when in 'createForm' mode
const defaultFinancialsForCreate: CreateGroupFinancialConfigurationInputDTO = {
    buyInAmount: 6, // Example default
    skinValueFormula: '[roundPlayers] * 0.25', // Example default
    cthPayoutFormula: '[roundPlayers] - 1', // Example default
};

// Supporting state
const availableCourses = ref<CourseSummary[]>([]);
const isLoadingCourses = ref(false);
const isSubmitting = ref(false);
const submissionError = ref<string | null>(null);

// Validation for the main "Create Group" button
const canSubmit = computed(() => {
  if (!groupName.value.trim()) return false;
  if (isSubmitting.value) return false;

  if (includeFinancials.value) {
    // If financials are included, they must be schema-valid and have passed all simulations
    if (!financialValidationStatus.value?.isValidSchema || !financialValidationStatus.value?.allSimulationsPassed) {
        return false;
    }
    // If it was valid but then became dirty, it needs re-validation
    if (financialValidationStatus.value?.isDirty && financialValidationStatus.value?.allSimulationsPassed) {
        return false;
    }
  }
  return true;
});

async function fetchCourses() {
  isLoadingCourses.value = true;
  const result = await courseStore.searchCourses({ limit: 100 }); // Fetch courses for dropdown
  if (result.isSuccess && result.value) {
    availableCourses.value = result.value;
  } else {
    console.error('CreateGroupView: Failed to load courses for dropdown:', result.error?.message);
    // submissionError.value = 'Could not load courses. Please try refreshing.'; // Optional: inform user
  }
  isLoadingCourses.value = false;
}

onMounted(() => {
  fetchCourses();
});

// Handler for updates from FinancialEditorCard
function handleFinancialConfigChange(payload: { data: CreateGroupFinancialConfigurationInputDTO, validationStatus: any }) {
  console.log('CreateGroupView: Financial config changed:', payload);
  currentFinancialInput.value = payload.data;
  financialValidationStatus.value = payload.validationStatus;
}

watch(includeFinancials, (newVal) => {
    if (!newVal) {
        // If financials are unchecked, clear related errors and potentially reset status
        // This ensures that if they uncheck it, previous validation failures don't block submission
        financialValidationStatus.value = null; // Or reset to a default "not applicable" state
        currentFinancialInput.value = null;
        if (submissionError.value?.includes('Financial')) { // Clear financial specific errors
            submissionError.value = null;
        }
    } else {
        // When checking "include financials", ensure we get an initial state from the card
        // The FinancialEditorCard should emit its initial state via @config-changed on mount/prop change
        // If not, we might need to manually trigger or set a default valid state.
        // For now, we rely on the card emitting.
    }
});


async function handleSubmit() {
  submissionError.value = null; // Clear previous errors

  if (!groupName.value.trim()) {
    submissionError.value = 'Group name is required.';
    return;
  }

  if (includeFinancials.value) {
    if (!financialValidationStatus.value?.isValidSchema) {
        submissionError.value = 'Financial configuration has basic errors. Please correct them and re-validate.';
        return;
    }
    if (!financialValidationStatus.value?.allSimulationsPassed) {
        submissionError.value = 'Financial configuration has not been successfully validated. Please click "Validate Formulas" in the financial section.';
        return;
    }
     if (financialValidationStatus.value?.isDirty && financialValidationStatus.value?.allSimulationsPassed) {
        // This means it was valid, then user changed something.
        submissionError.value = 'Financial configuration has been changed since the last successful validation. Please re-validate.';
        return;
    }
  }

  // Final check, though button should be disabled
  if (!canSubmit.value) {
    if (!submissionError.value) {
        submissionError.value = 'Please ensure all fields are correct and financial configuration (if included) is validated.';
    }
    console.warn('CreateGroupView: handleSubmit called but canSubmit is false. Current validation state:', financialValidationStatus.value);
    return;
  }

  isSubmitting.value = true;

  let financialConfigPayload: CreateGroupFinancialConfigurationInputDTO | undefined = undefined;
  if (includeFinancials.value && currentFinancialInput.value) {
    // Ensure the data from FinancialEditorCard is used
    financialConfigPayload = currentFinancialInput.value;
  }

  const request: CreateGroupRequest = {
    name: groupName.value.trim(),
    defaultCourseId: defaultCourseId.value || undefined, // Send undefined if null/empty
    optionalInitialFinancials: financialConfigPayload,
  };

  console.log('CreateGroupView: Submitting create group request:', request);
  const result = await groupsStore.createGroup(request);

  if (result.isSuccess && result.value) {
    console.log('CreateGroupView: Group created successfully:', result.value);
    router.push({ name: 'group-detail', params: { groupId: result.value.id } });
  } else {
    submissionError.value = result.error?.message || 'Failed to create group. Please try again.';
    console.error('CreateGroupView: Group creation failed:', result.error);
  }

  isSubmitting.value = false;
}
</script>

<template>
  <div class="container mt-4">
    <div class="row justify-content-center">
      <div class="col-md-8 col-lg-7">
        <div class="card shadow-sm">
          <div class="card-header bg-primary text-white">
            <h4 class="mb-0">Create New Group</h4>
          </div>
          <div class="card-body p-4">
            <form @submit.prevent="handleSubmit">
              <!-- Group Name -->
              <div class="mb-3">
                <label for="groupName" class="form-label">Group Name <span class="text-danger">*</span></label>
                <input type="text" class="form-control" id="groupName" v-model.trim="groupName" required>
              </div>

              <!-- Default Course (Optional) -->
              <div class="mb-3">
                <label for="defaultCourse" class="form-label">Default Course (Optional)</label>
                <select class="form-select" id="defaultCourse" v-model="defaultCourseId">
                  <option :value="null">None</option>
                  <option v-if="isLoadingCourses" disabled>Loading courses...</option>
                  <option v-for="course in availableCourses" :key="course.id" :value="course.id">
                    {{ course.name }}
                  </option>
                </select>
                <div v-if="isLoadingCourses" class="form-text text-muted">Loading available courses...</div>
              </div>

              <!-- Financial Configuration Toggle -->
              <div class="mb-3 form-check">
                <input type="checkbox" class="form-check-input" id="includeFinancials" v-model="includeFinancials">
                <label class="form-check-label" for="includeFinancials">Include Initial Financial Configuration</label>
              </div>

              <!-- Financial Editor Card (Conditional) -->
              <div v-if="includeFinancials">
                <FinancialEditorCard
                  mode="createForm"
                  :initial-values-for-create="defaultFinancialsForCreate"
                  @config-changed="handleFinancialConfigChange"
                />
                <!-- Informational message about validation for create mode -->
                <div v-if="includeFinancials && financialValidationStatus && (!financialValidationStatus.allSimulationsPassed || financialValidationStatus.isDirty)" class="alert alert-info mt-3 p-2 small">
                    <ul class="mb-0 ps-3">
                        <li v-if="!financialValidationStatus.isValidSchema">Ensure all financial fields (Buy-in, Skin Formula, CTH Formula) are filled.</li>
                        <li v-if="financialValidationStatus.isValidSchema && !financialValidationStatus.allSimulationsPassed">Please click "Validate Formulas" in the financial section to check the configuration.</li>
                        <li v-if="financialValidationStatus.allSimulationsPassed && financialValidationStatus.isDirty">Financial details changed. Please click "Re-Validate" in the financial section.</li>
                    </ul>
                </div>
                 <div v-else-if="includeFinancials && financialValidationStatus && financialValidationStatus.allSimulationsPassed && !financialValidationStatus.isDirty" class="alert alert-success mt-3 p-2 small">
                    Financial configuration validated successfully.
                </div>
              </div>


              <!-- Submission Error -->
              <div v-if="submissionError" class="alert alert-danger mt-3">
                {{ submissionError }}
              </div>

              <!-- Buttons -->
              <div class="d-flex justify-content-end mt-4">
                <button type="button" class="btn btn-secondary rounded-pill me-2" @click="router.back()">
                  Cancel
                </button>
                <button type="submit" class="btn btn-primary rounded-pill" :disabled="!canSubmit || isSubmitting">
                  <span v-if="isSubmitting" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
                  {{ isSubmitting ? 'Creating...' : 'Create Group' }}
                </button>
              </div>
            </form>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.rounded-pill {
  border-radius: 50rem !important;
}
.card-header h4 {
    font-weight: 500; /* Or your preferred weight */
}
.text-danger {
  color: #dc3545 !important; /* Bootstrap danger color */
}
.alert ul {
    list-style-type: disc; /* Or 'circle' or 'square' */
}
</style>
