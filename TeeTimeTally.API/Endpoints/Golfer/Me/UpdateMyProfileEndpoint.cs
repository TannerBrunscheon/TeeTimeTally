using Dapper;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Security.Claims;
// Assuming MyGolferProfileResponse is accessible or defined similarly
// If not, you might need to define it or reference it from another Me endpoint.
// For this example, let's assume it's defined as in GetMyProfileEndpoint.
// namespace TeeTimeTally.API.Endpoints.Golfer.Me; (ensure this matches if response is shared)

namespace TeeTimeTally.API.Endpoints.Golfer.Me;

// --- Request DTO ---
public class UpdateMyProfileRequest
{
	/// <summary>
	/// The new full name for the golfer.
	/// </summary>
	public string FullName { get; set; } = string.Empty;
}

// --- Response DTO (reusing from GetMyProfileEndpoint for consistency) ---
public record UpdateMyProfileResponse(
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

// --- Fluent Validator for UpdateMyProfileRequest ---
public class UpdateMyProfileRequestValidator : Validator<UpdateMyProfileRequest>
{
	public UpdateMyProfileRequestValidator()
	{
		RuleFor(x => x.FullName)
			.NotEmpty().WithMessage("Full name cannot be empty.")
			.MaximumLength(200).WithMessage("Full name cannot exceed 200 characters.");
	}
}

[HttpPut("/golfers/me"), Authorize] // Requires user to be authenticated
public class UpdateMyProfileEndpoint(NpgsqlDataSource dataSource, ILogger<UpdateMyProfileEndpoint> logger)
	: Endpoint<UpdateMyProfileRequest, UpdateMyProfileResponse>
{
	public override async Task HandleAsync(UpdateMyProfileRequest req, CancellationToken ct)
	{
		var auth0UserIdFromClaims = User.FindFirstValue(ClaimTypes.NameIdentifier);

		if (string.IsNullOrEmpty(auth0UserIdFromClaims))
		{
			logger.LogWarning("Auth0 User ID claim (NameIdentifier) is missing for user attempting to update their profile.");
			await SendUnauthorizedAsync(ct);
			return;
		}

		// SQL to update the golfer's full_name based on their Auth0 User ID
		// and return the updated profile.
		const string updateProfileSql = @"
            UPDATE golfers
            SET full_name = @FullName,
                updated_at = NOW()
            WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE
            RETURNING 
                id AS Id, 
                auth0_user_id AS Auth0UserId, 
                full_name AS FullName, 
                email AS Email, 
                is_system_admin AS IsSystemAdmin, 
                created_at AS CreatedAt, 
                updated_at AS UpdatedAt,
                is_deleted AS IsDeleted,
                deleted_at AS DeletedAt;";

		UpdateMyProfileResponse? updatedProfile;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);
			updatedProfile = await connection.QuerySingleOrDefaultAsync<UpdateMyProfileResponse>(updateProfileSql,
				new { req.FullName, Auth0UserId = auth0UserIdFromClaims });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error updating golfer profile for Auth0 User ID {Auth0UserId}.", auth0UserIdFromClaims);
			var errorProblem = TypedResults.Problem(
				title: "Database Error",
				detail: "An error occurred while updating your profile.",
				statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		if (updatedProfile == null)
		{
			// This means no active golfer profile was found for the authenticated user.
			// This could happen if the EnsureGolferProfileEndpoint hasn't run or failed.
			logger.LogWarning("No active golfer profile found to update for Auth0 User ID {Auth0UserId}.", auth0UserIdFromClaims);
			await SendNotFoundAsync(ct); // Or Forbidden if the expectation is profile must exist
			return;
		}

		logger.LogInformation("Golfer profile updated successfully for Auth0 User ID {Auth0UserId}. New FullName: {FullName}", auth0UserIdFromClaims, updatedProfile.FullName);
		await SendOkAsync(updatedProfile, ct);
	}
}
