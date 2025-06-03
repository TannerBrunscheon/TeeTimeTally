using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using Dapper;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes
using Microsoft.AspNetCore.Http; // For StatusCodes and TypedResults
using Microsoft.Extensions.Logging; // For ILogger
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace TeeTimeTally.API.Features.Groups.Endpoints.ListGroupMembers;

// --- DTOs for this endpoint ---
public class ListGroupMembersRequest
{
	/// <summary>
	/// The ID of the Group for which to list members.
	/// </summary>
	[FromRoute]
	public Guid GroupId { get; set; }
}

public record ListGroupMembersResponse(
	Guid GolferId,
	string FullName,
	string Email,
	bool IsScorer,
	DateTime JoinedAt
);

// Helper for fetching current user's golfer ID and admin status
file record CurrentUserGolferInfo(Guid Id, bool IsSystemAdmin);

[FastEndpoints.HttpGet("/groups/{GroupId:guid}/members"), Authorize(Policy = Auth0Scopes.ReadGroups)]
public class ListGroupMembersEndpoint : Endpoint<ListGroupMembersRequest, IEnumerable<ListGroupMembersResponse>>
{
	private readonly NpgsqlDataSource _dataSource;
	private readonly ILogger<ListGroupMembersEndpoint> _logger;

	public ListGroupMembersEndpoint(NpgsqlDataSource dataSource, ILogger<ListGroupMembersEndpoint> logger)
	{
		_dataSource = dataSource;
		_logger = logger;
	}

	public override async Task HandleAsync(ListGroupMembersRequest req, CancellationToken ct)
	{
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(auth0UserId))
		{
			var unauthorizedProblem = TypedResults.Problem(title: "Unauthorized", detail: "User identifier not found.", statusCode: StatusCodes.Status401Unauthorized);
			await SendResultAsync(unauthorizedProblem);
			return;
		}

		await using var connection = await _dataSource.OpenConnectionAsync(ct);

		// Fetch current user's internal ID and admin status
		var currentUserInfo = await connection.QuerySingleOrDefaultAsync<CurrentUserGolferInfo>(
			"SELECT id AS Id, is_system_admin AS IsSystemAdmin FROM golfers WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;",
			new { Auth0UserId = auth0UserId });

		if (currentUserInfo == null)
		{
			_logger.LogWarning("No active golfer profile found for Auth0 User ID {Auth0UserId} attempting to list members for group {GroupId}.", auth0UserId, req.GroupId);
			var forbiddenProblem = TypedResults.Problem(title: "Forbidden", detail: "User profile not found or inactive.", statusCode: StatusCodes.Status403Forbidden);
			await SendResultAsync(forbiddenProblem);
			return;
		}

		// Authorization Check: User must be Admin or a Scorer for this specific group
		if (!currentUserInfo.IsSystemAdmin)
		{
			var isScorerForGroup = await connection.QuerySingleOrDefaultAsync<bool>(
				"SELECT TRUE FROM group_members WHERE group_id = @GroupId AND golfer_id = @GolferId AND is_scorer = TRUE;",
				new { req.GroupId, GolferId = currentUserInfo.Id });

			if (!isScorerForGroup)
			{
				_logger.LogWarning("User {Auth0UserId} (GolferId: {GolferId}) is not an admin and not a scorer for group {GroupId}. Attempt to list members denied.",
					auth0UserId, currentUserInfo.Id, req.GroupId);
				var forbiddenProblem = TypedResults.Problem(title: "Forbidden", detail: "User is not authorized to view members of this group.", statusCode: StatusCodes.Status403Forbidden);
				await SendResultAsync(forbiddenProblem);
				return;
			}
		}
		_logger.LogInformation("User {Auth0UserId} (GolferId: {GolferId}) authorized to list members for group {GroupId}.", auth0UserId, currentUserInfo.Id, req.GroupId);

		// Check if the group itself exists and is active
		var groupExists = await connection.QuerySingleOrDefaultAsync<bool>(
			"SELECT TRUE FROM groups WHERE id = @GroupId AND is_deleted = FALSE;",
			new { req.GroupId });

		if (!groupExists)
		{
			_logger.LogWarning("Attempt to list members for non-existent or inactive group {GroupId}.", req.GroupId);
			var notFoundProblem = TypedResults.Problem(title: "Not Found", detail: "Target group not found or is inactive.", statusCode: StatusCodes.Status404NotFound);
			await SendResultAsync(notFoundProblem);
			return;
		}

		// Fetch group members
		const string sql = @"
            SELECT
                gm.golfer_id AS GolferId,
                g.full_name AS FullName,
                g.email AS Email,
                gm.is_scorer AS IsScorer,
                gm.joined_at AS JoinedAt
            FROM group_members gm
            INNER JOIN golfers g ON gm.golfer_id = g.id
            WHERE gm.group_id = @GroupId
              AND g.is_deleted = FALSE -- Only include active golfers in the member list
            ORDER BY g.full_name;";

		IEnumerable<ListGroupMembersResponse> members;

		try
		{
			members = await connection.QueryAsync<ListGroupMembersResponse>(sql, new { req.GroupId });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error fetching members for group {GroupId}.", req.GroupId);
			var errorProblem = TypedResults.Problem(
				title: "Database Error",
				detail: "An error occurred while fetching group members.",
				statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		await SendOkAsync(members ?? Enumerable.Empty<ListGroupMembersResponse>(), ct);
	}
}