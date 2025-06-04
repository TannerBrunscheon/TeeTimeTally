<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue';
import { useRoute, RouterLink } from 'vue-router'; // Import useRoute to access route parameters and RouterLink for navigation
import { useCourseStore } from '@/stores/courses';
import { useAuthenticationStore } from '@/stores/authentication';
import type { Course } from '@/models/course'; // Import the full Course interface for detailed view
import { Permissions } from '@/models/auth/permissions';
import { AppError, ErrorType } from '@/primitives/error'; // Import AppError and ErrorType for more specific error handling

// Access route information (e.g., for parameters)
const route = useRoute();
// Access Pinia stores
const courseStore = useCourseStore();
const authStore = useAuthenticationStore();

// Reactive state for the course details
const courseDetails = ref<Course | null>(null);
const isLoadingDetails = ref<boolean>(false);
const errorDetails = ref<AppError | null>(null); // Store AppError object for more detailed error info

// Computed property to get the courseId from route parameters
const courseIdFromRoute = computed(() => {
  const idParam = route.params.courseId; // Assuming your route parameter is named 'courseId'
  return Array.isArray(idParam) ? idParam[0] : idParam;
});

// Computed property to check if the user has permission to view course details
const canViewCourseDetails = computed(() => authStore.hasPermission(Permissions.ReadCourses));

// Function to fetch course details from the store
async function fetchCourseDetailsData() {
  const currentCourseId = courseIdFromRoute.value as string; // Cast because we check it

  if (!currentCourseId) {
    errorDetails.value = AppError.validation('Course ID is missing from the route.');
    courseDetails.value = null;
    return;
  }

  if (!canViewCourseDetails.value) {
    errorDetails.value = AppError.failure('You do not have permission to view course details.');
    courseDetails.value = null;
    return;
  }

  isLoadingDetails.value = true;
  errorDetails.value = null;
  courseDetails.value = null;

  try {
    const result = await courseStore.getCourseById(currentCourseId);

    if (result.isSuccess && result.value) {
      courseDetails.value = result.value;
    } else if (result.error) {
      errorDetails.value = result.error;
      // Example of specific error handling based on error type from store
      if (result.error.errorType === ErrorType.NotFound) {
         console.warn(`Course with ID '${currentCourseId}' not found.`);
      }
    } else {
      // Fallback for unexpected scenarios where result is not success but no error object is present
      errorDetails.value = AppError.failure('Failed to load course details for an unknown reason.');
    }
  } catch (e: any) {
    // Catch any unexpected errors during the fetch operation itself
    console.error("Unexpected error in fetchCourseDetailsData:", e);
    errorDetails.value = AppError.failure(e.message || 'An unexpected error occurred.');
  } finally {
    isLoadingDetails.value = false;
  }
}

// Fetch course details when the component is mounted
onMounted(() => {
  fetchCourseDetailsData();
});

// Watch for changes in courseId (e.g., if navigating between different course detail pages)
watch(courseIdFromRoute, (newId, oldId) => {
  if (newId && newId !== oldId) {
    fetchCourseDetailsData();
  }
});

// Helper function to format dates for display
function formatDate(dateString: string | null | undefined): string {
  if (!dateString) return 'N/A';
  try {
    return new Date(dateString).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      // Optional: add time if relevant
      // hour: '2-digit',
      // minute: '2-digit'
    });
  } catch (e) {
    console.error("Error formatting date:", dateString, e);
    return "Invalid Date";
  }
}
</script>

