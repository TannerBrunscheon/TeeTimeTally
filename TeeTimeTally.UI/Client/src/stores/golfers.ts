import { ref } from 'vue';
import { defineStore } from 'pinia';
import { useHttpClient } from '@/composables/useHttpClient';
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
  const httpClient = useHttpClient(); // Get instance once

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
      const { data } = await httpClient.get<Golfer[]>('/api/golfers', { params });
      golfers.value = data; // Update the store's state with search results
      isLoadingGolfers.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingGolfers.value = false;
      const apiError = error as ResponseError;
      golfersError.value = AppError.failure((apiError.response?.data as any)?.detail || apiError.message || 'Failed to search golfers.');
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
      const { data } = await httpClient.post<CreateGolferResponse>('/api/golfers', payload);
      isLoadingCreateGolfer.value = false;
      // Optionally, add the new golfer to the local 'golfers' list if appropriate for your UI
      // For example, if search results should immediately reflect the new golfer:
      // golfers.value.unshift(data); // Add to the beginning
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingCreateGolfer.value = false;
      const apiError = error as ResponseError;
      let errorMessage = (apiError.response?.data as any)?.detail || apiError.message || 'Failed to create golfer.';
      let errorType = ErrorType.Failure;

      if (apiError.response?.status === 409) { // Conflict
        errorMessage = (apiError.response?.data as any)?.detail || (apiError.response?.data as any)?.title || 'A golfer with this email or Auth0 ID already exists.';
        errorType = ErrorType.Conflict;
        golfersError.value = AppError.conflict(errorMessage);
      } else if (apiError.response?.status === 400 && (apiError.response?.data as any)?.errors) { // Validation error
        const validationErrors = (apiError.response.data as any).errors;
        // FastEndpoints often returns errors in a dictionary format.
        // Taking the first error message for simplicity.
        const firstErrorKey = Object.keys(validationErrors)[0];
        if (validationErrors[firstErrorKey] && validationErrors[firstErrorKey].length > 0) {
          errorMessage = validationErrors[firstErrorKey][0];
        } else {
          errorMessage = "Validation failed. Please check your input.";
        }
        errorType = ErrorType.Validation;
        golfersError.value = AppError.validation(errorMessage);
      } else {
        golfersError.value = AppError.failure(errorMessage);
      }

      console.error('Error creating golfer:', errorMessage, apiError.response);
      // Return a Result consistent with the error type
      if (errorType === ErrorType.Conflict) return Result.failureWithValue<CreateGolferResponse>(AppError.conflict(errorMessage));
      if (errorType === ErrorType.Validation) return Result.failureWithValue<CreateGolferResponse>(AppError.validation(errorMessage));
      return Result.failureWithValue<CreateGolferResponse>(AppError.failure(errorMessage));
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
