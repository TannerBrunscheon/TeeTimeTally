// Represents a golfer profile as returned by the API (e.g., from SearchGolfersEndpoint or CreateGolferEndpoint)
export interface Golfer {
  id: string; // Matches Guid Id
  auth0UserId: string | null; // Matches string? Auth0UserId
  fullName: string; // Matches string FullName
  email: string; // Matches string Email
  isSystemAdmin: boolean; // Matches bool IsSystemAdmin
  createdAt: string; // Matches DateTime CreatedAt (ISO date string)
  updatedAt: string; // Matches DateTime UpdatedAt (ISO date string)
  isDeleted: boolean; // Matches bool IsDeleted
  deletedAt: string | null; // Matches DateTime? DeletedAt (ISO date string or null)
}

// Represents the request DTO for searching golfers
export interface SearchGolfersRequest {
  search?: string;
  email?: string;
  limit?: number;
  offset?: number;
}

// Represents the request DTO for creating a new golfer
// Corresponds to C# CreateGolferRequest
export interface CreateGolferRequest {
  fullName: string;
  email: string;
  auth0UserId?: string | null; // Optional and nullable
}

// The response for creating a golfer is essentially the Golfer profile itself.
// So, we can use the existing Golfer interface as the expected response type.
// If the C# CreateGolferResponse had different fields, we would define a separate interface.
// For now, CreateGolferResponse (TS) will be an alias for Golfer.
export type CreateGolferResponse = Golfer;
