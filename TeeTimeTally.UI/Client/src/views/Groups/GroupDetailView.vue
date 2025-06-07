<script setup lang="ts">
import { ref, onMounted, computed, watch, nextTick } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useGroupsStore } from '@/stores/groups';
import { useCoursesStore } from '@/stores/courses';
import type { Group, GroupMember } from '@/models';

// Import the sub-components
import GroupInfoCard from './GroupDetail/GroupInfoCard.vue';
// import FinancialConfigCard from './GroupDetail/FinancialConfigCard.vue'; // Old import removed
import FinancialEditorCard from './FinancialEditorCard.vue'; // New import
import GroupMembersCard from './GroupDetail/GroupMembersCard.vue';

const groupsStore = useGroupsStore();
const courseStore = useCoursesStore();
const route = useRoute();
const router = useRouter();

const groupId = ref<string | string[]>(route.params.groupId);
const group = computed(() => groupsStore.currentGroup);
const groupMembers = ref<GroupMember[]>([]);
const defaultCourseName = ref<string>('N/A');

// State for FinancialEditorCard mode
const financialCardMode = ref<'view' | 'editForm'>('view');

// --- Data Loading ---
async function loadGroupData() {
  console.log('Parent (GroupDetailView): loadGroupData called. Group ID:', groupId.value);
  if (!groupId.value || Array.isArray(groupId.value) && groupId.value.length === 0) {
    console.warn('Parent (GroupDetailView): Invalid or missing groupId. Redirecting to groups index.');
    router.push({ name: 'groups-index' });
    return;
  }

  const currentGroupId = Array.isArray(groupId.value) ? groupId.value[0] : groupId.value;

  const groupResult = await groupsStore.fetchGroupById(currentGroupId);
  if (groupResult.isFailure) {
    console.error('Parent (GroupDetailView): Failed to load group:', groupResult.error?.message);
    return;
  }

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

  console.log('Parent (GroupDetailView): Fetching group members for group ID:', currentGroupId);
  const membersResult = await groupsStore.fetchGroupMembers(currentGroupId);
  if (membersResult.isSuccess && membersResult.value !== undefined) {
    groupMembers.value = [...membersResult.value];
    console.log('Parent (GroupDetailView): groupMembers ref updated. Count:', groupMembers.value.length);
  } else {
    console.error('Parent (GroupDetailView): Failed to load group members:', membersResult.error?.message);
    groupMembers.value = [];
    console.log('Parent (GroupDetailView): groupMembers ref set to empty array.');
  }
}

onMounted(loadGroupData);

watch(() => route.params.groupId, (newId) => {
  console.log('Parent (GroupDetailView): route.params.groupId changed to:', newId);
  if (newId && (!Array.isArray(newId) || newId.length > 0) ) {
    groupId.value = newId;
    loadGroupData();
  } else if (!newId) {
     console.warn('Parent (GroupDetailView): route.params.groupId changed to undefined/null.');
  }
}, { immediate: false });

// --- Event Handlers for Child Components ---
async function handleGroupUpdated() {
  console.log('Parent (GroupDetailView): handleGroupUpdated called. Reloading group data.');
  const scrollY = window.scrollY;
  await loadGroupData();
  await nextTick();
  window.scrollTo(0, scrollY);
  console.log('Parent (GroupDetailView): Scroll position restored after group update.');
}

// Handlers for FinancialEditorCard
function handleEditFinancialsClicked() {
  console.log('Parent (GroupDetailView): Edit financials clicked. Changing mode to editForm.');
  financialCardMode.value = 'editForm';
}

async function handleFinancialsSuccessfullyUpdated() {
  console.log('Parent (GroupDetailView): Financials successfully updated in child. Reloading data and switching to view mode.');
  const scrollY = window.scrollY;
  await loadGroupData(); // Reload group data which includes activeFinancialConfiguration
  await nextTick();
  window.scrollTo(0, scrollY);
  financialCardMode.value = 'view'; // Switch back to view mode
  console.log('Parent (GroupDetailView): Scroll position restored and mode set to view.');
}

function handleCancelFinancialEdit() {
  console.log('Parent (GroupDetailView): Cancel financial edit clicked. Switching to view mode.');
  financialCardMode.value = 'view';
  // FinancialEditorCard is responsible for resetting its own internal form state upon mode change or cancel
}

async function handleMembersUpdated() {
  console.log('Parent (GroupDetailView): handleMembersUpdated called. Reloading group data.');
  const scrollY = window.scrollY;
  await loadGroupData();
  await nextTick();
  window.scrollTo(0, scrollY);
  console.log('Parent (GroupDetailView): Scroll position restored after members update.');
}

function createNewGroup() {
  router.push({ name: 'groups-index' });
}
</script>

<template>
  <div class="container mt-4">
    <button @click="createNewGroup" class="btn btn-secondary mb-3 rounded-pill">
      <i class="bi bi-arrow-left-circle"></i> Back to Groups
    </button>

    <div v-if="groupsStore.isLoadingGroupDetail && !group" class="text-center">
      <div class="spinner-border text-primary" role="status">
        <span class="visually-hidden">Loading...</span>
      </div>
      <p>Loading group details...</p>
    </div>

    <div v-else-if="groupsStore.groupDetailError" class="alert alert-danger" role="alert">
      Error: {{ groupsStore.groupDetailError.message }}
    </div>

    <div v-else-if="group">
      <h2 class="mb-4">{{ group.name }} Details</h2>

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

      <FinancialEditorCard
        :group="group"
        :mode="financialCardMode"
        @edit-clicked="handleEditFinancialsClicked"
        @financial-config-updated="handleFinancialsSuccessfullyUpdated"
        @cancel-edit="handleCancelFinancialEdit"
      />

      <GroupMembersCard
        :group="group"
        :group-members="groupMembers"
        @members-updated="handleMembersUpdated"
      />
    </div>

    <div v-else class="alert alert-warning" role="alert">
      Group not found.
    </div>
  </div>
</template>

<style scoped>
.rounded-pill {
  border-radius: 50rem !important;
}
</style>
