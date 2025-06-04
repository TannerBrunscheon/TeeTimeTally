<script setup lang="ts">
import { ref, watch, computed, onMounted } from 'vue';
import { useGolfersStore } from '@/stores/golfers';
import { useGroupsStore } from '@/stores/groups';
import { useAuthenticationStore } from '@/stores/authentication';
import type { Golfer, GroupMember } from '@/models';
import { Permissions } from '@/models/auth/permissions';
import CreateGolferModal from './CreateGolferModal.vue'; // Adjust path if needed

const props = defineProps<{
  groupId: string;
  currentGroupMembers: GroupMember[];
}>();

const emit = defineEmits(['membersAdded', 'close']);

const golfersStore = useGolfersStore();
const groupsStore = useGroupsStore();
const authStore = useAuthenticationStore();

const showModal = ref(false); // For this AddMembersModal
const golferSearchTerm = ref('');
const selectedGolferIdsToAdd = ref<string[]>([]);
const explicitlySelectedGolfers = ref<Golfer[]>([]); // Store full Golfer objects for stickiness
const searchResults = ref<Golfer[]>([]); // Store search results separately

const addMembersError = ref<string | null>(null);
const createGolferModalRef = ref<InstanceType<typeof CreateGolferModal> | null>(null);

const canCreateGolfers = computed(() => authStore.hasPermission(Permissions.CreateGolfers));

// Combined list for display: sticky selected golfers first, then filtered search results
const displayableGolfers = computed(() => {
  const currentMemberIds = new Set(props.currentGroupMembers.map(m => m.golferId));
  const explicitlySelectedIds = new Set(explicitlySelectedGolfers.value.map(g => g.id));

  // Filter search results: not already a group member AND not already in the explicitlySelectedGolfers list
  const filteredSearchResults = searchResults.value.filter(
    g => !currentMemberIds.has(g.id) && !explicitlySelectedIds.has(g.id)
  );

  return [...explicitlySelectedGolfers.value, ...filteredSearchResults];
});


watch(() => props.currentGroupMembers, () => {
  // When current group members change, we might need to re-evaluate
  // explicitlySelectedGolfers if any of them became a group member through other means.
  // Also, re-filter search results.
  const currentMemberIds = new Set(props.currentGroupMembers.map(m => m.golferId));
  explicitlySelectedGolfers.value = explicitlySelectedGolfers.value.filter(g => !currentMemberIds.has(g.id));
  // Re-apply selection to selectedGolferIdsToAdd based on the potentially filtered explicitlySelectedGolfers
  selectedGolferIdsToAdd.value = explicitlySelectedGolfers.value.map(g => g.id);

  filterAndSetSearchResults(); // Re-filter search results
}, { deep: true });


function openModal() {
  showModal.value = true;
  golferSearchTerm.value = '';
  selectedGolferIdsToAdd.value = [];
  explicitlySelectedGolfers.value = [];
  searchResults.value = [];
  addMembersError.value = null;
  searchAvailableGolfers(); // Initial fetch

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
    filterAndSetSearchResults(result.value);
  } else {
    addMembersError.value = result.error?.message || 'Failed to search golfers.';
    searchResults.value = [];
  }
}

// Helper to filter raw search results and update the reactive searchResults ref
function filterAndSetSearchResults(golfers?: Golfer[]) {
    const rawResults = golfers || searchResults.value; // Use new results or existing if just re-filtering
    const currentMemberIds = new Set(props.currentGroupMembers.map(m => m.golferId));
    const explicitlySelectedIds = new Set(explicitlySelectedGolfers.value.map(g => g.id));

    searchResults.value = rawResults.filter(
        g => !currentMemberIds.has(g.id) && !explicitlySelectedIds.has(g.id)
    );
}


function toggleGolferSelection(golfer: Golfer) {
  const indexInSelectedIds = selectedGolferIdsToAdd.value.indexOf(golfer.id);
  if (indexInSelectedIds > -1) {
    selectedGolferIdsToAdd.value.splice(indexInSelectedIds, 1);
    // Remove from explicitlySelectedGolfers
    explicitlySelectedGolfers.value = explicitlySelectedGolfers.value.filter(g => g.id !== golfer.id);
    // If the deselected golfer was part of the last search result, add it back to searchResults
    // (if it matches current search term and isn't a current member)
    // This part is tricky to do perfectly without re-fetching, for simplicity, a re-search might be cleaner
    // or accept that deselected items might not immediately reappear in search without a new search.
    // For now, let's assume a new search will refresh it.
    // OR, add it back to searchResults if it's not there already
    const currentMemberIds = new Set(props.currentGroupMembers.map(m => m.golferId));
    if (!searchResults.value.find(g => g.id === golfer.id) && !currentMemberIds.has(golfer.id)) {
        searchResults.value.unshift(golfer); // Add to top of search results for visibility
    }

  } else {
    selectedGolferIdsToAdd.value.push(golfer.id);
    // Add to explicitlySelectedGolfers if not already there
    if (!explicitlySelectedGolfers.value.find(g => g.id === golfer.id)) {
      explicitlySelectedGolfers.value.push(golfer);
    }
    // Remove from searchResults as it's now "sticky selected"
    searchResults.value = searchResults.value.filter(g => g.id !== golfer.id);
  }
}

