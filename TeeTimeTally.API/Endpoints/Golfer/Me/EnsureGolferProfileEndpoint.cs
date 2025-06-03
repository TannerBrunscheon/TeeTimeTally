using Dapper;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Security.Claims;

namespace TeeTimeTally.API.Endpoints.Golfer.Me;

// Response DTO remains the same
public record GolferProfileResponse(
	Guid Id, // Internal UUID PK
	string Auth0UserId,
	string FullName,
	string Email,
	bool IsSystemAdmin,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	bool IsDeleted,
	DateTime? DeletedAt
);

// Internal record to fetch existing golfer data
file record ExistingGolferFullData(
	Guid Id,
	string? Auth0UserId,
	string FullName,
	string Email,
	bool IsSystemAdmin,
	bool IsDeleted,
	DateTime CreatedAt,
	DateTime UpdatedAt // Added UpdatedAt for completeness in the fetched object
);

[HttpPost("/golfers/me/ensure-profile"), Authorize]
public class EnsureGolferProfileEndpoint(NpgsqlDataSource dataSource, ILogger<EnsureGolferProfileEndpoint> logger)
	: EndpointWithoutRequest<GolferProfileResponse>
{
	public override async Task HandleAsync(CancellationToken ct)
	{
		var auth0UserIdFromClaims = User.FindFirstValue(ClaimTypes.NameIdentifier);
		var emailFromClaims = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email"); // Auth0 can use 'email'

		if (string.IsNullOrEmpty(auth0UserIdFromClaims)|| string.IsNullOrEmpty(emailFromClaims))
		{
			logger.LogWarning("Essential claims (auth0_user_id, name, or email) missing for user during profile ensure.");
			var badRequestProblem = TypedResults.Problem(
				title: "Bad Request",
				detail: "Essential user information missing from authentication token.",
				statusCode: StatusCodes.Status400BadRequest);
			await SendResultAsync(badRequestProblem);
			return;
		}

		await using var connection = await dataSource.OpenConnectionAsync(ct);
		await using var transaction = await connection.BeginTransactionAsync(ct);

		try
		{
			// Step 1: Try to find any golfer (active or soft-deleted) by auth0_user_id
			const string selectByAuth0IdSql = @"
                SELECT id AS Id, auth0_user_id AS Auth0UserId, full_name AS FullName, email AS Email, 
                       is_system_admin AS IsSystemAdmin, is_deleted AS IsDeleted, created_at AS CreatedAt, updated_at as UpdatedAt
                FROM golfers 
                WHERE auth0_user_id = @Auth0UserId;"; // Fetches regardless of is_deleted initially

			var golferByAuth0Id = await connection.QuerySingleOrDefaultAsync<ExistingGolferFullData>(
				selectByAuth0IdSql, new { Auth0UserId = auth0UserIdFromClaims }, transaction);


			GolferProfileResponse? finalProfileResponse;
			if (golferByAuth0Id != null)
			{
				// Golfer found by Auth0 ID
				if (!golferByAuth0Id.IsDeleted)
				{
					// Profile is already active and linked. Return it as is.
					// No updates to FullName or Email from claims in this specific scenario.
					logger.LogInformation("Active golfer profile already linked for Auth0 User ID {Auth0UserId}. Internal ID: {GolferId}. Returning existing profile.",
						auth0UserIdFromClaims, golferByAuth0Id.Id);

					// Construct the response from the fetched data
					finalProfileResponse = new GolferProfileResponse(
						golferByAuth0Id.Id,
						golferByAuth0Id.Auth0UserId!, // Known to be not null here
						golferByAuth0Id.FullName,
						golferByAuth0Id.Email,
						golferByAuth0Id.IsSystemAdmin,
						golferByAuth0Id.CreatedAt,
						golferByAuth0Id.UpdatedAt, // Use fetched UpdatedAt
						golferByAuth0Id.IsDeleted,
						null // DeletedAt is null for active users
					);
				}
				else
				{
					// Profile is linked but soft-deleted. Reactivate and update.
					logger.LogInformation("Soft-deleted golfer profile found for Auth0 User ID {Auth0UserId}. Internal ID: {GolferId}. Reactivating and updating.",
						auth0UserIdFromClaims, golferByAuth0Id.Id);

					const string reactivateAndUpdateSql = @"
                        UPDATE golfers
                        SET email = @Email,
                            is_deleted = FALSE,
                            deleted_at = NULL,
                            updated_at = NOW()
                        WHERE id = @Id
                        RETURNING id AS Id, auth0_user_id AS Auth0UserId, full_name AS FullName, email AS Email, 
                                  is_system_admin AS IsSystemAdmin, created_at AS CreatedAt, updated_at AS UpdatedAt,
                                  is_deleted AS IsDeleted, deleted_at AS DeletedAt;";

					finalProfileResponse = await connection.QuerySingleAsync<GolferProfileResponse>(reactivateAndUpdateSql, new
					{
						golferByAuth0Id.Id,
						Email = emailFromClaims
					}, transaction);
				}
			}
			else
			{
				// No golfer found by Auth0 ID. Try to link by email or create new.
				logger.LogInformation("No golfer found by Auth0 User ID {Auth0UserId}. Checking for unlinked profile by email {Email}.", auth0UserIdFromClaims, emailFromClaims);
				const string selectUnlinkedByEmailSql = @"
                    SELECT id AS Id, auth0_user_id AS Auth0UserId, full_name AS FullName, email AS Email, 
                           is_system_admin AS IsSystemAdmin, is_deleted AS IsDeleted, created_at AS CreatedAt, updated_at as UpdatedAt
                    FROM golfers 
                    WHERE email = @Email AND auth0_user_id IS NULL AND is_deleted = FALSE;";

				var golferByEmail = await connection.QuerySingleOrDefaultAsync<ExistingGolferFullData>(
					selectUnlinkedByEmailSql, new { Email = emailFromClaims }, transaction);

				if (golferByEmail != null)
				{
					// Found an active, unlinked profile by email. Link it and update FullName.
					logger.LogInformation("Found unlinked active profile by email {Email} for Auth0 User ID {Auth0UserId}. Internal ID: {GolferId}. Linking and updating name.",
						emailFromClaims, auth0UserIdFromClaims, golferByEmail.Id);

					const string linkAndUpdateSql = @"
                        UPDATE golfers
                        SET auth0_user_id = @Auth0UserId,
                            full_name = @FullName, 
                            updated_at = NOW()
                        WHERE id = @Id AND auth0_user_id IS NULL AND is_deleted = FALSE -- Extra safety
                        RETURNING id AS Id, auth0_user_id AS Auth0UserId, full_name AS FullName, email AS Email, 
                                  is_system_admin AS IsSystemAdmin, created_at AS CreatedAt, updated_at AS UpdatedAt,
                                  is_deleted AS IsDeleted, deleted_at AS DeletedAt;";

					finalProfileResponse = await connection.QuerySingleAsync<GolferProfileResponse>(linkAndUpdateSql, new
					{
						golferByEmail.Id,
						Auth0UserId = auth0UserIdFromClaims,
						Email = emailFromClaims // Email is used for matching, can be included for RETURNING consistency
					}, transaction);
				}
				else
				{
					// No existing profile to link. Create a new one.
					logger.LogInformation("No linkable profile found for Auth0 User ID {Auth0UserId} and Email {Email}. Creating new profile.", auth0UserIdFromClaims, emailFromClaims);
					const string insertNewGolferSql = @"
                        INSERT INTO golfers (auth0_user_id, full_name, email, is_system_admin, is_deleted, deleted_at)
                        VALUES (@Auth0UserId, @FullName, @Email, FALSE, FALSE, NULL)
                        RETURNING id AS Id, auth0_user_id AS Auth0UserId, full_name AS FullName, email AS Email, 
                                  is_system_admin AS IsSystemAdmin, created_at AS CreatedAt, updated_at AS UpdatedAt,
                                  is_deleted AS IsDeleted, deleted_at AS DeletedAt;";

					finalProfileResponse = await connection.QuerySingleAsync<GolferProfileResponse>(insertNewGolferSql, new
					{
						Auth0UserId = auth0UserIdFromClaims,
						FullName = emailFromClaims,
						Email = emailFromClaims
					}, transaction);
				}
			}

			await transaction.CommitAsync(ct);

			if (finalProfileResponse != null)
			{
				await SendOkAsync(finalProfileResponse, ct);
			}
			else
			{
				// This state should ideally not be reached if logic is correct
				logger.LogError("Golfer profile was unexpectedly null after ensure logic for Auth0 User ID {Auth0UserId}", auth0UserIdFromClaims);
				var serverErrorProblem = TypedResults.Problem(title: "Server Error", detail: "Could not ensure golfer profile state.", statusCode: StatusCodes.Status500InternalServerError);
				await SendResultAsync(serverErrorProblem);
			}
		}
		catch (PostgresException ex)
		{
			await transaction.RollbackAsync(ct);
			logger.LogError(ex, "Database error ensuring golfer profile for Auth0 User ID {Auth0UserId}. SQLState: {SqlState}", auth0UserIdFromClaims, ex.SqlState);
			var problemDetails = TypedResults.Problem(
				title: ex.SqlState == "23505" ? "Conflict" : "Database Error", // 23505 is unique_violation
				detail: ex.SqlState == "23505" ? "A profile conflict occurred. This email or Auth0 ID might already be in use by another active account."
												 : "An unexpected database error occurred while ensuring your profile.",
				statusCode: ex.SqlState == "23505" ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError
			);
			await SendResultAsync(problemDetails);
		}
		catch (Exception ex)
		{
			await transaction.RollbackAsync(ct);
			logger.LogError(ex, "Unexpected error ensuring golfer profile for Auth0 User ID {Auth0UserId}", auth0UserIdFromClaims);
			var generalErrorProblem = TypedResults.Problem(title: "Internal Server Error", detail: "An unexpected error occurred.", statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(generalErrorProblem);
		}
	}
}
