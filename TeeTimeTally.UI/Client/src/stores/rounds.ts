import { ref } from 'vue';
import { defineStore } from 'pinia';
import type { OpenRound } from '@/models/round'; // We'll define this interface next
import { Result, type DefaultResult } from '@/primitives/result';
import { useHttpClient } from '@/composables/useHttpClient';
import { AppError, type ResponseError } from '@/primitives/error';
import { useAuthenticationStore } from './authentication';
import { Permissions } from '@/models/auth/permissions'; // For checking permissions

export const useRoundsStore = defineStore('rounds', () => {
  const openRounds = ref<OpenRound[]>([]);
  const isLoadingOpenRounds = ref(false);
  const openRoundsError = ref<AppError | null>(null);

  const authenticationStore = useAuthenticationStore();

  async function fetchOpenRounds(): Promise<DefaultResult> {
    // Ensure user is authenticated and has permission to read group rounds
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadGroupRounds)) {
      openRounds.value = []; // Clear any existing rounds
      const unauthorizedError = AppError.failure("You are not authorized to view open rounds.");
      openRoundsError.value = unauthorizedError;
      // console.warn(unauthorizedError.message); // Optional: log to console
      return Result.failure(unauthorizedError);
    }

    isLoadingOpenRounds.value = true;
    openRoundsError.value = null;
    try {
      // The API endpoint is GET /api/rounds/open
      const { data } = await useHttpClient().get<OpenRound[]>('/api/rounds/open');
      openRounds.value = data;
      isLoadingOpenRounds.value = false;
      return Result.success();
    } catch (error: any) {
      isLoadingOpenRounds.value = false;
      const apiError = error as ResponseError;
      // Check for specific error statuses if needed, e.g., 403 if API re-checks permissions
      if (apiError.response && apiError.response.status === 403) {
        openRoundsError.value = AppError.failure("You are not authorized to fetch open rounds (API).");
      } else {
        openRoundsError.value = AppError.failure(apiError.message || 'An unknown error occurred while fetching open rounds.');
      }
      openRounds.value = []; // Clear rounds on error
      console.error("Error fetching open rounds:", openRoundsError.value);
      return Result.failure(openRoundsError.value);
    }
  }

  // Call this when the store is initialized or when the user logs in, if appropriate
  // Or call it from the component that needs the data (e.g., HomeView onMounted)
  // if (authenticationStore.isAuthenticated && authenticationStore.hasPermission(Permissions.ReadGroupRounds)) {
  //   fetchOpenRounds();
  // }

  return {
    openRounds,
    isLoadingOpenRounds,
    openRoundsError,
    fetchOpenRounds
  };
});