async function addMembersToGroup() {
  if (selectedGolferIdsToAdd.value.length === 0) {
    addMembersError.value = 'Please select at least one golfer to add.';
    return;
  }
  addMembersError.value = null;
  const result = await groupsStore.addGolfersToGroup(props.groupId, selectedGolferIdsToAdd.value);
  if (result.isSuccess) {
    emit('membersAdded', result.value);
    closeModal();
  } else {
    addMembersError.value = result.error?.message || 'Failed to add members.';
  }
}

function handleOpenCreateGolferModal() {
  if (createGolferModalRef.value) {
    createGolferModalRef.value.openModal();
  }
}

function handleGolferCreated(newGolfer: Golfer) {
  // Add to explicitly selected list and select it
  if (!explicitlySelectedGolfers.value.find(g => g.id === newGolfer.id)) {
    explicitlySelectedGolfers.value.unshift(newGolfer); // Add to top
  }
  if (!selectedGolferIdsToAdd.value.includes(newGolfer.id)) {
    selectedGolferIdsToAdd.value.push(newGolfer.id);
  }
  // Optionally, refresh the main search list or just rely on the sticky behavior
  // To ensure it doesn't appear in searchResults if it was somehow there:
  searchResults.value = searchResults.value.filter(g => g.id !== newGolfer.id);
}

onMounted(() => {
    // Ensure Bootstrap modal JS is loaded if you are not using a Vue-specific Bootstrap library
    // This is often handled globally in main.ts
});


defineExpose({
  openModal,
  closeModal
});
</script>

<template>
  <div class="modal fade" id="addMembersModal" tabindex="-1" aria-labelledby="addMembersModalLabel" aria-hidden="true">
    <div class="modal-dialog modal-lg modal-dialog-scrollable">
      <div class="modal-content">
        <div class="modal-header bg-success text-white">
          <h5 class="modal-title" id="addMembersModalLabel">Add Members to Group</h5>
          <button type="button" class="btn-close btn-close-white" @click="closeModal" aria-label="Close"></button>
        </div>
        <div class="modal-body">
          <div class="d-flex justify-content-end mb-3" v-if="canCreateGolfers">
            <button type="button" class="btn btn-sm btn-info rounded-pill" @click="handleOpenCreateGolferModal">
              <i class="bi bi-person-plus-fill"></i> Create New Golfer
            </button>
          </div>

          <div class="mb-3">
            <label for="golferSearch" class="form-label">Search Golfers (by name or email)</label>
            <div class="input-group">
              <input type="text" class="form-control" id="golferSearch" v-model="golferSearchTerm" @keyup.enter="searchAvailableGolfers" placeholder="Enter name or email">
              <button class="btn btn-outline-secondary" type="button" @click="searchAvailableGolfers">
                <i class="bi bi-search"></i> Search
              </button>
            </div>
          </div>

          <div v-if="golfersStore.isLoadingGolfers" class="text-center my-3">
            <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
            <p class="mt-1">Searching golfers...</p>
          </div>
          <div v-else-if="addMembersError" class="alert alert-danger">{{ addMembersError }}</div>

          <div v-if="displayableGolfers.length === 0 && !golfersStore.isLoadingGolfers && !addMembersError" class="alert alert-info">
            No available golfers found matching your criteria, or all found are already members.
          </div>

          <ul v-if="displayableGolfers.length > 0" class="list-group">
            <li v-for="golfer in displayableGolfers" :key="golfer.id" class="list-group-item d-flex justify-content-between align-items-center"
                :class="{ 'list-group-item-secondary fst-italic': explicitlySelectedGolfers.find(g => g.id === golfer.id) }">
              <div>
                <input
                  class="form-check-input me-2"
                  type="checkbox"
                  :value="golfer.id"
                  :id="`golfer-add-${golfer.id}`"
                  @change="toggleGolferSelection(golfer)"
                  :checked="selectedGolferIdsToAdd.includes(golfer.id)">
                <label :for="`golfer-add-${golfer.id}`">
                  {{ golfer.fullName }} ({{ golfer.email }})
                  <span v-if="explicitlySelectedGolfers.find(g => g.id === golfer.id)" class="badge bg-primary ms-2">Selected</span>
                </label>
              </div>
            </li>
          </ul>
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary rounded-pill" @click="closeModal">Close</button>
          <button
            type="button"
            class="btn btn-success rounded-pill"
            @click="addMembersToGroup"
            :disabled="selectedGolferIdsToAdd.length === 0 || groupsStore.isManagingMembers">
            <span v-if="groupsStore.isManagingMembers" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            Add Selected ({{ selectedGolferIdsToAdd.length }})
          </button>
        </div>
      </div>
    </div>
  </div>
  <CreateGolferModal ref="createGolferModalRef" @golferCreated="handleGolferCreated" />
</template>

<style scoped>
.rounded-pill {
  border-radius: 50rem !important;
}
.btn-close-white {
  filter: invert(1) grayscale(100%) brightness(200%);
}
.list-group-item-secondary { /* Style for sticky selected items */
    background-color: #e2e3e5; /* A light grey, adjust as needed */
}
</style>
