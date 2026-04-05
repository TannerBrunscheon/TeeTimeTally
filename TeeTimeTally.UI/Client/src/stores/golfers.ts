import { ref } from 'vue';
import { defineStore } from 'pinia';
import * as golfersApi from '@/services/golfersApi';
import { mapApiErrorToAppError } from '@/services/apiError';
import { AppError, ErrorType, type ResponseError } from '@/primitives/error'; // Added ErrorType
import { Result } from '@/primitives/result';
import { useAuthenticationStore } from './authentication';
import { Permissions } from '@/models/auth/permissions';
import type { Golfer, SearchGolfersRequest, CreateGolferRequest, CreateGolferResponse } from '@/models/golfer';

export const useGolfersStore = defineStore('golfers', () => {
  const golfers = ref<Golfer[]>([]); // For search results
  const isLoadingGolfers = ref(false); // For search
  const isLoadingCreateGolfer = ref(false); // For creation
  const golfersError = ref<AppError | null>(null); // General error for the store, can be specified per action

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
      return Result.failureWithValue<Golfer[]>(unauthorizedError); // Ensure generic type matches
    }

    isLoadingGolfers.value = true;
    golfersError.value = null;
    try {
      const data = await golfersApi.searchGolfers(params);
      golfers.value = data; // Update the store's state with search results
      isLoadingGolfers.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingGolfers.value = false;
      const appError = mapApiErrorToAppError(error, 'Failed to search golfers.');
      golfersError.value = appError;
      console.error('Error searching golfers:', golfersError.value);
      return Result.failureWithValue<Golfer[]>(golfersError.value); // Ensure generic type matches
    }
  }

  /**
   * Creates a new golfer.
   * Requires 'create:golfers' permission.
   * @param payload Data for the new golfer.
   */
  async function createGolfer(payload: CreateGolferRequest): Promise<Result<CreateGolferResponse>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.CreateGolfers)) {
      const unauthorizedError = AppError.failure('You are not authorized to create golfers.');
      golfersError.value = unauthorizedError; // Set general store error or a specific create error ref
      return Result.failureWithValue<CreateGolferResponse>(unauthorizedError);
    }

    isLoadingCreateGolfer.value = true;
    golfersError.value = null; // Clear previous errors
    try {
      const data = await golfersApi.createGolfer(payload);
      isLoadingCreateGolfer.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingCreateGolfer.value = false;
      const appError = mapApiErrorToAppError(error, 'Failed to create golfer.');
      golfersError.value = appError;
      console.error('Error creating golfer:', appError);
      return Result.failureWithValue<CreateGolferResponse>(appError);
    }
  }

  return {
    golfers,
    isLoadingGolfers,
    isLoadingCreateGolfer, // Expose new loading state
    golfersError,
    searchGolfers,
    createGolfer, // Expose new action
  };
});
