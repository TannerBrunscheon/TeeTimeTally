<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue';
import type { Group, GroupMember } from '@/models';
import { useGroupsStore } from '@/stores/groups';
import { useAuthenticationStore } from '@/stores/authentication';
import { Permissions } from '@/models/auth/permissions';
import AddMembersModal from './GroupMembers/AddMembersModal.vue';

const props = defineProps<{
  group: Group;
  groupMembers: GroupMember[];
}>();

const emit = defineEmits(['membersUpdated']);

const groupsStore = useGroupsStore();
const authStore = useAuthenticationStore();

const addMembersModalRef = ref<InstanceType<typeof AddMembersModal> | null>(null);

// --- Refs for Remove Member Modal ---
const removeMemberModalElementRef = ref<HTMLElement | null>(null);
const bsRemoveMemberModalInstance = ref<any>(null); // Bootstrap Modal Instance
const showConfirmRemoveModal = ref(false);
const golferToRemoveId = ref<string | null>(null);
const golferToRemoveName = ref('');

// --- Refs for Scorer Status Modal ---
const scorerStatusModalElementRef = ref<HTMLElement | null>(null);
const bsScorerStatusModalInstance = ref<any>(null); // Bootstrap Modal Instance
const showConfirmScorerStatusModal = ref(false);
const golferToChangeScorerStatusId = ref<string | null>(null);
const golferToChangeScorerStatusName = ref('');
const newScorerStatus = ref(false);

const canManageMembers = computed(() => authStore.hasPermission(Permissions.ManageGroupMembers));
const canManageScorers = computed(() => authStore.hasPermission(Permissions.ManageGroupScorers));

// Watch for changes in the groupMembers prop
watch(() => props.groupMembers, (newVal, oldVal) => {
  console.log('GroupMembersCard: groupMembers prop changed.');
  console.log('GroupMembersCard: Old members count:', oldVal ? oldVal.length : 'N/A');
  console.log('GroupMembersCard: New members count:', newVal ? newVal.length : 'N/A');
  // console.log('GroupMembersCard: New members data:', JSON.parse(JSON.stringify(newVal)));
}, { deep: true });

// --- Modal Hidden Handlers ---
function handleRemoveMemberModalHidden() {
  showConfirmRemoveModal.value = false;
  golferToRemoveId.value = null;
  golferToRemoveName.value = '';
  console.log('GroupMembersCard: Remove member modal hidden and state reset.');
}

function handleScorerStatusModalHidden() {
  showConfirmScorerStatusModal.value = false;
  golferToChangeScorerStatusId.value = null;
  golferToChangeScorerStatusName.value = '';
  console.log('GroupMembersCard: Scorer status modal hidden and state reset.');
}

onMounted(() => {
  console.log('GroupMembersCard: Mounted. Initial groupMembers count:', props.groupMembers.length);
  // Initialize Remove Member Modal
  const removeModalEl = document.getElementById('removeMemberModal');
  if (removeModalEl) {
    removeMemberModalElementRef.value = removeModalEl;
    bsRemoveMemberModalInstance.value = new (window as any).bootstrap.Modal(removeModalEl);
    removeModalEl.addEventListener('hidden.bs.modal', handleRemoveMemberModalHidden);
  } else {
    console.error('GroupMembersCard: removeMemberModal element not found.');
  }

  // Initialize Scorer Status Modal
  const scorerModalEl = document.getElementById('scorerStatusModal');
  if (scorerModalEl) {
    scorerStatusModalElementRef.value = scorerModalEl;
    bsScorerStatusModalInstance.value = new (window as any).bootstrap.Modal(scorerModalEl);
    scorerModalEl.addEventListener('hidden.bs.modal', handleScorerStatusModalHidden);
  } else {
    console.error('GroupMembersCard: scorerStatusModal element not found.');
  }
});

