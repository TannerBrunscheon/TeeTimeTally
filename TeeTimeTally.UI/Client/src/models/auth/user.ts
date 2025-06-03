export interface MyGolferProfile {
  // Fields from the database (via API's /api/golfers/me)
  id: string; // Golfer's internal DB Id
  auth0UserId: string;
  fullName: string; // From DB
  email: string;    // From DB
  isSystemAdmin: boolean;
  createdAt: string; // ISO date-time string
  updatedAt: string; // ISO date-time string

  // Fields from claims (merged in by BFF)
  profileImage?: string; // e.g., from Auth0 'picture' claim
  roles: string[];       // e.g., from Auth0 roles/groups claim
  permissions: string[];  // e.g., from Auth0 permissions claim
}
