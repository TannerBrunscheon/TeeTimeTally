<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { useRouter } from 'vue-router';
import { useCourseStore } from '@/stores/courses'; // Assuming this store exists
import type { CourseSummary } from '@/models'; // Assuming CourseSummary is defined in your models
import { useAuthenticationStore } from '@/stores/authentication';
import { Permissions } from '@/models/auth/permissions';

const router = useRouter();
const courseStore = useCourseStore();
const authStore = useAuthenticationStore();

const courses = computed(() => courseStore.courses);
const isLoading = computed(() => courseStore.isLoadingCourses);
const error = computed(() => courseStore.coursesError); // Assuming a general error state in the store

const canCreateCourses = computed(() => authStore.hasPermission(Permissions.CreateCourses));
const canViewCourses = computed(() => authStore.hasPermission(Permissions.ReadCourses));

async function fetchCourses() {
  if (!canViewCourses.value) {
    // Optionally set an error message or redirect if user lacks permission to view any courses
    console.warn('User does not have permission to view courses.');
    // courseStore.courseError.value = AppError.failure('You are not authorized to view courses.'); // If store supports this
    return;
  }
  await courseStore.fetchAllCourses(); // Assuming this method exists and populates courses, isLoading, error
}

onMounted(() => {
  fetchCourses();
});

function navigateToCreateCourse() {
  router.push({ name: 'course-create' }); // Assuming a route named 'course-create'
}

function navigateToCourseDetail(courseId: string) {
  router.push({ name: 'course-detail', params: { courseId } }); // Assuming a route named 'course-detail'
}
</script>

<template>
  <div class="container mt-4">
    <div class="d-flex justify-content-between align-items-center mb-4">
      <h1>Golf Courses</h1>
      <button
        v-if="canCreateCourses"
        @click="navigateToCreateCourse"
        class="btn btn-primary rounded-pill"
      >
        <i class="bi bi-plus-circle-fill me-2"></i>Create New Course
      </button>
    </div>

    <div v-if="isLoading" class="text-center">
      <div class="spinner-border text-primary" role="status">
        <span class="visually-hidden">Loading courses...</span>
      </div>
      <p class="mt-2">Loading courses...</p>
    </div>

    <div v-else-if="error" class="alert alert-danger" role="alert">
      <strong>Error:</strong> {{ error.message || 'Failed to load courses.' }}
      <button @click="fetchCourses" class="btn btn-sm btn-danger-outline ms-3">Try Again</button>
    </div>

    <div v-else-if="!canViewCourses" class="alert alert-warning" role="alert">
      You do not have permission to view courses.
    </div>

    <div v-else-if="courses.length === 0" class="alert alert-info" role="alert">
      No courses found.
      <span v-if="canCreateCourses">
        Click the "Create New Course" button to add one.
      </span>
    </div>

    <div v-else class="row row-cols-1 row-cols-md-2 row-cols-lg-3 g-4">
      <div v-for="course in courses" :key="course.id" class="col">
        <div class="card h-100 shadow-sm course-card" @click="navigateToCourseDetail(course.id)">
          <div class="card-body">
            <h5 class="card-title">{{ course.name }}</h5>
            <!-- Add more summary details if available in CourseSummary -->
            <!-- e.g., <p class="card-text"><small>Par: {{ course.par }}</small></p> -->
          </div>
          <div class="card-footer bg-transparent border-top-0 text-end">
             <span class="text-primary view-details-link">View Details <i class="bi bi-arrow-right-short"></i></span>
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

.course-card {
  cursor: pointer;
  transition: transform 0.2s ease-in-out, box-shadow 0.2s ease-in-out;
}

.course-card:hover {
  transform: translateY(-5px);
  box-shadow: 0 0.5rem 1rem rgba(0, 0, 0, 0.15) !important;
}

.card-title {
  color: var(--bs-primary); /* Or your theme's primary color */
  font-weight: 500;
}

.view-details-link {
  font-size: 0.9rem;
  font-weight: 500;
}

.card-footer {
  padding-top: 0.5rem;
  padding-bottom: 0.75rem;
}
</style>
