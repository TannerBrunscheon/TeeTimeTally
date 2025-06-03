using Dapper;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Endpoints.Rounds;

// --- DTOs for this endpoint ---
public record GetAllOpenRoundsResponse(
	Guid RoundId,
	DateTime RoundDate,
	string Status,
	Guid GroupId,
	string GroupName,
	Guid CourseId,
	string CourseName,
	short? NumPlayers // Changed from int to short? (nullable short)
);

// Helper for fetching current user's golfer ID and admin status (file-scoped)
file record CurrentUserGolferInfo(Guid Id, bool IsSystemAdmin);

[HttpGet("/rounds/open"), Authorize(Policy = Auth0Scopes.ReadGroupRounds)]
public class GetAllOpenRoundsEndpoint(NpgsqlDataSource dataSource, ILogger<GetAllOpenRoundsEndpoint> logger)
	: EndpointWithoutRequest<IEnumerable<GetAllOpenRoundsResponse>>
{
	public override async Task HandleAsync(CancellationToken ct)
	{
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(auth0UserId))
		{
			var unauthorizedProblem = TypedResults.Problem(title: "Unauthorized", detail: "User identifier not found.", statusCode: StatusCodes.Status401Unauthorized);
			await SendResultAsync(unauthorizedProblem);
			return;
		}

		await using var connection = await dataSource.OpenConnectionAsync(ct);

		var currentUserInfo = await connection.QuerySingleOrDefaultAsync<CurrentUserGolferInfo>(
			"SELECT id AS Id, is_system_admin AS IsSystemAdmin FROM golfers WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;",
			new { Auth0UserId = auth0UserId });

		if (currentUserInfo == null)
		{
			logger.LogWarning("No active golfer profile found for Auth0 User ID {Auth0UserId} attempting to fetch open rounds.", auth0UserId);
			await SendOkAsync(Enumerable.Empty<GetAllOpenRoundsResponse>(), ct); // Return empty if profile lookup fails
			return;
		}

		string sql;
		object? queryParams;
		// Statuses considered "open" for viewing in this list.
		// "Completed" means scores are in, but it's not yet "Finalized" with payouts.
		var openStatuses = new[] { "PendingSetup", "SetupComplete", "InProgress", "Completed" };

		if (currentUserInfo.IsSystemAdmin)
		{
			logger.LogInformation("Admin user {GolferId} fetching all open rounds.", currentUserInfo.Id);
			sql = @"
                SELECT
                    r.id AS RoundId,
                    r.round_date AS RoundDate,
                    r.status::TEXT AS Status,
                    r.group_id AS GroupId,
                    g.name AS GroupName,
                    r.course_id AS CourseId,
                    c.name AS CourseName,
                    r.num_players AS NumPlayers
                FROM rounds r
                INNER JOIN groups g ON r.group_id = g.id
                INNER JOIN courses c ON r.course_id = c.id
                WHERE r.is_deleted = FALSE 
                  AND g.is_deleted = FALSE 
                  AND c.is_deleted = FALSE
                  AND r.status::TEXT = ANY(@OpenStatuses)
                ORDER BY r.round_date DESC, g.name;";
			queryParams = new { OpenStatuses = openStatuses };
		}
		else // Non-admin scorer - fetch for groups they are a scorer for
		{
			logger.LogInformation("User {GolferId} fetching open rounds for groups they manage.", currentUserInfo.Id);
			sql = @"
                SELECT
                    r.id AS RoundId,
                    r.round_date AS RoundDate,
                    r.status::TEXT AS Status,
                    r.group_id AS GroupId,
                    g.name AS GroupName,
                    r.course_id AS CourseId,
                    c.name AS CourseName,
                    r.num_players AS NumPlayers
                FROM rounds r
                INNER JOIN groups g ON r.group_id = g.id
                INNER JOIN courses c ON r.course_id = c.id
                INNER JOIN group_members gm ON g.id = gm.group_id
                WHERE r.is_deleted = FALSE 
                  AND g.is_deleted = FALSE 
                  AND c.is_deleted = FALSE
                  AND r.status::TEXT = ANY(@OpenStatuses)
                  AND gm.golfer_id = @CurrentGolferId AND gm.is_scorer = TRUE
                ORDER BY r.round_date DESC, g.name;";
			queryParams = new { CurrentGolferId = currentUserInfo.Id, OpenStatuses = openStatuses };
		}

		IEnumerable<GetAllOpenRoundsResponse> openRounds;

		try
		{
			openRounds = await connection.QueryAsync<GetAllOpenRoundsResponse>(sql, queryParams);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error fetching open rounds for user {Auth0UserId} (GolferId: {GolferId}, IsAdmin: {IsAdmin}). SQL: {SQLQuery}",
				auth0UserId, currentUserInfo.Id, currentUserInfo.IsSystemAdmin, sql); // Log the query for debugging
			var errorProblem = TypedResults.Problem(
				title: "Database Error",
				detail: "An error occurred while fetching open rounds data.",
				statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		await SendOkAsync(openRounds ?? Enumerable.Empty<GetAllOpenRoundsResponse>(), ct);
	}
}
