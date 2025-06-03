namespace TeeTimeTally.UI.Identity;

// This DTO will be returned by the BFF's /api/authentication/user-info endpoint
// It combines database profile information with claims.
public sealed record UserInfoResponse(
	// Fields from the database (via API's /api/golfers/me)
	Guid Id, // Golfer's internal DB Id
	string Auth0UserId,
	string FullName, // From DB
	string Email,    // From DB (should match claim, but DB is source of truth after link)
	bool IsSystemAdmin,
	DateTime CreatedAt,
	DateTime UpdatedAt,

	// Fields from claims (merged in by BFF)
	string? ProfileImage, // e.g., from Auth0 'picture' claim
	string[] Roles,       // e.g., from Auth0 roles/groups claim
	string[] Permissions  // e.g., from Auth0 permissions claim
);

// This is a helper DTO that the BFF will expect back from the TeeTimeTally.API's /api/golfers/me endpoint.
// It should match the MyGolferProfileResponse defined in the API's GetMyProfileEndpoint.cs
// We define it here for clarity in the BFF's deserialization step.
public sealed record ApiGolferProfileResponse(
	Guid Id,
	string Auth0UserId,
	string FullName,
	string Email,
	bool IsSystemAdmin,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	bool IsDeleted,
	DateTime? DeletedAt
);
