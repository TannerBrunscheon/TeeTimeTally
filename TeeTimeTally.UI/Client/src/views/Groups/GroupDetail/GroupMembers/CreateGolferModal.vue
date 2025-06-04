<script setup lang="ts">
import { ref, defineEmits, defineExpose } from 'vue';
import { useGolfersStore } from '@/stores/golfers'; // Assuming you'll add a createGolfer method here
import type { Golfer } from '@/models'; // Or a specific CreateGolferResponse DTO

const emit = defineEmits<{
  (e: 'golferCreated', golfer: Golfer): void;
  (e: 'close'): void;
}>();

const golfersStore = useGolfersStore();

const showModal = ref(false);
const newGolferFullName = ref('');
const newGolferEmail = ref('');
const createError = ref<string | null>(null);
const isCreating = ref(false);

async function handleCreateGolfer() {
  if (!newGolferFullName.value.trim() || !newGolferEmail.value.trim()) {
    createError.value = 'Full name and email are required.';
    return;
  }
  // Basic email validation (can be enhanced)
  if (!/^\S+@\S+\.\S+$/.test(newGolferEmail.value)) {
    createError.value = 'Please enter a valid email address.';
    return;
  }

  isCreating.value = true;
  createError.value = null;

  try {
    // Assuming createGolferInStore exists and returns a Result<Golfer>
    // You'll need to add this method to your golfersStore
    const result = await golfersStore.createGolfer({
      fullName: newGolferFullName.value,
      email: newGolferEmail.value,
      // Auth0UserId will likely be null initially or handled by backend
    });

    if (result.isSuccess && result.value) {
      emit('golferCreated', result.value);
      closeModal(); // Close this modal
    } else {
      createError.value = result.error?.message || 'Failed to create golfer.';
    }
  } catch (e: any) {
    createError.value = e.message || 'An unexpected error occurred.';
  } finally {
    isCreating.value = false;
  }
}

function openModal() {
  newGolferFullName.value = '';
  newGolferEmail.value = '';
  createError.value = null;
  showModal.value = true;
  // Bootstrap modal instance handling
  const modalElement = document.getElementById('createGolferForGroupModal');
  if (modalElement) {
    const modal = new (window as any).bootstrap.Modal(modalElement);
    modal.show();
  }
}

function closeModal() {
  showModal.value = false;
  emit('close');
   const modalElement = document.getElementById('createGolferForGroupModal');
  if (modalElement) {
    const modal = (window as any).bootstrap.Modal.getInstance(modalElement);
    if (modal) {
      modal.hide();
    }
  }
}

defineExpose({
  openModal,
  closeModal
});
</script>

<template>
  <div class="modal fade" id="createGolferForGroupModal" tabindex="-1" aria-labelledby="createGolferModalLabel" aria-hidden="true" data-bs-backdrop="static" data-bs-keyboard="false">
    <div class="modal-dialog modal-dialog-centered">
      <div class="modal-content">
        <div class="modal-header bg-info text-white">
          <h5 class="modal-title" id="createGolferModalLabel">Create New Golfer</h5>
          <button type="button" class="btn-close btn-close-white" @click="closeModal" aria-label="Close"></button>
        </div>
        <div class="modal-body">
          <form @submit.prevent="handleCreateGolfer">
            <div class="mb-3">
              <label for="newGolferFullName" class="form-label">Full Name <span class="text-danger">*</span></label>
              <input type="text" class="form-control" id="newGolferFullName" v-model="newGolferFullName" required>
            </div>
            <div class="mb-3">
              <label for="newGolferEmail" class="form-label">Email <span class="text-danger">*</span></label>
              <input type="email" class="form-control" id="newGolferEmail" v-model="newGolferEmail" required>
            </div>

            <div v-if="createError" class="alert alert-danger mt-3">
              {{ createError }}
            </div>
          </form>
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary rounded-pill" @click="closeModal">Cancel</button>
          <button type="button" class="btn btn-info rounded-pill" @click="handleCreateGolfer" :disabled="isCreating">
            <span v-if="isCreating" class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
            {{ isCreating ? 'Creating...' : 'Create & Add Golfer' }}
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
.text-danger {
  color: #dc3545 !important; /* Bootstrap danger color for asterisk */
}
</style>