<template>
  <div class="container mt-4 mb-5">
    <!-- Loading State -->
    <div v-if="isLoadingDetails" class="text-center my-5">
      <div class="spinner-border text-primary" role="status" style="width: 3rem; height: 3rem;">
        <span class="visually-hidden">Loading course details...</span>
      </div>
      <p class="mt-3 fs-5">Loading course details...</p>
    </div>

    <!-- Error State -->
    <div v-else-if="errorDetails" class="alert alert-danger" role="alert">
      <h4 class="alert-heading">
        <i class="bi bi-exclamation-triangle-fill me-2"></i>Error Loading Course
      </h4>
      <p>{{ errorDetails.message }}</p>
      <hr>
      <div class="d-flex justify-content-end">
        <button @click="fetchCourseDetailsData" class="btn btn-danger-outline me-2">
          <i class="bi bi-arrow-clockwise"></i> Try Again
        </button>
        <RouterLink :to="{ name: 'course-index' }" class="btn btn-secondary">
          <i class="bi bi-list-ul"></i> View All Courses
        </RouterLink>
      </div>
    </div>

    <!-- Permission Denied State -->
    <div v-else-if="!canViewCourseDetails && !isLoadingDetails" class="alert alert-warning" role="alert">
      <h4 class="alert-heading"><i class="bi bi-shield-lock-fill me-2"></i>Access Denied</h4>
      <p>You do not have permission to view these course details.</p>
       <RouterLink :to="{ name: 'home' }" class="btn btn-warning">
        <i class="bi bi-house"></i> Go to Home
      </RouterLink>
    </div>

    <!-- Course Details Display -->
    <div v-else-if="courseDetails" class="card shadow-lg">
      <div class="card-header bg-primary text-white d-flex justify-content-between align-items-center py-3">
        <h1 class="mb-0 h2">{{ courseDetails.name }}</h1>
        <RouterLink :to="{ name: 'course-index' }" class="btn btn-light btn-sm" title="Back to courses list">
          <i class="bi bi-arrow-left-circle me-1"></i> Back to List
        </RouterLink>
      </div>
      <div class="card-body p-4">
        <h2 class="h5 mb-3 text-muted border-bottom pb-2">Course Information</h2>
        <div class="row g-3">
          <div class="col-md-6">
            <p class="mb-2"><strong>Name:</strong> {{ courseDetails.name }}</p>
            <p class="mb-2"><strong>CTH Hole #:</strong> <span class="badge bg-info text-dark">{{ courseDetails.cthHoleNumber }}</span></p>
          </div>
          <div class="col-md-6">
            <p class="mb-2"><strong>Created At:</strong> {{ formatDate(courseDetails.createdAt) }}</p>
            <p class="mb-2"><strong>Last Updated:</strong> {{ formatDate(courseDetails.updatedAt) }}</p>
          </div>
          <!-- Add more fields from your Course model as needed -->
          <!-- Example:
          <div class="col-12" v-if="courseDetails.city || courseDetails.state">
            <p class="mb-2"><strong>Location:</strong> {{ courseDetails.city }}{{ courseDetails.city && courseDetails.state ? ', ' : '' }}{{ courseDetails.state }}</p>
          </div>
          <div class="col-md-6" v-if="courseDetails.par">
            <p class="mb-2"><strong>Par:</strong> {{ courseDetails.par }}</p>
          </div>
           -->
        </div>

        <hr class="my-4">

        <h2 class="h5 mb-3 text-muted border-bottom pb-2">Round History</h2>
        <div class="alert alert-secondary" role="alert">
          <i class="bi bi-info-circle-fill me-2"></i>
          Round history for this course is not yet implemented. This section will display past rounds played here.
        </div>
        <!-- Placeholder for future round history component or list -->
        <!-- Example: <RoundHistoryListComponent :course-id="courseDetails.id" /> -->

      </div>
       <div class="card-footer text-muted small">
        Course ID: {{ courseDetails.id }}
      </div>
    </div>

    <!-- Course Not Found (after loading and no error, but courseDetails is still null) -->
    <div v-else class="alert alert-warning text-center my-5" role="alert">
      <h4 class="alert-heading"><i class="bi bi-search me-2"></i>Course Not Found</h4>
      <p>The requested course could not be found or loaded.</p>
      <RouterLink :to="{ name: 'course-index' }" class="btn btn-secondary">
        <i class="bi bi-list-ul"></i> View All Courses
      </RouterLink>
    </div>
  </div>
</template>

<style scoped>
.container {
  max-width: 900px; /* Adjusted for a detail view, can be wider if needed */
}
.card-header h1 {
  font-weight: 500;
}
.card-body p {
  margin-bottom: 0.85rem;
  font-size: 1.05rem; /* Slightly larger for readability */
}
.card-body strong {
  color: #212529; /* Bootstrap's default dark text color */
  margin-right: 0.5em;
}
.alert-heading {
    margin-bottom: 0.75rem;
}
.badge.bg-info {
    font-size: 1em;
    padding: 0.4em 0.6em;
}
/* Ensure buttons in error state are spaced */
.alert-danger .btn {
    margin-top: 0.5rem;
}
</style>
```
