import { ref, computed } from 'vue';
import { defineStore } from 'pinia';
import type { MyGolferProfile } from '@/models/auth/user';
import { Result, type DefaultResult } from '@/primitives/result';
import { useHttpClient } from '@/composables/useHttpClient';
import { AppError, type ResponseError } from '@/primitives/error';
import { mapApiErrorToAppError } from '@/services/apiError';

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
      const appError = mapApiErrorToAppError(error, 'Failed to fetch user profile.');
      // For auth endpoints, 401/403/404 are expected to mean 'no authenticated user' — treat as success with empty profile
      const resp = (error as ResponseError)?.response;
      if (resp && [401, 403, 404].includes(resp.status)) {
        console.warn('User info fetch returned unauthenticated status:', resp.status);
        return Result.success();
      }
      return Result.failure(appError);
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
      const appError = mapApiErrorToAppError(error, 'Failed to update profile.');
      return Result.failure(appError);
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
