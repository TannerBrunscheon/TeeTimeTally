<script setup lang="ts">
import { ref, computed } from 'vue';
import { useRouter } from 'vue-router';
import { useCoursesStore } from '@/stores/courses';
import type { CreateCourseRequest } from '@/models/course';

const router = useRouter();
const courseStore = useCoursesStore();

// Form state
const courseName = ref('');
const cthHoleNumber = ref<number | null>(null);

// UI state
const isSubmitting = computed(() => courseStore.isLoadingCreateCourse);
const submissionError = computed(() => courseStore.coursesError?.message);

// Form validation
const isFormValid = computed(() => {
  return (
    courseName.value.trim() !== '' &&
    cthHoleNumber.value !== null &&
    cthHoleNumber.value >= 1 &&
    cthHoleNumber.value <= 18
  );
});

async function handleSubmit() {
  if (!isFormValid.value) {
    alert('Please fill out all fields correctly.');
    return;
  }

  const payload: CreateCourseRequest = {
    name: courseName.value.trim(),
    cthHoleNumber: cthHoleNumber.value!,
  };

  const result = await courseStore.createCourse(payload);

  if (result.isSuccess && result.value) {
    // Navigate to the new course's detail page or the index
    router.push({ name: 'course-index' });
  }
  // Error is handled reactively via computed `submissionError`
}
</script>

<template>
  <div class="container mt-4 mb-5">
    <div class="row justify-content-center">
      <div class="col-md-8 col-lg-6">
        <div class="card shadow-sm">
          <div class="card-header bg-primary text-white">
            <h4 class="mb-0">Create New Golf Course</h4>
          </div>
          <div class="card-body p-4">
            <form @submit.prevent="handleSubmit">
              <div class="mb-3">
                <label for="courseName" class="form-label">Course Name <span class="text-danger">*</span></label>
                <input
                  type="text"
                  class="form-control"
                  id="courseName"
                  v-model.trim="courseName"
                  required
                  placeholder="e.g., Pebble Peach Golf Links"
                />
              </div>

              <div class="mb-3">
                <label for="cthHoleNumber" class="form-label">Closest to the Hole (CTH) # <span class="text-danger">*</span></label>
                <input
                  type="number"
                  class="form-control"
                  id="cthHoleNumber"
                  v-model.number="cthHoleNumber"
                  required
                  min="1"
                  max="18"
                  placeholder="1-18"
                />
                 <div class="form-text">The designated hole number for the CTH contest.</div>
              </div>

              <div v-if="submissionError" class="alert alert-danger mt-3">
                {{ submissionError }}
              </div>

              <div class="d-flex justify-content-end mt-4">
                <button type="button" class="btn btn-secondary rounded-pill me-2" @click="router.back()">
                  Cancel
                </button>
                <button type="submit" class="btn btn-primary rounded-pill" :disabled="!isFormValid || isSubmitting">
                  <span v-if="isSubmitting" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
                  {{ isSubmitting ? 'Creating...' : 'Create Course' }}
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
  font-weight: 500;
}
.text-danger {
  color: #dc3545 !important;
}
</style>
