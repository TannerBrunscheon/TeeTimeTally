// src/stores/courses.ts
import { ref } from 'vue'; // Import ref
import { useHttpClient } from '@/composables/useHttpClient';
import type { Course, SearchCoursesRequest } from '@/models/course'; // Import SearchCoursesRequest
import { AppError, type ResponseError } from '@/primitives/error';
import { Result } from '@/primitives/result';
import { defineStore } from 'pinia';
import { useAuthenticationStore } from './authentication'; // Assuming this store exists
import { Permissions } from '@/models/auth/permissions'; // Assuming this exists

export const useCourseStore = defineStore('course', () => {
  const isLoadingCourses = ref(false);
  const coursesError = ref<AppError | null>(null);
  const authenticationStore = useAuthenticationStore();

  /**
   * Fetches a single course by its ID.
   * @param courseId The ID of the course to fetch.
   */
  async function getCourseById(courseId: string): Promise<Result<Course>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadCourses)) {
      const unauthorizedError = AppError.failure('You are not authorized to view course details.');
      coursesError.value = unauthorizedError;
      return Result.failureWithValue(unauthorizedError);
    }

    isLoadingCourses.value = true;
    coursesError.value = null;
    try {
      const { data } = await useHttpClient().get<Course>(`/api/courses/${courseId}`);
      isLoadingCourses.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingCourses.value = false;
      const apiError = error as ResponseError;
      const errorMessage = (apiError.response?.data as any)?.detail || apiError.message || 'An unknown error occurred while fetching course details.';
      coursesError.value = AppError.failure(errorMessage);
      console.error('Error fetching course by ID:', coursesError.value);
      return Result.failureWithValue(coursesError.value);
    }
  }

  /**
   * Searches for courses based on criteria, with pagination.
   * @param params Search parameters (search term, limit, offset).6
   */
  async function searchCourses(params: SearchCoursesRequest): Promise<Result<Course[]>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadCourses)) {
      const unauthorizedError = AppError.failure('You are not authorized to search for courses.');
      coursesError.value = unauthorizedError;
      return Result.failureWithValue(unauthorizedError);
    }

    isLoadingCourses.value = true;
    coursesError.value = null;
    try {
      const { data } = await useHttpClient().get<Course[]>('/api/courses', { params });
      isLoadingCourses.value = false;
      return Result.successWithValue<Course[]>(data);
    } catch (error: any) {
      isLoadingCourses.value = false;
      const apiError = error as ResponseError;
      const errorMessage = (apiError.response?.data as any)?.detail || apiError.message || 'An unknown error occurred while searching courses.';
      coursesError.value = AppError.failure(errorMessage);
      console.error('Error searching courses:', coursesError.value);
      return Result.failureWithValue<Course[]>(AppError.failure(errorMessage));
    }
  }

  return {
    isLoadingCourses,
    coursesError,
    getCourseById, // Expose new function
    searchCourses // Expose updated function
  };
});
