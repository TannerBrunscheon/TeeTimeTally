// Represents a Course as returned by GetCourseByIdEndpoint or SearchCoursesEndpoint
export interface Course {
  id: string;
  name: string;
  cthHoleNumber: number;
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
