<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import type { Group, Course, UpdateGroupRequest } from '@/models';
import { useGroupsStore } from '@/stores/groups';
import { useCoursesStore } from '@/stores/courses';
import { useAuthenticationStore } from '@/stores/authentication';
import { Permissions } from '@/models/auth/permissions';

const props = defineProps<{
  group: Group;
  defaultCourseName: string;
}>();

const emit = defineEmits(['groupUpdated']);

const groupsStore = useGroupsStore();
const courseStore = useCoursesStore();
const authStore = useAuthenticationStore();

const isEditing = ref(false);
const newGroupName = ref('');
const newDefaultCourseId = ref<string | null>(null);
const availableCourses = ref<Course[]>([]); // Courses for the dropdown

const canEditGroup = computed(() => authStore.hasPermission(Permissions.ManageGroupSettings));

// Initialize local state when editing starts or group prop changes
watch(() => props.group, (newGroup) => {
  if (newGroup && !isEditing.value) { // Only update if not in edit mode
    newGroupName.value = newGroup.name;
    newDefaultCourseId.value = newGroup.defaultCourseId;
  }
}, { immediate: true });

function startEditing() {
  if (props.group) {
    newGroupName.value = props.group.name;
    newDefaultCourseId.value = props.group.defaultCourseId;
    isEditing.value = true;
    loadCoursesForDropdown(); // Load courses when starting edit
  }
}

async function loadCoursesForDropdown() {
  // Load initial 10 courses for the dropdown.
  // If the current default course is not in the first 10, ensure it's added.
  const coursesResult = await courseStore.searchCourses({ limit: 10 });
  if (coursesResult.isSuccess && coursesResult.value !== undefined) {
    let loadedCourses = coursesResult.value;

    // Ensure the current default course is in the list if it's not already
    if (props.group.defaultCourseId && !loadedCourses.some(c => c.id === props.group.defaultCourseId)) {
      const defaultCourseResult = await courseStore.getCourseById(props.group.defaultCourseId);
      if (defaultCourseResult.isSuccess && defaultCourseResult.value !== undefined) {
        loadedCourses.unshift(defaultCourseResult.value); // Add to the beginning
      }
    }
    availableCourses.value = loadedCourses;
  } else {
    console.error('Failed to load courses for dropdown:', coursesResult.error?.message);
    availableCourses.value = [];
  }
}

async function saveChanges() {
  if (!props.group) return;

  const updatePayload: UpdateGroupRequest = {
    name: newGroupName.value,
    defaultCourseId: newDefaultCourseId.value === '' ? null : newDefaultCourseId.value,
  };

  const result = await groupsStore.updateGroup(props.group.id, updatePayload);
  if (result.isSuccess) {
    isEditing.value = false;
    emit('groupUpdated'); // Notify parent to refresh group data
  } else {
    alert(`Error updating group: ${result.error?.message || 'Unknown error'}`);
  }
}

function cancelEditing() {
  isEditing.value = false;
  // Reset to original values
  if (props.group) {
    newGroupName.value = props.group.name;
    newDefaultCourseId.value = props.group.defaultCourseId;
  }
}
</script>

<template>
  <div class="card shadow-sm mb-4">
    <div class="card-header d-flex justify-content-between align-items-center bg-primary text-white">
      Group Information
      <button v-if="!isEditing && canEditGroup" class="btn btn-sm btn-light rounded-pill" @click="startEditing">
        <i class="bi bi-pencil"></i> Edit
      </button>
    </div>
    <div class="card-body">
      <div v-if="!isEditing">
        <p><strong>Name:</strong> {{ props.group.name }}</p>
        <p><strong>Default Course:</strong> {{ props.defaultCourseName }}</p>
        <p><strong>Created:</strong> {{ new Date(props.group.createdAt).toLocaleDateString() }}</p>
        <p><strong>Last Updated:</strong> {{ new Date(props.group.updatedAt).toLocaleString() }}</p>
      </div>
      <form v-else @submit.prevent="saveChanges">
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
          <button type="button" class="btn btn-secondary me-2 rounded-pill" @click="cancelEditing">Cancel</button>
          <button type="submit" class="btn btn-primary rounded-pill" :disabled="groupsStore.isUpdatingGroup">
            <span v-if="groupsStore.isUpdatingGroup" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            Save Changes
          </button>
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
