import { ref, computed } from 'vue';
import { defineStore } from 'pinia';
import type { MyGolferProfile } from '@/models/auth/user';
import { Result, type DefaultResult } from '@/primitives/result';
import { useHttpClient } from '@/composables/useHttpClient';
import { AppError, type ResponseError } from '@/primitives/error';

const nullGolferProfile: MyGolferProfile = {
  id: '',
  auth0UserId: '',
  fullName: '',
  email: '',
  isSystemAdmin: false,
  createdAt: '',
  updatedAt: '',
  profileImage: undefined,
  roles: [],
  permissions: []
};

export const useAuthenticationStore = defineStore('authentication', () => {
  const user = ref<MyGolferProfile | undefined>(undefined);
  const isLoading = ref(true);
  const isUpdatingProfile = ref(false); // For loading state during profile update

  const isAuthenticated = computed(() => {
    return !isLoading.value && user.value !== undefined && user.value.id !== '' && user.value.auth0UserId !== '';
  });

  function hasPermission(permission: string): boolean {
    if (isAuthenticated.value && user.value?.permissions) {
      return user.value.permissions.includes(permission);
    }
    return false;
  }

  function register(): void {
    window.location.href = '/api/authentication/register';
  }

  function login(): void {
    window.location.href = '/api/authentication/login';
  }

  function logout(): void {
    user.value = undefined;
    isLoading.value = true;
    window.location.href = '/api/authentication/logout';
  }

  async function getUserInfo(): Promise<DefaultResult> {
    isLoading.value = true;
    try {
      const { data } = await useHttpClient().get<MyGolferProfile>('/api/authentication/user-info', {
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
      });
      user.value = data;
      isLoading.value = false;
      return Result.success();
    } catch (error: any) {
      user.value = { ...nullGolferProfile };
      isLoading.value = false;
      const apiError = error as ResponseError;
      if (error.response && (error.response.status === 401 || error.response.status === 403 || error.response.status === 404)) {
        console.warn('User info fetch failed with status:', error.response.status);
        return Result.success();
      }
      return Result.failure(AppError.failure(apiError.message || 'Failed to fetch user profile.'));
    }
  }

  async function updateMyProfile(newFullName: string): Promise<DefaultResult> {
    if (!user.value) {
      return Result.failure(AppError.failure('User not loaded. Cannot update profile.'));
    }
    isUpdatingProfile.value = true;
    try {
      const response = await useHttpClient().put<MyGolferProfile>('/api/golfers/me', { fullName: newFullName });
      user.value = response.data; // Update store with the response from API
      isUpdatingProfile.value = false;
      return Result.success();
    } catch (error: any) {
      isUpdatingProfile.value = false;
      const apiError = error as ResponseError;
      // Construct a more specific error message if possible
      let detail = 'Failed to update profile.';
      if (apiError.response?.data && typeof apiError.response.data === 'object') {
        // Assuming error response might have a 'detail' or 'title' property
        detail = (apiError.response.data as any).detail || (apiError.response.data as any).title || detail;
      } else if (apiError.message) {
        detail = apiError.message;
      }
      return Result.failure(AppError.failure(detail));
    }
  }

  return {
    user,
    isAuthenticated,
    isLoading,
    isUpdatingProfile, // Expose for UI
    register,
    login,
    logout,
    getUserInfo,
    updateMyProfile, // Expose new action
    hasPermission
  };
});
