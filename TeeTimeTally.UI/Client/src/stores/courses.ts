import { ref } from 'vue'; // Import ref
import { useHttpClient } from '@/composables/useHttpClient';
// Assuming CourseSummary is also in '@/models/course'
import type { Course, SearchCoursesRequest, CourseSummary } from '@/models/course';
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
      // Adjust limit as appropriate for your application's needs.
      const searchParams: SearchCoursesRequest = { limit: 100, offset: 0 }; // Example: fetch up to 500 courses
      const searchResult = await searchCourses(searchParams);

      if (searchResult.isSuccess && searchResult.value) {
        // Map full Course objects to CourseSummary objects
        // Ensure that your Course type includes city and state if CourseSummary does.
        courses.value = searchResult.value.map(course => ({
          id: course.id,
          name: course.name,
          // city: course.city,    // Removed as 'city' is not in the current Course model in TeeTimeTally_Models_CourseTS
          // state: course.state,  // Removed as 'state' is not in the current Course model in TeeTimeTally_Models_CourseTS
          // Add any other fields required by CourseSummary that are present in Course
        }));
        isLoadingCourses.value = false; // Set loading to false after successful fetch and mapping
        return Result.successWithValue(courses.value);
      } else {
        // If searchCourses handled its own error and set coursesError, it will be reflected.
        // If searchResult is a failure, propagate its error.
        isLoadingCourses.value = false; // Ensure loading is false on failure path too
        if (searchResult.error) {
          coursesError.value = searchResult.error; // Use the error from searchCourses
          return Result.failureWithValue(searchResult.error);
        }
        // Fallback for unexpected non-success without specific error
        const fallbackError = AppError.failure('Failed to retrieve and process course list.');
        coursesError.value = fallbackError;
        return Result.failureWithValue(fallbackError);
      }
    } catch (error: any) {
      // Catch errors from this function's own logic (e.g., mapping if searchCourses was mocked differently)
      isLoadingCourses.value = false;
      // Corrected: Replaced AppError.unknown with AppError.failure
      const generalError = AppError.failure('An unexpected error occurred while fetching all courses.');
      coursesError.value = generalError;
      console.error('Error in fetchAllCourses:', generalError);
      return Result.failureWithValue(generalError);
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
   * This function returns full Course objects.
   * @param params Search parameters (search term, limit, offset).
   */
  async function searchCourses(params: SearchCoursesRequest): Promise<Result<Course[]>> {
    if (!authenticationStore.isAuthenticated || !authenticationStore.hasPermission(Permissions.ReadCourses)) {
      const unauthorizedError = AppError.failure('You are not authorized to search for courses.');
      coursesError.value = unauthorizedError;
      return Result.failureWithValue(unauthorizedError);
    }

    isLoadingCourses.value = true; // This function sets the global loading state
    coursesError.value = null;
    try {
      const { data } = await useHttpClient().get<Course[]>('/api/courses', { params });
      isLoadingCourses.value = false; // Clear loading state on success
      return Result.successWithValue<Course[]>(data);
    } catch (error: any) {
      isLoadingCourses.value = false; // Clear loading state on error
      const apiError = error as ResponseError;
      const errorMessage = (apiError.response?.data as any)?.detail || apiError.message || 'An unknown error occurred while searching courses.';
      coursesError.value = AppError.failure(errorMessage);
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
      const { data } = await useHttpClient().post<Course>('/api/courses', payload);
      isLoadingCreateCourse.value = false;
      await fetchAllCourses(); // Refresh list after creation
      return Result.successWithValue(data);
    } catch (error: any) {
      isLoadingCreateCourse.value = false;
      const apiError = error as ResponseError;
      let appError: AppError;

      if (apiError.response?.status === 409) {
        const detail = (apiError.response.data as any)?.detail || 'A course with this name already exists.';
        appError = AppError.conflict(detail);
      } else if (apiError.response?.status === 400 && (apiError.response.data as any)?.errors) {
        const validationErrors = (apiError.response.data as any).errors;
        const firstErrorKey = Object.keys(validationErrors)[0];
        const firstErrorMessage = validationErrors[firstErrorKey]?.[0] || 'Please check your input.';
        appError = AppError.validation(firstErrorMessage);
      } else {
        const detail = (apiError.response?.data as any)?.detail || apiError.message || 'Failed to create course.';
        appError = AppError.failure(detail);
      }

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
