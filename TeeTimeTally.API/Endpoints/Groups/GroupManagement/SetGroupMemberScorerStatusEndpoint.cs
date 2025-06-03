using Dapper;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc; // For FromRoute
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Endpoints.Groups.GroupManagement;

// --- DTOs for this endpoint ---
public class SetGroupMemberScorerStatusRequest
{
	[FromRoute]
	public Guid GroupId { get; set; }

	[FromRoute]
	public Guid MemberGolferId { get; set; }

	public bool IsScorer { get; set; } // From request body
}

// Re-using GroupMemberResponse definition or define here for co-location
public record SetGroupMemberScorerStatusResponse(
	Guid GolferId,
	string FullName,
	string Email,
	bool IsScorer, // The updated scorer status for this group
	DateTime JoinedAt
);

// Helper for fetching current user's golfer ID and admin status
file record CurrentUserGolferInfo(Guid Id, bool IsSystemAdmin);

// --- Fluent Validator for SetGroupMemberScorerStatusRequest ---
public class SetGroupMemberScorerStatusRequestValidator : Validator<SetGroupMemberScorerStatusRequest>
{
	private readonly NpgsqlDataSource _dataSource;

	public SetGroupMemberScorerStatusRequestValidator(NpgsqlDataSource dataSource)
	{
		_dataSource = dataSource;

		RuleFor(x => x.GroupId)
			.NotEmpty().WithMessage("GroupId is required.")
			.MustAsync(async (groupId, cancellationToken) => await GroupExistsAndIsActiveAsync(groupId, cancellationToken))
			.WithMessage(x => $"Target group with ID '{x.GroupId}' not found or is inactive.");

		RuleFor(x => x.MemberGolferId)
			.NotEmpty().WithMessage("MemberGolferId is required.")
			.MustAsync(async (memberGolferId, cancellationToken) => await GolferExistsAndIsActiveAsync(memberGolferId, cancellationToken))
			.WithMessage(x => $"Target golfer with ID '{x.MemberGolferId}' not found or is inactive.");

		RuleFor(x => x) // Validate combination
			.MustAsync(async (req, cancellationToken) => await IsGolferMemberOfGroupAsync(req.GroupId, req.MemberGolferId, cancellationToken))
			.WithMessage(req => $"Golfer ID '{req.MemberGolferId}' is not a member of group ID '{req.GroupId}'.")
			.When(req => req.GroupId != Guid.Empty && req.MemberGolferId != Guid.Empty); // Only if IDs are valid
	}

	private async Task<bool> GroupExistsAndIsActiveAsync(Guid groupId, CancellationToken token)
	{
		if (groupId == Guid.Empty) return false;
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		return await connection.ExecuteScalarAsync<bool>(
			"SELECT EXISTS (SELECT 1 FROM groups WHERE id = @GroupId AND is_deleted = FALSE)",
			new { GroupId = groupId });
	}

	private async Task<bool> GolferExistsAndIsActiveAsync(Guid golferId, CancellationToken token)
	{
		if (golferId == Guid.Empty) return false;
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		return await connection.ExecuteScalarAsync<bool>(
			"SELECT EXISTS (SELECT 1 FROM golfers WHERE id = @GolferId AND is_deleted = FALSE)",
			new { GolferId = golferId });
	}

	private async Task<bool> IsGolferMemberOfGroupAsync(Guid groupId, Guid memberGolferId, CancellationToken token)
	{
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		return await connection.ExecuteScalarAsync<bool>(
			"SELECT EXISTS (SELECT 1 FROM group_members WHERE group_id = @GroupId AND golfer_id = @MemberGolferId)",
			new { GroupId = groupId, MemberGolferId = memberGolferId });
	}
}


