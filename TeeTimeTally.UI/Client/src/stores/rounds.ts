import { ref } from 'vue';
import { defineStore } from 'pinia';
import { useHttpClient } from '@/composables/useHttpClient';
import { AppError, ErrorType, type ResponseError } from '@/primitives/error';
import { Result, type DefaultResult } from '@/primitives/result';
import { useAuthenticationStore } from './authentication';
import { Permissions } from '@/models/auth/permissions';
import type {
  OpenRound,
  StartRoundRequest,
  StartRoundResponse,
  GetRoundByIdResponse,
  SubmitScoresRequest,
  SubmitScoresResponse,
  CompleteRoundRequest,
  CompleteRoundResponse
} from '@/models/round';

export const useRoundsStore = defineStore('rounds', () => {
  // --- STATE ---
  const openRounds = ref<OpenRound[]>([]);
  const currentRound = ref<GetRoundByIdResponse | null>(null);
  const isLoadingOpenRounds = ref(false);
  const isLoadingCurrentRound = ref(false);
  const openRoundsError = ref<AppError | null>(null);

  const isLoadingStartRound = ref(false);
  const isLoadingSubmitScores = ref(false);
  const isLoadingCompleteRound = ref(false);

  const authenticationStore = useAuthenticationStore();
  const httpClient = useHttpClient();

  // --- ACTIONS ---

  async function fetchOpenRounds(): Promise<DefaultResult> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadGroupRounds)) {
      openRounds.value = [];
      const unauthorizedError = AppError.failure('You are not authorized to view open rounds.');
      openRoundsError.value = unauthorizedError;
      return Result.failure(unauthorizedError);
    }

    isLoadingOpenRounds.value = true;
    openRoundsError.value = null;
    try {
      const { data } = await httpClient.get<OpenRound[]>('/api/rounds/open');
      openRounds.value = data;
      isLoadingOpenRounds.value = false;
      return Result.success();
    } catch (error: any) {
      isLoadingOpenRounds.value = false;
      const apiError = error as ResponseError;
      openRoundsError.value = AppError.failure(
        (apiError.response?.data as any)?.detail ||
        apiError.message ||
        'An unknown error occurred while fetching open rounds.'
      );
      openRounds.value = [];
      console.error('Error fetching open rounds:', openRoundsError.value);
      return Result.failure(openRoundsError.value);
    }
  }

  async function startRound(
    groupId: string,
    request: StartRoundRequest
  ): Promise<Result<StartRoundResponse>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.CreateRounds)) {
      return Result.failureWithValue(AppError.failure('You are not authorized to create rounds.'));
    }

    isLoadingStartRound.value = true;
    try {
      const { data } = await httpClient.post<StartRoundResponse>(
        `/api/groups/${groupId}/rounds`,
        request
      );
      isLoadingStartRound.value = false;
      await fetchOpenRounds();
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingStartRound.value = false;
      const apiError = error as ResponseError;
      const errorMessage =
        (apiError.response?.data as any)?.detail ||
        (apiError.response?.data as any)?.title ||
        apiError.message ||
        'Failed to start round.';

      let appError: AppError;
      if (apiError.response?.status === 400 && (apiError.response?.data as any)?.errors) {
        const validationErrors = (apiError.response.data as any).errors;
        const firstErrorKey = Object.keys(validationErrors)[0];
        const firstErrorMessage = validationErrors[firstErrorKey]?.[0] || 'Please check your input.';
        appError = AppError.validation(firstErrorMessage)
      } else {
        appError = AppError.failure(errorMessage)
      }

      console.error('Error starting round:', appError);
      return Result.failureWithValue(appError);
    }
  }

  async function fetchRoundById(roundId: string): Promise<Result<GetRoundByIdResponse>> {
    isLoadingCurrentRound.value = true;
    currentRound.value = null;
    try {
      const { data } = await httpClient.get<GetRoundByIdResponse>(`/api/rounds/${roundId}`);
      currentRound.value = data;
      isLoadingCurrentRound.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingCurrentRound.value = false;
      const apiError = error as ResponseError;
      const appError = AppError.failure((apiError.response?.data as any)?.detail || 'Failed to fetch round details.');
      console.error('Error fetching round by ID:', appError);
      return Result.failureWithValue(appError);
    }
  }

  async function submitScores(roundId: string, scores: SubmitScoresRequest['scoresToSubmit']): Promise<Result<SubmitScoresResponse>> {
    isLoadingSubmitScores.value = true;
    try {
      const { data } = await httpClient.post<SubmitScoresResponse>(`/api/rounds/${roundId}/scores`, { scoresToSubmit: scores });
      isLoadingSubmitScores.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingSubmitScores.value = false;
      const apiError = error as ResponseError;
      const appError = AppError.failure((apiError.response?.data as any)?.detail || 'Failed to submit scores.');
      console.error('Error submitting scores:', appError);
      return Result.failureWithValue(appError);
    }
  }

  async function completeRound(roundId: string, request: Omit<CompleteRoundRequest, 'roundId'>): Promise<Result<CompleteRoundResponse>> {
    isLoadingCompleteRound.value = true;
    try {
      const { data } = await httpClient.post<CompleteRoundResponse>(`/api/rounds/${roundId}/complete`, request);
      isLoadingCompleteRound.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingCompleteRound.value = false;
      const apiError = error as ResponseError;

      // Handle the specific 409 Conflict for ties
      if (apiError.response?.status === 409) {
        const problemDetails = apiError.response.data as { detail?: string, [key: string]: any };
        const appError = AppError.conflict(
          problemDetails.detail || "A tie was detected.",
          problemDetails
        );
        return Result.failureWithValue(appError);
      }

      const appError = AppError.failure((apiError.response?.data as any)?.detail || 'Failed to complete round.');
      console.error('Error completing round:', appError);
      return Result.failureWithValue(appError);
    }
  }


  return {
    // State
    openRounds,
    currentRound,
    isLoadingOpenRounds,
    isLoadingCurrentRound,
    openRoundsError,
    isLoadingStartRound,
    isLoadingSubmitScores,
    isLoadingCompleteRound,
    // Actions
    fetchOpenRounds,
    startRound,
    fetchRoundById,
    submitScores,
    completeRound
  };
});