onBeforeUnmount(() => {
  // Cleanup for Remove Member Modal
  if (removeMemberModalElementRef.value) {
    removeMemberModalElementRef.value.removeEventListener('hidden.bs.modal', handleRemoveMemberModalHidden);
  }
  if (bsRemoveMemberModalInstance.value) {
    // bsRemoveMemberModalInstance.value.dispose();
  }

  // Cleanup for Scorer Status Modal
  if (scorerStatusModalElementRef.value) {
    scorerStatusModalElementRef.value.removeEventListener('hidden.bs.modal', handleScorerStatusModalHidden);
  }
  if (bsScorerStatusModalInstance.value) {
    // bsScorerStatusModalInstance.value.dispose();
  }
});

function openAddMembersModal() {
  addMembersModalRef.value?.openModal();
}

function handleMembersAdded(eventPayload: any) { // Added eventPayload for logging
  console.log('GroupMembersCard: handleMembersAdded called. Event payload from AddMembersModal:', eventPayload);
  console.log('GroupMembersCard: Emitting membersUpdated due to members being added.');
  emit('membersUpdated');
}

function confirmRemoveMember(golferId: string, fullName: string) {
  golferToRemoveId.value = golferId;
  golferToRemoveName.value = fullName;
  showConfirmRemoveModal.value = true;
  if (bsRemoveMemberModalInstance.value) {
    bsRemoveMemberModalInstance.value.show();
  } else {
    console.error('Cannot show remove member modal, instance not available.');
    const modalElement = document.getElementById('removeMemberModal');
    if (modalElement) new (window as any).bootstrap.Modal(modalElement).show();
  }
}

async function removeMember() {
  if (!props.group || !golferToRemoveId.value) return;

  const result = await groupsStore.removeGolfersFromGroup(props.group.id, [golferToRemoveId.value]);

  if (result.isSuccess) {
    console.log('GroupMembersCard: Member removed successfully via API. Emitting membersUpdated.');
    emit('membersUpdated');
  } else {
    alert(`Error removing member: ${result.error?.message || 'Unknown error'}`);
    console.error('GroupMembersCard: Error removing member:', result.error);
  }
  if (bsRemoveMemberModalInstance.value) {
    bsRemoveMemberModalInstance.value.hide();
  }
}

function confirmScorerStatusChange(golferId: string, fullName: string, currentStatus: boolean) {
  golferToChangeScorerStatusId.value = golferId;
  golferToChangeScorerStatusName.value = fullName;
  newScorerStatus.value = !currentStatus;
  showConfirmScorerStatusModal.value = true;
  if (bsScorerStatusModalInstance.value) {
    bsScorerStatusModalInstance.value.show();
  } else {
    console.error('Cannot show scorer status modal, instance not available.');
    const modalElement = document.getElementById('scorerStatusModal');
    if (modalElement) new (window as any).bootstrap.Modal(modalElement).show();
  }
}

async function changeScorerStatus() {
  if (!props.group || !golferToChangeScorerStatusId.value) return;

  const result = await groupsStore.setGroupMemberScorerStatus(
    props.group.id,
    golferToChangeScorerStatusId.value,
    newScorerStatus.value
  );

  if (result.isSuccess) {
    console.log('GroupMembersCard: Scorer status changed successfully via API. Emitting membersUpdated.');
    emit('membersUpdated');
  } else {
    alert(`Error changing scorer status: ${result.error?.message || 'Unknown error'}`);
    console.error('GroupMembersCard: Error changing scorer status:', result.error);
  }
  if (bsScorerStatusModalInstance.value) {
    bsScorerStatusModalInstance.value.hide();
  }
}
</script>

