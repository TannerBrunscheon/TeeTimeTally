using Dapper;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Security.Claims;

namespace TeeTimeTally.API.Endpoints.Golfer.Me;

// DTOs co-located
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

// This internal record is used to fetch existing golfer data including soft-delete status and auth0_user_id
file record ExistingGolferFullData(
	Guid Id,
	string? Auth0UserId, // Nullable if pre-created locally
	string FullName,
	string Email,
	bool IsSystemAdmin,
	bool IsDeleted,
	DateTime CreatedAt
);

[HttpPost("/golfers/me/ensure-profile"), Authorize]
public class EnsureGolferProfileEndpoint(NpgsqlDataSource dataSource, ILogger<EnsureGolferProfileEndpoint> logger) : EndpointWithoutRequest<GolferProfileResponse>
{
	public override async Task HandleAsync(CancellationToken ct)
	{
		var auth0UserIdFromClaims = User.FindFirstValue(ClaimTypes.NameIdentifier);
		var fullNameFromClaims = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("name");
		var emailFromClaims = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");

		if (string.IsNullOrEmpty(auth0UserIdFromClaims) || string.IsNullOrEmpty(fullNameFromClaims) || string.IsNullOrEmpty(emailFromClaims))
		{
			logger.LogWarning("Essential claims (auth0_user_id, name, or email) missing for user during profile ensure.");
			var badRequestProblem = TypedResults.Problem(
				title: "Bad Request",
				detail: "Essential user information missing from authentication token.",
				statusCode: StatusCodes.Status400BadRequest);
			await SendResultAsync(badRequestProblem);
			return;
		}

		GolferProfileResponse? finalProfileResponse = null;

		await using var connection = await dataSource.OpenConnectionAsync(ct);
		await using var transaction = await connection.BeginTransactionAsync(ct);

		try
		{
			// Step 1: Try to find any golfer (active or soft-deleted) by auth0_user_id
			const string selectByAuth0IdSql = @"
                SELECT id AS Id, auth0_user_id AS Auth0UserId, full_name AS FullName, email AS Email, 
                       is_system_admin AS IsSystemAdmin, is_deleted AS IsDeleted, created_at AS CreatedAt
                FROM golfers 
                WHERE auth0_user_id = @Auth0UserId;";

			var golferByAuth0Id = await connection.QuerySingleOrDefaultAsync<ExistingGolferFullData>(
				selectByAuth0IdSql, new { Auth0UserId = auth0UserIdFromClaims }, transaction);

			if (golferByAuth0Id != null)
			{
				// Found by Auth0 ID. Update (if needed) and ensure active.
				logger.LogInformation("Found existing golfer profile by Auth0 User ID {Auth0UserId}. Internal ID: {GolferId}, IsDeleted: {IsDeleted}. Updating/reactivating.",
					auth0UserIdFromClaims, golferByAuth0Id.Id, golferByAuth0Id.IsDeleted);

				const string updateAndReactivateSql = @"
                    UPDATE golfers
                    SET full_name = @FullName,
                        email = @Email,      -- Update email if it changed in Auth0
                        is_deleted = FALSE,
                        deleted_at = NULL,
                        updated_at = NOW()
                    WHERE id = @Id
                    RETURNING id AS Id, auth0_user_id AS Auth0UserId, full_name AS FullName, email AS Email, 
                              is_system_admin AS IsSystemAdmin, created_at AS CreatedAt, updated_at AS UpdatedAt,
                              is_deleted AS IsDeleted, deleted_at AS DeletedAt;";

				finalProfileResponse = await connection.QuerySingleAsync<GolferProfileResponse>(updateAndReactivateSql, new
				{
					golferByAuth0Id.Id,
					FullName = fullNameFromClaims,
					Email = emailFromClaims
				}, transaction);
			}
			else
			{
				// Step 2: Not found by Auth0 ID. Try to find an active, unlinked golfer by email.
				logger.LogInformation("No golfer found by Auth0 User ID {Auth0UserId}. Checking for unlinked profile by email {Email}.", auth0UserIdFromClaims, emailFromClaims);
				const string selectByEmailSql = @"
                    SELECT id AS Id, auth0_user_id AS Auth0UserId, full_name AS FullName, email AS Email, 
                           is_system_admin AS IsSystemAdmin, is_deleted AS IsDeleted, created_at AS CreatedAt
                    FROM golfers 
                    WHERE email = @Email AND auth0_user_id IS NULL AND is_deleted = FALSE;";

				var golferByEmail = await connection.QuerySingleOrDefaultAsync<ExistingGolferFullData>(
					selectByEmailSql, new { Email = emailFromClaims }, transaction);

				if (golferByEmail != null)
				{
					// Found an active, unlinked profile by email. Link it and update.
					logger.LogInformation("Found unlinked active profile by email {Email} for Auth0 User ID {Auth0UserId}. Internal ID: {GolferId}. Linking and updating.",
						emailFromClaims, auth0UserIdFromClaims, golferByEmail.Id);

					const string linkAndUpdateSql = @"
                        UPDATE golfers
                        SET auth0_user_id = @Auth0UserId,
                            full_name = @FullName, -- Update name from claims
                            -- Email is already matched, but update if casing differs or for consistency
                            email = @Email, 
                            is_deleted = FALSE, 
                            deleted_at = NULL,
                            updated_at = NOW()
                        WHERE id = @Id
                        RETURNING id AS Id, auth0_user_id AS Auth0UserId, full_name AS FullName, email AS Email, 
                                  is_system_admin AS IsSystemAdmin, created_at AS CreatedAt, updated_at AS UpdatedAt,
                                  is_deleted AS IsDeleted, deleted_at AS DeletedAt;";

					finalProfileResponse = await connection.QuerySingleAsync<GolferProfileResponse>(linkAndUpdateSql, new
					{
						golferByEmail.Id,
						Auth0UserId = auth0UserIdFromClaims,
						FullName = fullNameFromClaims,
						Email = emailFromClaims
					}, transaction);
				}
				else
				{
					// Step 3: No existing profile found by Auth0 ID or linkable email. Create a new one.
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
						FullName = fullNameFromClaims,
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
				logger.LogError("Golfer profile was unexpectedly null after sync logic for Auth0 User ID {Auth0UserId}", auth0UserIdFromClaims);
				var serverErrorProblem = TypedResults.Problem(title: "Server Error", detail: "Could not ensure golfer profile state.", statusCode: StatusCodes.Status500InternalServerError);
				await SendResultAsync(serverErrorProblem);
			}
		}
		catch (PostgresException ex)
		{
			await transaction.RollbackAsync(ct);
			logger.LogError(ex, "Database error ensuring golfer profile for Auth0 User ID {Auth0UserId}. SQLState: {SqlState}", auth0UserIdFromClaims, ex.SqlState);
			// This could be a unique constraint violation on email if another active user already has it
			// AND that other user is linked to a *different* auth0_user_id.
			// The partial unique index on auth0_user_id WHERE is_deleted = FALSE handles the auth0_id uniqueness for active users.
			// The partial unique index on email WHERE is_deleted = FALSE handles email uniqueness for active users.
			var problemDetails = TypedResults.Problem(
				title: ex.SqlState == "23505" ? "Conflict" : "Database Error",
				detail: ex.SqlState == "23505" ? "A profile conflict occurred. This email might already be in use by another active account, or an issue with Auth0 ID linkage."
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