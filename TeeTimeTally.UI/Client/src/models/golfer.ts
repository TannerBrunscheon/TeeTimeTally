// Represents a golfer profile as returned by the API (e.g., from SearchGolfersEndpoint)
export interface Golfer {
  id: string;
  auth0UserId: string | null;
  fullName: string;
  email: string;
  isSystemAdmin: boolean;
  createdAt: string; // ISO date string
  updatedAt: string; // ISO date string
  isDeleted: boolean;
  deletedAt: string | null;
}

// Represents the request DTO for searching golfers
export interface SearchGolfersRequest {
  search?: string;
  email?: string;
  limit?: number;
  offset?: number;
}