<template>
  <div class="card shadow-sm mb-4">
    <div class="card-header d-flex justify-content-between align-items-center bg-success text-white">
      Group Members
      <button v-if="canManageMembers" class="btn btn-sm btn-light rounded-pill" @click="openAddMembersModal">
        <i class="bi bi-person-plus"></i> Add Members
      </button>
    </div>
    <div class="card-body">
      <div v-if="groupsStore.isManagingMembers" class="text-center">
        <div class="spinner-border spinner-border-sm text-success" role="status">
          <span class="visually-hidden">Loading...</span>
        </div>
        <p>Updating members...</p>
      </div>
      <div v-else-if="groupMembers.length === 0" class="alert alert-info" role="alert">
        No members in this group.
      </div>
      <ul v-else class="list-group list-group-flush">
        <li v-for="member in groupMembers" :key="member.golferId" class="list-group-item d-flex justify-content-between align-items-center">
          <div>
            <strong>{{ member.fullName }}</strong> ({{ member.email }})
            <span v-if="member.isScorer" class="badge bg-primary ms-2">Scorer</span>
          </div>
          <div>
            <button
              v-if="canManageScorers && member.golferId !== authStore.user?.id"
              class="btn btn-sm btn-outline-primary me-2 rounded-pill"
              @click="confirmScorerStatusChange(member.golferId, member.fullName, member.isScorer)"
            >
              <i :class="member.isScorer ? 'bi bi-person-dash' : 'bi bi-person-plus'"></i>
              {{ member.isScorer ? 'Demote Scorer' : 'Promote Scorer' }}
            </button>
            <button
              v-if="canManageMembers && member.golferId !== authStore.user?.id"
              class="btn btn-sm btn-outline-danger rounded-pill"
              @click="confirmRemoveMember(member.golferId, member.fullName)"
            >
              <i class="bi bi-person-x"></i> Remove
            </button>
          </div>
        </li>
      </ul>
    </div>
  </div>

  <AddMembersModal
    ref="addMembersModalRef"
    :group-id="props.group.id"
    :current-group-members="props.groupMembers"
    @members-added="handleMembersAdded"
  />

  <!-- Remove Member Confirmation Modal -->
  <div class="modal fade" id="removeMemberModal" tabindex="-1" aria-labelledby="removeMemberModalLabel" aria-hidden="true">
    <div class="modal-dialog">
      <div class="modal-content">
        <div class="modal-header bg-danger text-white">
          <h5 class="modal-title" id="removeMemberModalLabel">Confirm Removal</h5>
          <button type="button" class="btn-close btn-close-white" @click="bsRemoveMemberModalInstance?.hide()" aria-label="Close"></button>
        </div>
        <div class="modal-body">
          Are you sure you want to remove <strong>{{ golferToRemoveName }}</strong> from this group?
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary rounded-pill" @click="bsRemoveMemberModalInstance?.hide()">Cancel</button>
          <button type="button" class="btn btn-danger rounded-pill" @click="removeMember" :disabled="groupsStore.isManagingMembers">
            <span v-if="groupsStore.isManagingMembers" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            Remove
          </button>
        </div>
      </div>
    </div>
  </div>

  <!-- Scorer Status Confirmation Modal -->
  <div class="modal fade" id="scorerStatusModal" tabindex="-1" aria-labelledby="scorerStatusModalLabel" aria-hidden="true">
    <div class="modal-dialog">
      <div class="modal-content">
        <div class="modal-header bg-primary text-white">
          <h5 class="modal-title" id="scorerStatusModalLabel">Confirm Scorer Status Change</h5>
          <button type="button" class="btn-close btn-close-white" @click="bsScorerStatusModalInstance?.hide()" aria-label="Close"></button>
        </div>
        <div class="modal-body">
          Are you sure you want to {{ newScorerStatus ? 'promote' : 'demote' }} <strong>{{ golferToChangeScorerStatusName }}</strong> to {{ newScorerStatus ? 'a scorer' : 'a regular member' }} in this group?
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary rounded-pill" @click="bsScorerStatusModalInstance?.hide()">Cancel</button>
          <button type="button" class="btn btn-primary rounded-pill" @click="changeScorerStatus" :disabled="groupsStore.isManagingMembers">
            <span v-if="groupsStore.isManagingMembers" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            Confirm
          </button>
        </div>
      </div>
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
.btn-outline-primary {
  color: #007bff;
  border-color: #007bff;
}
.btn-outline-primary:hover {
  background-color: #007bff;
  color: #fff;
}
.btn-outline-danger {
  color: #dc3545;
  border-color: #dc3545;
}
.btn-outline-danger:hover {
  background-color: #dc3545;
  color: #fff;
}
.btn-close-white {
  filter: invert(1) grayscale(100%) brightness(200%);
}
</style>
