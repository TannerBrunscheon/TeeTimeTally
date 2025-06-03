<script setup lang="ts">
import { ref, watch } from 'vue';
import { useGolfersStore } from '@/stores/golfers';
import { useGroupsStore } from '@/stores/groups';
import type { Golfer, GroupMember } from '@/models'; // Assuming Golfer and GroupMember are in models
import { AppError } from '@/primitives/error'; // Assuming AppError is defined here

const props = defineProps<{
  groupId: string;
  currentGroupMembers: GroupMember[]; // Pass existing members to filter out
}>();

const emit = defineEmits(['membersAdded', 'close']);

const golfersStore = useGolfersStore();
const groupsStore = useGroupsStore();

const showModal = ref(false);
const golferSearchTerm = ref('');
const selectedGolfersToAdd = ref<string[]>([]); // Array of golfer IDs
const addMembersError = ref<string | null>(null);

// Watch for changes in currentGroupMembers to ensure availableGolfers list is always up-to-date
watch(() => props.currentGroupMembers, () => {
  // Re-filter available golfers when current group members change
  filterAvailableGolfers();
}, { deep: true });

function openModal() {
  showModal.value = true;
  golferSearchTerm.value = '';
  selectedGolfersToAdd.value = [];
  addMembersError.value = null;
  // Fetch initial list of golfers (e.g., top 50)
  searchAvailableGolfers();
  const modalElement = document.getElementById('addMembersModal');
  if (modalElement) {
    const modal = new (window as any).bootstrap.Modal(modalElement);
    modal.show();
  }
}

function closeModal() {
  showModal.value = false;
  emit('close');
  const modalElement = document.getElementById('addMembersModal');
  if (modalElement) {
    const modal = (window as any).bootstrap.Modal.getInstance(modalElement);
    if (modal) {
      modal.hide();
    }
  }
}

async function searchAvailableGolfers() {
  addMembersError.value = null;
  const result = await golfersStore.searchGolfers({ search: golferSearchTerm.value, limit: 50 });
  if (result.isSuccess && result.value !== undefined) {
    // Filter out golfers already in the group
    const currentMemberIds = new Set(props.currentGroupMembers.map(m => m.golferId));
    golfersStore.golfers = result.value.filter(g => !currentMemberIds.has(g.id)); // Update store's internal list
  } else {
    addMembersError.value = result.error?.message || 'Failed to search golfers.';
    golfersStore.golfers = []; // Clear golfers on error
  }
}

function filterAvailableGolfers() {
  // Re-filter golfersStore.golfers based on currentGroupMembers
  const currentMemberIds = new Set(props.currentGroupMembers.map(m => m.golferId));
  golfersStore.golfers = golfersStore.golfers.filter(g => !currentMemberIds.has(g.id));
}


function toggleGolferSelection(golferId: string) {
  const index = selectedGolfersToAdd.value.indexOf(golferId);
  if (index > -1) {
    selectedGolfersToAdd.value.splice(index, 1);
  } else {
    selectedGolfersToAdd.value.push(golferId);
  }
}

async function addMembersToGroup() {
  if (selectedGolfersToAdd.value.length === 0) {
    addMembersError.value = 'Please select at least one golfer to add.';
    return;
  }

  addMembersError.value = null;
  const result = await groupsStore.addGolfersToGroup(props.groupId, selectedGolfersToAdd.value);
  if (result.isSuccess) {
    emit('membersAdded', result.value); // Emit success with response data
    closeModal(); // Close modal on success
  } else {
    addMembersError.value = result.error?.message || 'Failed to add members.';
  }
}

defineExpose({
  openModal,
  closeModal
});
</script>

<template>
  <div class="modal fade" id="addMembersModal" tabindex="-1" aria-labelledby="addMembersModalLabel" aria-hidden="true">
    <div class="modal-dialog modal-lg">
      <div class="modal-content">
        <div class="modal-header bg-success text-white">
          <h5 class="modal-title" id="addMembersModalLabel">Add Members to Group</h5>
          <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal" aria-label="Close" @click="closeModal"></button>
        </div>
        <div class="modal-body">
          <div class="mb-3">
            <label for="golferSearch" class="form-label">Search Golfers (by name or email)</label>
            <div class="input-group">
              <input type="text" class="form-control" id="golferSearch" v-model="golferSearchTerm" @keyup.enter="searchAvailableGolfers" placeholder="Enter name or email">
              <button class="btn btn-outline-secondary" type="button" @click="searchAvailableGolfers">Search</button>
            </div>
          </div>

          <div v-if="golfersStore.isLoadingGolfers" class="text-center">
            <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
            <p>Searching golfers...</p>
          </div>
          <div v-else-if="addMembersError" class="alert alert-danger">{{ addMembersError }}</div>
          <div v-else-if="golfersStore.golfers.length === 0" class="alert alert-info">No golfers found or all are already members.</div>
          <div v-else>
            <h6>Available Golfers:</h6>
            <ul class="list-group">
              <li v-for="golfer in golfersStore.golfers" :key="golfer.id" class="list-group-item d-flex justify-content-between align-items-center">
                <div>
                  <input class="form-check-input me-2" type="checkbox" :value="golfer.id" :id="`golfer-${golfer.id}`"
                         @change="toggleGolferSelection(golfer.id)" :checked="selectedGolfersToAdd.includes(golfer.id)">
                  <label :for="`golfer-${golfer.id}`">
                    {{ golfer.fullName }} ({{ golfer.email }})
                  </label>
                </div>
              </li>
            </ul>
          </div>
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary rounded-pill" data-bs-dismiss="modal" @click="closeModal">Close</button>
          <button type="button" class="btn btn-success rounded-pill" @click="addMembersToGroup" :disabled="selectedGolfersToAdd.length === 0 || groupsStore.isManagingMembers">
            <span v-if="groupsStore.isManagingMembers" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            Add Selected ({{ selectedGolfersToAdd.length }})
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.rounded-pill {
  border-radius: 50rem !important;
}
.btn-close-white {
  filter: invert(1) grayscale(100%) brightness(200%);
}
</style>
