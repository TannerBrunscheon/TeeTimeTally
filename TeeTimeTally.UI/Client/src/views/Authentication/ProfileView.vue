<template>
  <section id="profile-view" class="py-4">
    <div v-if="authenticationStore.isLoading" class="text-center">
      <div class="spinner-border text-primary" role="status">
        <span class="visually-hidden">Loading profile...</span>
      </div>
    </div>

    <div v-else-if="authenticationStore.isAuthenticated && user" class="container">
      <div class="row justify-content-center">
        <div class="col-md-10 col-lg-8">
          <div class="card shadow-sm mb-4">
            <div class="card-header bg-primary text-white d-flex justify-content-between align-items-center">
              <h2 class="h4 mb-0">Your Profile</h2>
              <img
                v-if="user.profileImage"
                :src="user.profileImage"
                alt="User Profile Picture"
                class="rounded-circle"
                style="width: 40px; height: 40px; object-fit: cover; border: 2px solid white;"
              />
               <div
                  v-else-if="user.fullName"
                  class="bg-light text-primary d-flex align-items-center justify-content-center rounded-circle"
                  style="width: 40px; height: 40px; font-size: 1.2rem; border: 2px solid white;"
                >
                  {{ user.fullName.charAt(0).toUpperCase() }}
                </div>
            </div>
            <div class="card-body">
              <form @submit.prevent="handleProfileUpdate">
                <div class="mb-3 row align-items-center">
                  <label for="fullNameInput" class="col-sm-3 col-form-label"><strong>Full Name:</strong></label>
                  <div class="col-sm-9">
                    <div class="input-group">
                      <input
                        type="text"
                        class="form-control"
                        id="fullNameInput"
                        v-model="editableFullName"
                        :disabled="authenticationStore.isUpdatingProfile"
                      />
                    </div>
                  </div>
                </div>

                <div class="mb-3 row">
                  <label class="col-sm-3 col-form-label"><strong>Email:</strong></label>
                  <div class="col-sm-9">
                    <p class="form-control-plaintext">{{ user.email }}</p>
                  </div>
                </div>

                <div class="mb-3 row">
                  <label class="col-sm-3 col-form-label"><strong>Golfer ID:</strong></label>
                  <div class="col-sm-9">
                    <p class="form-control-plaintext text-muted">{{ user.id }}</p>
                  </div>
                </div>

                <div class="mb-3 row">
                  <label class="col-sm-3 col-form-label"><strong>Joined:</strong></label>
                  <div class="col-sm-9">
                    <p class="form-control-plaintext">{{ formattedCreatedAt }}</p>
                  </div>
                </div>

                <div v-if="user.isSystemAdmin" class="mb-3 row">
                  <label class="col-sm-3 col-form-label"><strong>Status:</strong></label>
                  <div class="col-sm-9">
                    <p class="form-control-plaintext">
                        <span class="badge bg-success">System Admin</span>
                    </p>
                  </div>
                </div>

                <hr class="my-4">

                <div class="d-flex justify-content-end">
                  <button
                    type="submit"
                    class="btn btn-primary"
                    :disabled="!isNameChanged || authenticationStore.isUpdatingProfile"
                  >
                    <span v-if="authenticationStore.isUpdatingProfile" class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
                    {{ authenticationStore.isUpdatingProfile ? 'Saving...' : 'Save Changes' }}
                  </button>
                </div>
                <div v-if="updateStatusMessage" :class="['mt-3', 'alert', updateStatusType === 'success' ? 'alert-success' : 'alert-danger']" role="alert">
                  {{ updateStatusMessage }}
                </div>
              </form>
            </div>
          </div>

          <div class="card shadow-sm">
            <div class="card-header bg-light">
              <h2 class="h4 mb-0">Round History</h2>
            </div>
            <div class="card-body">
              <p class="text-muted">
                Your past round information will be displayed here once the feature is available.
              </p>
              </div>
          </div>

        </div>
      </div>
    </div>

    <div v-else class="container text-center">
      <p class="lead">Please log in to view your profile.</p>
      <button @click="authenticationStore.login()" class="btn btn-primary">Login</button>
    </div>
  </section>
</template>

<script setup lang="ts">
import { ref, computed, watchEffect } from 'vue';
import { useAuthenticationStore } from '@/stores/authentication';

const authenticationStore = useAuthenticationStore();
const user = computed(() => authenticationStore.user);

const editableFullName = ref('');
const updateStatusMessage = ref('');
const updateStatusType = ref<'success' | 'error' | ''>('');

// Watch for changes in the store's user object to initialize/reset editableFullName
watchEffect(() => {
  if (user.value) {
    editableFullName.value = user.value.fullName || '';
  } else {
    editableFullName.value = '';
  }
});

const isNameChanged = computed(() => {
  return user.value ? editableFullName.value.trim() !== user.value.fullName.trim() && editableFullName.value.trim() !== '' : false;
});

const formattedCreatedAt = computed(() => {
  if (user.value?.createdAt) {
    try {
      return new Date(user.value.createdAt).toLocaleDateString(undefined, {
        year: 'numeric', month: 'long', day: 'numeric', hour: '2-digit', minute: '2-digit'
      });
    } catch (e) {
      return user.value.createdAt;
    }
  }
  return 'N/A';
});

async function handleProfileUpdate() {
  if (!isNameChanged.value || !user.value) {
    updateStatusMessage.value = 'No changes to save or user data not loaded.';
    updateStatusType.value = 'error';
    return;
  }

  updateStatusMessage.value = ''; // Clear previous messages
  updateStatusType.value = '';

  const result = await authenticationStore.updateMyProfile(editableFullName.value.trim());

  if (result.isSuccess) {
    updateStatusMessage.value = 'Profile updated successfully!';
    updateStatusType.value = 'success';
    // editableFullName will be updated via watchEffect when store's user.value changes
  } else {
    updateStatusMessage.value = result.error?.message || 'Failed to update profile. Please try again.';
    updateStatusType.value = 'error';
  }
  // Optionally clear message after a few seconds
  setTimeout(() => {
    updateStatusMessage.value = '';
    updateStatusType.value = '';
  }, 5000);
}
</script>

<style scoped>
#profile-view .card {
  border: none;
}
#profile-view .card-header {
  border-bottom: none;
}
.form-control-plaintext {
  padding-top: 0.375rem; /* Align with form-control */
  padding-bottom: 0.375rem;
}
/* Add any additional specific styles here */
</style>
