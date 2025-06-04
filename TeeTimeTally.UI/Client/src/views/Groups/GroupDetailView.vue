<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useGroupsStore } from '@/stores/groups';
import { useCourseStore } from '@/stores/courses'; // Still needed for defaultCourseName lookup
import type { Group, GroupMember } from '@/models'; // Import only necessary models

// Import the new sub-components
import GroupInfoCard from './GroupDetail/GroupInfoCard.vue';
import FinancialConfigCard from './GroupDetail/FinancialConfigCard.vue';
import GroupMembersCard from './GroupDetail/GroupMembersCard.vue';

const groupsStore = useGroupsStore();
const courseStore = useCourseStore();
const route = useRoute();
const router = useRouter();

const groupId = ref<string | string[]>(route.params.groupId);
const group = computed(() => groupsStore.currentGroup);
const groupMembers = ref<GroupMember[]>([]); // State for group members
const defaultCourseName = ref<string>('N/A'); // State for default course name display

// --- Data Loading ---
async function loadGroupData() {
  console.log('Parent (GroupDetailView): loadGroupData called. Group ID:', groupId.value);
  if (!groupId.value || Array.isArray(groupId.value) && groupId.value.length === 0) { // Handle empty array case for groupId
    console.warn('Parent (GroupDetailView): Invalid or missing groupId. Redirecting to groups index.');
    router.push({ name: 'groups-index' });
    return;
  }

  const currentGroupId = Array.isArray(groupId.value) ? groupId.value[0] : groupId.value; // Ensure string

  const groupResult = await groupsStore.fetchGroupById(currentGroupId);
  if (groupResult.isFailure) {
    console.error('Parent (GroupDetailView): Failed to load group:', groupResult.error?.message);
    // Optionally redirect or show a persistent error
    return;
  }

  // Load default course name if available
  if (group.value?.defaultCourseId) {
    const defaultCourseResult = await courseStore.getCourseById(group.value.defaultCourseId);
    if (defaultCourseResult.isSuccess && defaultCourseResult.value !== undefined) {
      defaultCourseName.value = defaultCourseResult.value.name;
    } else {
      console.error('Parent (GroupDetailView): Failed to load default course:', defaultCourseResult.error?.message);
      defaultCourseName.value = 'N/A (Error)';
    }
  } else {
    defaultCourseName.value = 'N/A';
  }

  // Fetch group members using the store's action
  console.log('Parent (GroupDetailView): Fetching group members for group ID:', currentGroupId);
  const membersResult = await groupsStore.fetchGroupMembers(currentGroupId);
  if (membersResult.isSuccess && membersResult.value !== undefined) {
    // Ensure a new array reference to trigger reactivity more explicitly
    groupMembers.value = [...membersResult.value];
    console.log('Parent (GroupDetailView): groupMembers ref updated with new array. Count:', groupMembers.value.length);
    // console.log('Parent (GroupDetailView): New members data:', JSON.parse(JSON.stringify(groupMembers.value)));
  } else {
    console.error('Parent (GroupDetailView): Failed to load group members:', membersResult.error?.message);
    groupMembers.value = []; // Ensure it's an empty array on error
    console.log('Parent (GroupDetailView): groupMembers ref set to empty array due to error/no data.');
  }
}

onMounted(loadGroupData);

watch(() => route.params.groupId, (newId) => {
  console.log('Parent (GroupDetailView): route.params.groupId changed to:', newId);
  if (newId && (!Array.isArray(newId) || newId.length > 0) ) { // Ensure newId is valid
    groupId.value = newId;
    loadGroupData();
  } else if (!newId) {
     console.warn('Parent (GroupDetailView): route.params.groupId changed to undefined/null. Current groupId:', groupId.value);
  }
}, { immediate: false }); // immediate: false is default, so watcher runs on change after mount

// Handlers for events emitted by child components to trigger data refresh
function handleGroupUpdated() {
  console.log('Parent (GroupDetailView): handleGroupUpdated called. Reloading group data.');
  loadGroupData();
}

function handleFinancialConfigUpdated() {
  console.log('Parent (GroupDetailView): handleFinancialConfigUpdated called. Reloading group data.');
  loadGroupData();
}

function handleMembersUpdated() {
  console.log('Parent (GroupDetailView): handleMembersUpdated called. Reloading group data.');
  loadGroupData();
}
</script>

<template>
  <div class="container mt-4">
    <button @click="router.back()" class="btn btn-secondary mb-3 rounded-pill">
      <i class="bi bi-arrow-left-circle"></i> Back to Groups
    </button>

    <!-- Show initial loading spinner only if 'group' is not yet loaded -->
    <div v-if="groupsStore.isLoadingGroupDetail && !group" class="text-center">
      <div class="spinner-border text-primary" role="status">
        <span class="visually-hidden">Loading...</span>
      </div>
      <p>Loading group details...</p>
    </div>

    <!-- Show error if there's a group detail error -->
    <div v-else-if="groupsStore.groupDetailError" class="alert alert-danger" role="alert">
      Error: {{ groupsStore.groupDetailError.message }}
    </div>

    <!-- If group data exists, render the main content -->
    <!-- This block will now remain rendered even if isLoadingGroupDetail becomes true during a refresh, as long as 'group' has data -->
    <div v-else-if="group">
      <h2 class="mb-4">{{ group.name }} Details</h2>

      <!-- Optional: Display a subtle loading indicator during refreshes if group data is already present -->
      <div v-if="groupsStore.isLoadingGroupDetail && group" class="text-muted small my-2 text-center">
        <div class="spinner-border spinner-border-sm text-secondary" role="status">
          <span class="visually-hidden">Refreshing...</span>
        </div>
        <span class="ms-2">Refreshing group data...</span>
      </div>

      <GroupInfoCard
        :group="group"
        :default-course-name="defaultCourseName"
        @group-updated="handleGroupUpdated"
      />

      <FinancialConfigCard
        :group="group"
        @financial-config-updated="handleFinancialConfigUpdated"
      />

      <GroupMembersCard
        :group="group"
        :group-members="groupMembers"
        @members-updated="handleMembersUpdated"
      />
    </div>

    <!-- Fallback if group is not found after initial load attempt and no error state is set -->
    <div v-else class="alert alert-warning" role="alert">
      Group not found.
    </div>
  </div>
</template>

<style scoped>
/* Scoped styles specific to GroupDetailView.vue (minimal now) */
.rounded-pill {
  border-radius: 50rem !important;
}
</style>
