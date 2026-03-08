import { ref } from 'vue';
import { defineStore } from 'pinia';
import * as reportsApi from '@/services/reportsApi';
import { mapApiErrorToAppError } from '@/services/apiError';
import { Result } from '@/primitives/result';
import { AppError } from '@/primitives/error';
import { useAuthenticationStore } from './authentication';
import type { GroupYearEndReportDto } from '@/types/reports';

export const useReportsStore = defineStore('reports', () => {
  const isLoading = ref(false);
  const error = ref<AppError | null>(null);

  const authenticationStore = useAuthenticationStore();

  async function fetchGroupYearEndReport(groupId: string, year: number): Promise<Result<GroupYearEndReportDto>> {
    if (!authenticationStore.isAuthenticated) return Result.failureWithValue<GroupYearEndReportDto>(AppError.failure('Not authenticated'));
    isLoading.value = true;
    error.value = null;
    try {
      const data = await reportsApi.fetchGroupYearEndReport(groupId, year);
      isLoading.value = false;
      return Result.successWithValue<GroupYearEndReportDto>(data);
    } catch (e: any) {
      isLoading.value = false;
      const appError = mapApiErrorToAppError(e, 'Failed to fetch report');
      error.value = appError;
      return Result.failureWithValue<GroupYearEndReportDto>(error.value as AppError);
    }
  }

  return { isLoading, error, fetchGroupYearEndReport };
});
