<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import type { Group, GroupMember } from '@/models';
import { useGroupsStore } from '@/stores/groups';
import { useAuthenticationStore } from '@/stores/authentication';
import { Permissions } from '@/models/auth/permissions';
import AddMembersModal from './GroupMembers/AddMembersModal.vue'; // Import the new modal component

const props = defineProps<{
  group: Group;
  groupMembers: GroupMember[];
}>();

const emit = defineEmits(['membersUpdated']);

const groupsStore = useGroupsStore();
const authStore = useAuthenticationStore();

const addMembersModalRef = ref<InstanceType<typeof AddMembersModal> | null>(null);

// State for removing members confirmation
const showConfirmRemoveModal = ref(false);
const golferToRemoveId = ref<string | null>(null);
const golferToRemoveName = ref('');

// State for changing scorer status confirmation
const showConfirmScorerStatusModal = ref(false);
const golferToChangeScorerStatusId = ref<string | null>(null);
const golferToChangeScorerStatusName = ref('');
const newScorerStatus = ref(false); // true for promote, false for demote

const canManageMembers = computed(() => authStore.hasPermission(Permissions.ManageGroupMembers));
const canManageScorers = computed(() => authStore.hasPermission(Permissions.ManageGroupScorers));

function openAddMembersModal() {
  addMembersModalRef.value?.openModal();
}

function handleMembersAdded() {
  emit('membersUpdated'); // Notify parent to refresh group members
}

function confirmRemoveMember(golferId: string, fullName: string) {
  golferToRemoveId.value = golferId;
  golferToRemoveName.value = fullName;
  showConfirmRemoveModal.value = true;
  const modalElement = document.getElementById('removeMemberModal');
  if (modalElement) {
    const modal = new (window as any).bootstrap.Modal(modalElement);
    modal.show();
  }
}

async function removeMember() {
  if (!props.group || !golferToRemoveId.value) return;

  const result = await groupsStore.removeGolfersFromGroup(props.group.id, [golferToRemoveId.value]);
  if (result.isSuccess) {
    emit('membersUpdated'); // Notify parent to refresh group members
  } else {
    alert(`Error removing member: ${result.error?.message || 'Unknown error'}`); // Replace with nicer modal/toast
  }
  golferToRemoveId.value = null;
  golferToRemoveName.value = '';
  showConfirmRemoveModal.value = false;
  const modalElement = document.getElementById('removeMemberModal');
  if (modalElement) {
    const modal = (window as any).bootstrap.Modal.getInstance(modalElement);
    modal?.hide();
  }
}

function confirmScorerStatusChange(golferId: string, fullName: string, currentStatus: boolean) {
  golferToChangeScorerStatusId.value = golferId;
  golferToChangeScorerStatusName.value = fullName;
  newScorerStatus.value = !currentStatus; // Toggle status
  showConfirmScorerStatusModal.value = true;
  const modalElement = document.getElementById('scorerStatusModal');
  if (modalElement) {
    const modal = new (window as any).bootstrap.Modal(modalElement);
    modal.show();
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
    emit('membersUpdated'); // Notify parent to refresh group members
  } else {
    alert(`Error changing scorer status: ${result.error?.message || 'Unknown error'}`); // Replace with nicer modal/toast
  }
  golferToChangeScorerStatusId.value = null;
  golferToChangeScorerStatusName.value = '';
  showConfirmScorerStatusModal.value = false;
  const modalElement = document.getElementById('scorerStatusModal');
  if (modalElement) {
    const modal = (window as any).bootstrap.Modal.getInstance(modalElement);
    modal?.hide();
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

  <div class="modal fade" id="removeMemberModal" tabindex="-1" aria-labelledby="removeMemberModalLabel" aria-hidden="true">
    <div class="modal-dialog">
      <div class="modal-content">
        <div class="modal-header bg-danger text-white">
          <h5 class="modal-title" id="removeMemberModalLabel">Confirm Removal</h5>
          <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal" aria-label="Close"></button>
        </div>
        <div class="modal-body">
          Are you sure you want to remove <strong>{{ golferToRemoveName }}</strong> from this group?
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary rounded-pill" data-bs-dismiss="modal">Cancel</button>
          <button type="button" class="btn btn-danger rounded-pill" @click="removeMember" :disabled="groupsStore.isManagingMembers">
            <span v-if="groupsStore.isManagingMembers" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            Remove
          </button>
        </div>
      </div>
    </div>
  </div>

  <div class="modal fade" id="scorerStatusModal" tabindex="-1" aria-labelledby="scorerStatusModalLabel" aria-hidden="true">
    <div class="modal-dialog">
      <div class="modal-content">
        <div class="modal-header bg-primary text-white">
          <h5 class="modal-title" id="scorerStatusModalLabel">Confirm Scorer Status Change</h5>
          <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal" aria-label="Close"></button>
        </div>
        <div class="modal-body">
          Are you sure you want to {{ newScorerStatus ? 'promote' : 'demote' }} <strong>{{ golferToChangeScorerStatusName }}</strong> to {{ newScorerStatus ? 'a scorer' : 'a regular member' }} in this group?
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary rounded-pill" data-bs-dismiss="modal">Cancel</button>
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
