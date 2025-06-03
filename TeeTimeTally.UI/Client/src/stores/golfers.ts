import { ref } from 'vue';
import { defineStore } from 'pinia';
import { useHttpClient } from '@/composables/useHttpClient';
import { AppError, type ResponseError } from '@/primitives/error';
import { Result } from '@/primitives/result';
import { useAuthenticationStore } from './authentication';
import { Permissions } from '@/models/auth/permissions';
import type { Golfer, SearchGolfersRequest } from '@/models/golfer';

export const useGolfersStore = defineStore('golfers', () => {
  const golfers = ref<Golfer[]>([]);
  const isLoadingGolfers = ref(false);
  const golfersError = ref<AppError | null>(null);

  const authenticationStore = useAuthenticationStore();

  /**
   * Searches for golfers based on provided criteria.
   * Requires 'read:golfers' permission.
   * @param params Search parameters (search term, email, limit, offset).
   */
  async function searchGolfers(params: SearchGolfersRequest): Promise<Result<Golfer[]>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadGolfers)) {
      golfers.value = [];
      const unauthorizedError = AppError.failure('You are not authorized to search for golfers.');
      golfersError.value = unauthorizedError;
      return Result.failureWithValue(unauthorizedError);
    }

    isLoadingGolfers.value = true;
    golfersError.value = null;
    try {
      const { data } = await useHttpClient().get<Golfer[]>('/api/golfers', { params });
      golfers.value = data; // Update the store's state with search results
      isLoadingGolfers.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingGolfers.value = false;
      const apiError = error as ResponseError;
      golfersError.value = AppError.failure((apiError.response?.data as any)?.detail || apiError.message || 'Failed to search golfers.');
      console.error('Error searching golfers:', golfersError.value);
      return Result.failureWithValue(golfersError.value);
    }
  }

  return {
    golfers,
    isLoadingGolfers,
    golfersError,
    searchGolfers,
  };
});
