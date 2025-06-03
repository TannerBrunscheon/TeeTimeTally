using Dapper;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Security.Claims;

namespace TeeTimeTally.API.Endpoints.Golfer.Me;

// Response DTO - can reuse or adapt an existing one if suitable.
// Using the same GolferProfileResponse from EnsureGolferProfileEndpoint for consistency.
public record GetMyGolferProfileResponse(
	Guid Id, // Internal UUID PK
	string Auth0UserId,
	string FullName,
	string Email,
	bool IsSystemAdmin,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	bool IsDeleted, // Should be false for a profile returned by this endpoint
	DateTime? DeletedAt
);

[HttpGet("/golfers/me"), Authorize] // General authorization, specific scope like ReadSelf could be added
public class GetMyProfileEndpoint(NpgsqlDataSource dataSource, ILogger<GetMyProfileEndpoint> logger)
	: EndpointWithoutRequest<GetMyGolferProfileResponse>
{
	public override async Task HandleAsync(CancellationToken ct)
	{
		var auth0UserIdFromClaims = User.FindFirstValue(ClaimTypes.NameIdentifier);

		if (string.IsNullOrEmpty(auth0UserIdFromClaims))
		{
			logger.LogWarning("Auth0 User ID claim (NameIdentifier) is missing for user attempting to get their profile.");
			await SendUnauthorizedAsync(ct);
			return;
		}

		const string sql = @"
            SELECT 
                id AS Id, 
                auth0_user_id AS Auth0UserId, 
                full_name AS FullName, 
                email AS Email, 
                is_system_admin AS IsSystemAdmin, 
                created_at AS CreatedAt, 
                updated_at AS UpdatedAt,
                is_deleted AS IsDeleted,
                deleted_at AS DeletedAt
            FROM golfers
            WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;";

		GetMyGolferProfileResponse? golferProfile;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);
			golferProfile = await connection.QuerySingleOrDefaultAsync<GetMyGolferProfileResponse>(sql,
				new { Auth0UserId = auth0UserIdFromClaims });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error fetching golfer profile for Auth0 User ID {Auth0UserId}.", auth0UserIdFromClaims);
			var errorProblem = TypedResults.Problem(
				title: "Database Error",
				detail: "An error occurred while fetching your golfer profile.",
				statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		if (golferProfile == null)
		{
			// This case implies that EnsureGolferProfileEndpoint might not have run successfully,
			// or the user's profile was somehow deleted after being ensured.
			// For a "get my profile" endpoint after login, a 404 is appropriate if the DB record doesn't exist.
			logger.LogWarning("No active golfer profile found in database for authenticated Auth0 User ID {Auth0UserId}. EnsureGolferProfileEndpoint might need to be called or has failed.", auth0UserIdFromClaims);
			await SendNotFoundAsync(ct);
			return;
		}

		await SendOkAsync(golferProfile, ct);
	}
}
