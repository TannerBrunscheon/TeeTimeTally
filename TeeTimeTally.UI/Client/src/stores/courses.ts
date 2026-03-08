import { ref } from 'vue'; // Import ref
import * as coursesApi from '@/services/coursesApi';
import { mapApiErrorToAppError } from '@/services/apiError';
// Assuming CourseSummary is also in '@/models/course'
import type { Course, SearchCoursesRequest, CourseSummary, CreateCourseRequest } from '@/models/course';
import { AppError, type ResponseError } from '@/primitives/error';
import { Result } from '@/primitives/result';
import { defineStore } from 'pinia';
import { useAuthenticationStore } from './authentication'; // Assuming this store exists
import { Permissions } from '@/models/auth/permissions'; // Assuming this exists

export const useCoursesStore = defineStore('course', () => {
  const courses = ref<CourseSummary[]>([]); // Added: to store the list for the overview
  const isLoadingCourses = ref(false);
  const isLoadingCreateCourse = ref(false); // State for the creation process
  const coursesError = ref<AppError | null>(null); // Kept name as coursesError to match view
  const authenticationStore = useAuthenticationStore();

  /**
   * Fetches all courses (as summaries) for the overview.
   * This typically involves fetching a larger list without specific search terms.
   */
  async function fetchAllCourses(): Promise<Result<CourseSummary[]>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadCourses)) {
      courses.value = []; // Clear existing courses
      const unauthorizedError = AppError.failure('You are not authorized to view courses.');
      coursesError.value = unauthorizedError;
      return Result.failureWithValue(unauthorizedError);
    }

    isLoadingCourses.value = true;
    coursesError.value = null;
    try {
      // Use searchCourses with a high limit to fetch "all" courses.
      const searchParams: SearchCoursesRequest = { limit: 100, offset: 0 };
      const data = await coursesApi.searchCourses(searchParams);
      courses.value = data.map(course => ({ id: course.id, name: course.name }));
      isLoadingCourses.value = false;
      return Result.successWithValue(courses.value);
    } catch (error: any) {
      isLoadingCourses.value = false;
      const appError = mapApiErrorToAppError(error, 'Failed to retrieve and process course list.');
      coursesError.value = appError;
      return Result.failureWithValue(appError);
    }
  }

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
      const data = await coursesApi.getCourseById(courseId);
      isLoadingCourses.value = false;
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingCourses.value = false;
      const appError = mapApiErrorToAppError(error, 'An unknown error occurred while fetching course details.');
      coursesError.value = appError;
      console.error('Error fetching course by ID:', coursesError.value);
      return Result.failureWithValue(coursesError.value);
    }
  }

  /**
   * Searches for courses based on criteria, with pagination.
   * This function returns full Course objects.
   * @param params Search parameters (search term, limit, offset).
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
      const data = await coursesApi.searchCourses(params);
      isLoadingCourses.value = false;
      return Result.successWithValue<Course[]>(data);
    } catch (error: any) {
      isLoadingCourses.value = false;
      const appError = mapApiErrorToAppError(error, 'An unknown error occurred while searching courses.');
      coursesError.value = appError;
      console.error('Error searching courses:', coursesError.value);
      return Result.failureWithValue<Course[]>(coursesError.value);
    }
  }

  /**
  * Creates a new course.
  * @param payload The data for the new course.
  */
  async function createCourse(payload: CreateCourseRequest): Promise<Result<Course>> {
    if (!authenticationStore.hasPermission(Permissions.CreateCourses)) {
      const unauthorizedError = AppError.failure('You are not authorized to create courses.');
      coursesError.value = unauthorizedError;
      return Result.failureWithValue(unauthorizedError);
    }

    isLoadingCreateCourse.value = true;
    coursesError.value = null;
    try {
      const data = await coursesApi.createCourse(payload);
      isLoadingCreateCourse.value = false;
      await fetchAllCourses(); // Refresh list after creation
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingCreateCourse.value = false;
      const appError = mapApiErrorToAppError(error, 'Failed to create course.');
      coursesError.value = appError;
      return Result.failureWithValue(appError);
    }
  }

  return {
    courses,
    isLoadingCourses,
    coursesError,
    isLoadingCreateCourse, // Expose loading state
    fetchAllCourses,
    getCourseById,
    searchCourses,
    createCourse, // Expose new action
  };
});
