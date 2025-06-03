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
  if (!groupId.value) {
    router.push({ name: 'groups-index' });
    return;
  }

  const groupResult = await groupsStore.fetchGroupById(groupId.value as string);
  if (groupResult.isFailure) {
    console.error('Failed to load group:', groupResult.error?.message);
    // Optionally redirect or show a persistent error
    return;
  }

  // Load default course name if available
  if (group.value?.defaultCourseId) {
    const defaultCourseResult = await courseStore.getCourseById(group.value.defaultCourseId);
    if (defaultCourseResult.isSuccess && defaultCourseResult.value !== undefined) {
      defaultCourseName.value = defaultCourseResult.value.name;
    } else {
      console.error('Failed to load default course:', defaultCourseResult.error?.message);
      defaultCourseName.value = 'N/A (Error)';
    }
  } else {
    defaultCourseName.value = 'N/A';
  }

  // Fetch group members using the store's action
  const membersResult = await groupsStore.fetchGroupMembers(groupId.value as string);
  if (membersResult.isSuccess && membersResult.value !== undefined) {
    groupMembers.value = membersResult.value;
  } else {
    console.error('Failed to load group members:', membersResult.error?.message);
    groupMembers.value = []; // Ensure it's an empty array on error
  }
}

onMounted(loadGroupData);
watch(() => route.params.groupId, (newId) => {
  if (newId) {
    groupId.value = newId;
    loadGroupData();
  }
});

// Handlers for events emitted by child components to trigger data refresh
function handleGroupUpdated() {
  loadGroupData();
}

function handleFinancialConfigUpdated() {
  loadGroupData();
}

function handleMembersUpdated() {
  loadGroupData();
}
</script>

<template>
  <div class="container mt-4">
    <button @click="router.back()" class="btn btn-secondary mb-3 rounded-pill">
      <i class="bi bi-arrow-left-circle"></i> Back to Groups
    </button>

    <div v-if="groupsStore.isLoadingGroupDetail" class="text-center">
      <div class="spinner-border text-primary" role="status">
        <span class="visually-hidden">Loading...</span>
      </div>
      <p>Loading group details...</p>
    </div>

    <div v-else-if="groupsStore.groupDetailError" class="alert alert-danger" role="alert">
      Error: {{ groupsStore.groupDetailError.message }}
    </div>

    <div v-else-if="!group" class="alert alert-warning" role="alert">
      Group not found.
    </div>

    <div v-else>
      <h2 class="mb-4">{{ group.name }} Details</h2>

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
  </div>
</template>

<style scoped>
/* Scoped styles specific to GroupDetailView.vue (minimal now) */
.rounded-pill {
  border-radius: 50rem !important;
}
</style>
