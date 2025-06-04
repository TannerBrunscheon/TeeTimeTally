<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { useCourseStore } from '@/stores/courses';
import { useAuthenticationStore } from '@/stores/authentication';
import type { Course, SearchCoursesRequest } from '@/models/course';
import { Permissions } from '@/models/auth/permissions';
import { RouterLink } from 'vue-router';

const courseStore = useCourseStore();
const authStore = useAuthenticationStore();

const courses = ref<Course[]>([]);
const searchTerm = ref('');
const currentPage = ref(1);
const itemsPerPage = ref(10); // Or your preferred default
const totalCourses = ref(0); // To store the total count for pagination if API supports it

const isLoading = computed(() => courseStore.isLoadingCourses);
const error = computed(() => courseStore.coursesError);

const canCreateCourses = computed(() => authStore.hasPermission(Permissions.CreateCourses));
// Assuming Admins inherently have ManageCourses or CreateCourses, or this can be more specific
const canManageCourses = computed(() => authStore.hasPermission(Permissions.ManageCourses) || authStore.user?.isSystemAdmin);


async function fetchCourses(page: number = 1) {
  currentPage.value = page;
  const params: SearchCoursesRequest = {
    search: searchTerm.value || undefined,
    limit: itemsPerPage.value,
    offset: (page - 1) * itemsPerPage.value,
  };

  const result = await courseStore.searchCourses(params);
  if (result.isSuccess && result.value) {
    courses.value = result.value;
    // Assuming your API might return a total count for pagination
    // If not, you might need a separate endpoint or adjust pagination logic
    // For now, we'll just use the length of the current batch for simplicity if no total count.
    // totalCourses.value = result.totalCount || result.value.length; // Example if totalCount was available
  } else {
    courses.value = [];
  }
}

function handleSearch() {
  fetchCourses(1); // Reset to first page on new search
}

onMounted(() => {
  // Check if user has permission to read courses before fetching
  if (authStore.hasPermission(Permissions.ReadCourses)) {
    fetchCourses();
  } else {
    // Handle unauthorized access to this view, though route guards should ideally prevent this
    console.warn("User does not have permission to read courses.");
    // Optionally redirect or show an error message specific to permissions
  }
});


function formatDate(dateString: string | null | undefined) {
  if (!dateString) return 'N/A';
  return new Date(dateString).toLocaleDateString();
}
</script>

<template>
  <div class="container mt-4 mb-5"> <!-- Added mb-5 for bottom margin -->
    <div class="d-flex justify-content-between align-items-center mb-3">
      <h1>Golf Courses</h1>
      <RouterLink v-if="canCreateCourses" to="/courses/create" class="btn btn-primary rounded-pill">
        <i class="bi bi-plus-circle"></i> Create New Course
      </RouterLink>
    </div>

    <div class="card shadow-sm">
      <div class="card-body">
        <form @submit.prevent="handleSearch" class="mb-3">
          <div class="input-group">
            <input
              type="text"
              class="form-control"
              placeholder="Search courses by name..."
              v-model="searchTerm"
            />
            <button class="btn btn-outline-secondary" type="submit">
              <i class="bi bi-search"></i> Search
            </button>
          </div>
        </form>

        <div v-if="isLoading" class="text-center my-5">
          <div class="spinner-border text-primary" role="status">
            <span class="visually-hidden">Loading courses...</span>
          </div>
          <p class="mt-2">Loading courses...</p>
        </div>

        <div v-else-if="error" class="alert alert-danger" role="alert">
          <strong>Error:</strong> {{ error.message || 'Could not fetch courses.' }}
        </div>

        <div v-else-if="courses.length === 0" class="alert alert-info" role="alert">
          No courses found.
          <span v-if="searchTerm">Try adjusting your search term.</span>
          <span v-else-if="canCreateCourses">
            <RouterLink to="/courses/create">Create the first one!</RouterLink>
          </span>
        </div>

        <div v-else class="table-responsive">
          <table class="table table-hover align-middle">
            <thead class="table-light">
              <tr>
                <th scope="col">Name</th>
                <th scope="col" class="text-center">CTH Hole #</th>
                <th scope="col">Created At</th>
                <th scope="col">Last Updated</th>
                <th scope="col" class="text-center" v-if="canManageCourses">Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="course in courses" :key="course.id">
                <td>
                  <RouterLink :to="`/courses/${course.id}`"> <!-- Assuming a detail view might exist -->
                    {{ course.name }}
                  </RouterLink>
                </td>
                <td class="text-center">{{ course.cthHoleNumber }}</td>
                <td>{{ formatDate(course.createdAt) }}</td>
                <td>{{ formatDate(course.updatedAt) }}</td>
                <td class="text-center" v-if="canManageCourses">
                  <RouterLink :to="`/courses/${course.id}/edit`" class="btn btn-sm btn-outline-primary me-2 rounded-pill" title="Edit Course">
                    <i class="bi bi-pencil-square"></i>
                  </RouterLink>
                  <!-- Delete button would require a confirmation modal and a delete action in the store -->
                  <!-- <button class="btn btn-sm btn-outline-danger rounded-pill" title="Delete Course">
                    <i class="bi bi-trash"></i>
                  </button> -->
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.container {
  max-width: 960px;
}
.table th {
  font-weight: 600;
}
.table td, .table th {
  vertical-align: middle;
}
.btn-primary, .btn-outline-primary, .btn-outline-danger {
  /* Ensure icons are vertically aligned */
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
}
.rounded-pill {
  border-radius: 50rem !important;
}
</style>
