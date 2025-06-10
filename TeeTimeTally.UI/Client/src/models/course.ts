// Represents a summary of a course, typically for lists/dropdowns
// Used by CourseOverviewView and populated by useCoursesStore.fetchAllCourses
export interface CourseSummary {
  id: string;
  name: string;
  // city?: string; // Optional: Add if your Course model and API response can include city
  // state?: string; // Optional: Add if your Course model and API response can include state
}

// Represents a Course as returned by GetCourseByIdEndpoint or SearchCoursesEndpoint
export interface Course {
  id: string;
  name: string;
  // city?: string; // Add if your backend provides this
  // state?: string; // Add if your backend provides this
  // par?: number; // Example: Add if your backend provides this
  // rating?: number; // Example: Add if your backend provides this
  // slope?: number; // Example: Add if your backend provides this
  cthHoleNumber: number; // As per your provided model
  createdAt: string; // ISO date string
  updatedAt: string; // ISO date string
  isDeleted?: boolean; // Optional, as it might not always be returned or relevant for active courses
  deletedAt?: string | null; // Optional
}

// Represents the request DTO for searching courses
export interface SearchCoursesRequest {
  search?: string;
  limit?: number;
  offset?: number;
}

// Optional: Placeholder for CreateCourseRequest DTO
// Define this based on what your API endpoint for creating a course expects
export interface CreateCourseRequest {
  name: string;
  // city?: string;
  // state?: string;
  // par?: number;
  // rating?: number;
  // slope?: number;
  cthHoleNumber: number;
}

// Optional: Placeholder for UpdateCourseRequest DTO
// Define this based on what your API endpoint for updating a course expects
export interface UpdateCourseRequest {
  name?: string;
  // city?: string;
  // state?: string;
  // par?: number;
  // rating?: number;
  // slope?: number;
  cthHoleNumber?: number;
}

export interface CreateCourseRequest {
  name: string;
  cthHoleNumber: number;
}

// Matches the C# record: CreateCourseResponse
export interface CreateCourseResponse {
  id: string;
  name: string;
  cthHoleNumber: number;
  createdAt: string; // Representing DateTime as string
  updatedAt: string; // Representing DateTime as string
}
