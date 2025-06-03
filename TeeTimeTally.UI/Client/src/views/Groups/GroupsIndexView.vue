<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { useRouter } from 'vue-router';
import { useGroupsStore } from '@/stores/groups';
import { useAuthenticationStore } from '@/stores/authentication';
import { Permissions } from '@/models/auth/permissions';
import type { Group } from '@/models/group';

const groupsStore = useGroupsStore();
const authStore = useAuthenticationStore();
const router = useRouter();

const currentPage = ref(1);
const itemsPerPage = 10; // You can adjust this value
const selectedGroupIdToDelete = ref<string | null>(null);

const paginatedGroups = computed<Group[]>(() => {
  const start = (currentPage.value - 1) * itemsPerPage;
  const end = start + itemsPerPage;
  // Simulate pagination, as the API currently fetches all
  return groupsStore.groups.slice(start, end);
});

const totalPages = computed(() => {
  return Math.ceil(groupsStore.groups.length / itemsPerPage);
});

const canCreateGroups = computed(() => authStore.hasPermission(Permissions.CreateGroups));
const canManageAllGroups = computed(() => authStore.hasPermission(Permissions.ManageAllGroups));

onMounted(async () => {
  await groupsStore.fetchAllGroups();
});

function goToPage(page: number) {
  if (page >= 1 && page <= totalPages.value) {
    currentPage.value = page;
  }
}

function viewGroupDetails(groupId: string) {
  router.push({ name: 'group-detail', params: { groupId } });
}

function createNewGroup() {
  router.push({ name: 'create-group' });
}

function confirmDeleteGroup(groupId: string) {
  selectedGroupIdToDelete.value = groupId;
  const deleteModal = new (window as any).bootstrap.Modal(document.getElementById('deleteGroupModal'));
  deleteModal.show();
}

async function executeDeleteGroup() {
  if (selectedGroupIdToDelete.value) {
    const result = await groupsStore.deleteGroup(selectedGroupIdToDelete.value);
    if (result.isSuccess) {
      // Success handled by store refreshing the list
    } else {
      alert(`Error deleting group: ${result.error?.message}`); // Replace with a nicer modal/toast
    }
    selectedGroupIdToDelete.value = null;
    const deleteModal = (window as any).bootstrap.Modal.getInstance(document.getElementById('deleteGroupModal'));
    deleteModal.hide();
  }
}
</script>

<template>
  <div class="container mt-4">
    <div class="d-flex justify-content-between align-items-center mb-4">
      <h2>Your Groups</h2>
      <button v-if="canCreateGroups" class="btn btn-primary" @click="createNewGroup">
        <i class="bi bi-plus-circle"></i> Create New Group
      </button>
    </div>

    <div v-if="groupsStore.isLoadingGroups" class="text-center">
      <div class="spinner-border text-primary" role="status">
        <span class="visually-hidden">Loading...</span>
      </div>
      <p>Loading groups...</p>
    </div>

    <div v-else-if="groupsStore.groupsError" class="alert alert-danger" role="alert">
      Error: {{ groupsStore.groupsError.message }}
    </div>

    <div v-else-if="paginatedGroups.length === 0" class="alert alert-info" role="alert">
      No groups found. <span v-if="canCreateGroups">Click "Create New Group" to get started!</span>
    </div>

    <div v-else class="list-group">
      <div v-for="group in paginatedGroups" :key="group.id" class="list-group-item list-group-item-action d-flex justify-content-between align-items-center mb-2 rounded shadow-sm">
        <div @click="viewGroupDetails(group.id)" class="flex-grow-1 cursor-pointer p-2">
          <h5 class="mb-1">{{ group.name }}</h5>
          <small class="text-muted">
            Members: {{ group.activeFinancialConfiguration?.buyInAmount ? 'Yes' : 'No' }} |
            Created: {{ new Date(group.createdAt).toLocaleDateString() }}
          </small>
        </div>
        <div class="d-flex align-items-center">
          <button @click="viewGroupDetails(group.id)" class="btn btn-info btn-sm me-2 rounded-pill">
            <i class="bi bi-eye"></i> View
          </button>
          <button v-if="canManageAllGroups" @click="confirmDeleteGroup(group.id)" class="btn btn-danger btn-sm rounded-pill">
            <i class="bi bi-trash"></i> Delete
          </button>
        </div>
      </div>

      <nav v-if="totalPages > 1" aria-label="Group list pagination" class="mt-4">
        <ul class="pagination justify-content-center">
          <li class="page-item" :class="{ disabled: currentPage === 1 }">
            <a class="page-link" href="#" @click.prevent="goToPage(currentPage - 1)">Previous</a>
          </li>
          <li v-for="page in totalPages" :key="page" class="page-item" :class="{ active: currentPage === page }">
            <a class="page-link" href="#" @click.prevent="goToPage(page)">{{ page }}</a>
          </li>
          <li class="page-item" :class="{ disabled: currentPage === totalPages }">
            <a class="page-link" href="#" @click.prevent="goToPage(currentPage + 1)">Next</a>
          </li>
        </ul>
      </nav>
    </div>

    <div class="modal fade" id="deleteGroupModal" tabindex="-1" aria-labelledby="deleteGroupModalLabel" aria-hidden="true">
      <div class="modal-dialog">
        <div class="modal-content">
          <div class="modal-header">
            <h5 class="modal-title" id="deleteGroupModalLabel">Confirm Delete</h5>
            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
          </div>
          <div class="modal-body">
            Are you sure you want to delete this group? This action cannot be undone.
          </div>
          <div class="modal-footer">
            <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cancel</button>
            <button type="button" class="btn btn-danger" @click="executeDeleteGroup">Delete</button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.cursor-pointer {
  cursor: pointer;
}
.list-group-item {
  border-radius: 0.5rem; /* Rounded corners for list items */
  border: 1px solid #dee2e6; /* Bootstrap default border color */
  transition: all 0.2s ease-in-out;
}
.list-group-item:hover {
  transform: translateY(-2px); /* Slight lift effect on hover */
  box-shadow: 0 4px 8px rgba(0, 0, 0, 0.1); /* Subtle shadow on hover */
}
.btn-primary, .btn-info, .btn-danger {
  font-weight: 600;
}
.btn-primary {
  background-color: #007bff;
  border-color: #007bff;
}
.btn-primary:hover {
  background-color: #0056b3;
  border-color: #0056b3;
}
.btn-info {
  background-color: #17a2b8;
  border-color: #17a2b8;
}
.btn-info:hover {
  background-color: #117a8b;
  border-color: #117a8b;
}
.btn-danger {
  background-color: #dc3545;
  border-color: #dc3545;
}
.btn-danger:hover {
  background-color: #bd2130;
  border-color: #bd2130;
}
.rounded-pill {
  border-radius: 50rem !important; /* Make buttons more pill-shaped */
}
</style>
