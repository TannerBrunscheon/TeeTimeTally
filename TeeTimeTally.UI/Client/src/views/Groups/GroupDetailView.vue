<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useGroupDetail } from '@/composables/useGroupDetail';
import { useGroupsStore } from '@/stores/groups';

// Import the sub-components
import GroupInfoCard from './GroupDetail/GroupInfoCard.vue';
import FinancialEditorCard from './GroupDetail/FinancialEditorCard.vue'; // New import
import GroupMembersCard from './GroupDetail/GroupMembersCard.vue';
import GroupRoundHistoryCard from './GroupDetail/GroupRoundHistoryCard.vue';
import GroupYearReportsSection from './GroupDetail/GroupYearReportsSection.vue';

const route = useRoute();
const router = useRouter();
const props = defineProps({ groupId: { type: String, required: true } });

const {
  groupIdRef,
  group,
  groupMembers,
  defaultCourseName,
  financialCardMode,
  showReportModal,
  loadGroupData,
  handleGroupUpdated,
  handleEditFinancialsClicked,
  handleFinancialsSuccessfullyUpdated,
  handleCancelFinancialEdit,
  handleMembersUpdated,
} = useGroupDetail(String(props.groupId));

onMounted(() => loadGroupData());

const groupsStore = useGroupsStore();

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
        <div class="d-flex align-items-center justify-content-between mb-3">
          <h2 class="mb-0">{{ group.name }} Details</h2>
        </div>

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

  <GroupRoundHistoryCard :group-id="props.groupId" />
  <GroupYearReportsSection :group-id="String(props.groupId)" />
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