[FastEndpoints.HttpPut("/groups/{GroupId:guid}/members/{MemberGolferId:guid}/scorer-status"), Authorize(Policy = Auth0Scopes.ManageGroupScorers)]
public class SetGroupMemberScorerStatusEndpoint(NpgsqlDataSource dataSource, ILogger<SetGroupMemberScorerStatusEndpoint> logger)
	: Endpoint<SetGroupMemberScorerStatusRequest, SetGroupMemberScorerStatusResponse>
{
	public override async Task HandleAsync(SetGroupMemberScorerStatusRequest req, CancellationToken ct)
	{
		// Request DTO validation (GroupId, MemberGolferId exist, member is in group) is handled by validator.
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(auth0UserId))
		{
			var unauthorizedProblem = TypedResults.Problem(title: "Unauthorized", detail: "User identifier not found.", statusCode: StatusCodes.Status401Unauthorized);
			await SendResultAsync(unauthorizedProblem);
			return;
		}

		await using var connection = await dataSource.OpenConnectionAsync(ct);

		// --- Authorization Check (Resource-based: is user Admin or Scorer for *this* group?) ---
		var currentUserInfo = await connection.QuerySingleOrDefaultAsync<CurrentUserGolferInfo>(
			"SELECT id AS Id, is_system_admin AS IsSystemAdmin FROM golfers WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;",
			new { Auth0UserId = auth0UserId });

		if (currentUserInfo == null)
		{
			logger.LogWarning("No active golfer profile found for Auth0 User ID {Auth0UserId} attempting to manage scorer status for group {GroupId}.", auth0UserId, req.GroupId);
			var forbiddenProblem = TypedResults.Problem(title: "Forbidden", detail: "User profile not found or inactive.", statusCode: StatusCodes.Status403Forbidden);
			await SendResultAsync(forbiddenProblem);
			return;
		}

		if (!currentUserInfo.IsSystemAdmin)
		{
			var isCurrentUserScorerForGroup = await connection.QuerySingleOrDefaultAsync<bool>(
				"SELECT TRUE FROM group_members WHERE group_id = @GroupId AND golfer_id = @CurrentUserId AND is_scorer = TRUE;",
				new { req.GroupId, CurrentUserId = currentUserInfo.Id });

			if (!isCurrentUserScorerForGroup)
			{
				logger.LogWarning("User {Auth0UserId} (GolferId: {GolferId}) is not an admin and not a scorer for group {GroupId}. Attempt to manage scorer status denied.",
					auth0UserId, currentUserInfo.Id, req.GroupId);
				var forbiddenProblem = TypedResults.Problem(title: "Forbidden", detail: "User is not authorized to manage scorer status for this group.", statusCode: StatusCodes.Status403Forbidden);
				await SendResultAsync(forbiddenProblem);
				return;
			}
		}
		logger.LogInformation("User {Auth0UserId} (GolferId: {GolferId}) authorized to manage scorer status for group {GroupId}, member {MemberGolferId}.",
			auth0UserId, currentUserInfo.Id, req.GroupId, req.MemberGolferId);

		// --- Database Operation (Update is_scorer flag) ---
		// Validator has already confirmed group, member, and membership exist.
		await using var transaction = await connection.BeginTransactionAsync(ct);
		int rowsAffected;
		try
		{
			const string updateScorerSql = @"
                UPDATE group_members
                SET is_scorer = @IsScorer
                WHERE group_id = @GroupId AND golfer_id = @MemberGolferId;";

			rowsAffected = await connection.ExecuteAsync(updateScorerSql,
				new { req.IsScorer, req.GroupId, req.MemberGolferId },
				transaction);

			if (rowsAffected > 0)
			{
				// Also update the group's updated_at timestamp as its roles/memberships have changed
				await connection.ExecuteAsync(
					"UPDATE groups SET updated_at = NOW() WHERE id = @GroupId;",
					new { req.GroupId }, transaction);
			}

			await transaction.CommitAsync(ct);
		}
		catch (Exception ex)
		{
			await transaction.RollbackAsync(ct);
			logger.LogError(ex, "Error updating scorer status for member {MemberGolferId} in group {GroupId}.", req.MemberGolferId, req.GroupId);
			var errorProblem = TypedResults.Problem(title: "Database Error", detail: "An error occurred while updating scorer status.", statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		if (rowsAffected == 0)
		{
			// This case should ideally be caught by the validator ensuring the member exists in the group.
			// If reached, it means the record was deleted between validation and update, or validator logic issue.
			logger.LogWarning("No group member record found for Golfer {MemberGolferId} in Group {GroupId} during update, though validator passed.", req.MemberGolferId, req.GroupId);
			var notFoundProblem = TypedResults.Problem(title: "Not Found", detail: "Group member not found for update. The member might have been removed.", statusCode: StatusCodes.Status404NotFound);
			await SendResultAsync(notFoundProblem);
			return;
		}

		// Fetch the updated member details to return
		const string selectMemberSql = @"
            SELECT
                gm.golfer_id AS GolferId,
                g.full_name AS FullName,
                g.email AS Email,
                gm.is_scorer AS IsScorer,
                gm.joined_at AS JoinedAt
            FROM group_members gm
            INNER JOIN golfers g ON gm.golfer_id = g.id
            WHERE gm.group_id = @GroupId AND gm.golfer_id = @MemberGolferId AND g.is_deleted = FALSE;";

		var updatedMemberResponse = await connection.QuerySingleOrDefaultAsync<SetGroupMemberScorerStatusResponse>(selectMemberSql,
			new { req.GroupId, req.MemberGolferId });

		if (updatedMemberResponse == null)
		{
			logger.LogError("Failed to retrieve updated member details for Golfer {MemberGolferId} in Group {GroupId} after update.", req.MemberGolferId, req.GroupId);
			var fetchErrorProblem = TypedResults.Problem(title: "Server Error", detail: "Successfully updated scorer status but failed to retrieve confirmation details.", statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(fetchErrorProblem);
			return;
		}

		logger.LogInformation("Scorer status for member {MemberGolferId} in group {GroupId} updated to {IsScorer} by user {Auth0UserId}.",
			req.MemberGolferId, req.GroupId, req.IsScorer, auth0UserId);
		await SendOkAsync(updatedMemberResponse, ct);
	}
}