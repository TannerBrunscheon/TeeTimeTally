using Dapper;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc; 
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.Shared.Auth;

namespace TeeTimeTally.API.Features.Groups.Endpoints.RemoveGolfersFromGroup;

// --- DTOs for this endpoint ---
public class RemoveGolfersFromGroupRequest
{
	[FromRoute]
	public Guid GroupId { get; set; }

	public List<Guid> GolferIds { get; set; } = new();
}

public record RemoveGolfersFromGroupResponse(
	string Message,
	Guid GroupId,
	int RequestedToRemoveCount,
	int SuccessfullyRemovedCount
);

// Helper for fetching current user's golfer ID and admin status
file record CurrentUserGolferInfo(Guid Id, bool IsSystemAdmin);

// --- Fluent Validator for RemoveGolfersFromGroupRequest ---
public class RemoveGolfersFromGroupRequestValidator : Validator<RemoveGolfersFromGroupRequest>
{
	private readonly NpgsqlDataSource _dataSource;

	public RemoveGolfersFromGroupRequestValidator(NpgsqlDataSource dataSource)
	{
		_dataSource = dataSource;

		RuleFor(x => x.GroupId)
			.NotEmpty().WithMessage("GroupId is required.")
			.MustAsync(async (groupId, cancellationToken) => await GroupExistsAndIsActiveAsync(groupId, cancellationToken))
			.WithMessage(x => $"Group with ID '{x.GroupId}' not found or is inactive.");

		RuleFor(x => x.GolferIds)
			.NotEmpty().WithMessage("At least one GolferId must be provided to remove.")
			.Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("GolferIds list contains duplicates.");
		// Note: We don't necessarily need to validate if each golfer ID exists in the 'golfers' table here,
		// as the DELETE operation on 'group_members' will simply not affect rows if the golfer_id isn't a member.
		// However, if specific feedback per golfer ID was needed, that check could be added.
	}

	private async Task<bool> GroupExistsAndIsActiveAsync(Guid groupId, CancellationToken token)
	{
		if (groupId == Guid.Empty) return false;
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		return await connection.ExecuteScalarAsync<bool>(
			"SELECT EXISTS (SELECT 1 FROM groups WHERE id = @GroupId AND is_deleted = FALSE)",
			new { GroupId = groupId });
	}
}


[FastEndpoints.HttpDelete("/groups/{GroupId:guid}/members"), Authorize(Policy = Auth0Scopes.ManageGroupMembers)]
public class RemoveGolfersFromGroupEndpoint(NpgsqlDataSource dataSource, ILogger<RemoveGolfersFromGroupEndpoint> logger)
	: Endpoint<RemoveGolfersFromGroupRequest, RemoveGolfersFromGroupResponse>
{
	public override async Task HandleAsync(RemoveGolfersFromGroupRequest req, CancellationToken ct)
	{
		// Request DTO validation (GroupId exists, GolferIds list not empty) is handled by validator.

		var distinctGolferIdsToRemove = req.GolferIds.Distinct().ToList();
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

		if (string.IsNullOrEmpty(auth0UserId))
		{
			var unauthorizedProblem = TypedResults.Problem(title: "Unauthorized", detail: "User identifier not found.", statusCode: StatusCodes.Status401Unauthorized);
			await SendResultAsync(unauthorizedProblem);
			return;
		}

		await using var connection = await dataSource.OpenConnectionAsync(ct);

		// --- Authorization Check (Resource-based) ---
		var currentUserInfo = await connection.QuerySingleOrDefaultAsync<CurrentUserGolferInfo>(
			"SELECT id AS Id, is_system_admin AS IsSystemAdmin FROM golfers WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;",
			new { Auth0UserId = auth0UserId });

		if (currentUserInfo == null)
		{
			logger.LogWarning("No active golfer profile found for Auth0 User ID {Auth0UserId} attempting to remove members from group {GroupId}.", auth0UserId, req.GroupId);
			var forbiddenProblem = TypedResults.Problem(title: "Forbidden", detail: "User profile not found or inactive.", statusCode: StatusCodes.Status403Forbidden);
			await SendResultAsync(forbiddenProblem);
			return;
		}

		if (!currentUserInfo.IsSystemAdmin)
		{
			var isScorer = await connection.QuerySingleOrDefaultAsync<bool>(
				"SELECT TRUE FROM group_members WHERE group_id = @GroupId AND golfer_id = @GolferId AND is_scorer = TRUE;",
				new { req.GroupId, GolferId = currentUserInfo.Id });
			if (!isScorer)
			{
				logger.LogWarning("User {Auth0UserId} (GolferId: {GolferId}) is not an admin and not a scorer for group {GroupId}. Attempt to remove members denied.",
					auth0UserId, currentUserInfo.Id, req.GroupId);
				var forbiddenProblem = TypedResults.Problem(title: "Forbidden", detail: "User is not authorized to remove members from this group.", statusCode: StatusCodes.Status403Forbidden);
				await SendResultAsync(forbiddenProblem);
				return;
			}
		}
		logger.LogInformation("User {Auth0UserId} (GolferId: {GolferId}) authorized to remove members from group {GroupId}.",
			auth0UserId, currentUserInfo.Id, req.GroupId);

		// Note: Group existence was already checked by the validator.

		int successfullyRemovedCount = 0;

		if (distinctGolferIdsToRemove.Count != 0)
		{
			await using var transaction = await connection.BeginTransactionAsync(ct);
			try
			{
				// Hard delete from the group_members table
				const string deleteMembersSql = @"
                    DELETE FROM group_members
                    WHERE group_id = @GroupId AND golfer_id = ANY(@GolferIds);";

				successfullyRemovedCount = await connection.ExecuteAsync(deleteMembersSql,
					new { req.GroupId, GolferIds = distinctGolferIdsToRemove },
					transaction);

				await transaction.CommitAsync(ct);
			}
			catch (Exception ex)
			{
				await transaction.RollbackAsync(ct);
				logger.LogError(ex, "Error during transaction while removing golfers from group {GroupId}.", req.GroupId);
				var errorProblem = TypedResults.Problem(title: "Database Error", detail: "An error occurred while removing golfers from the group.", statusCode: StatusCodes.Status500InternalServerError);
				await SendResultAsync(errorProblem);
				return;
			}
		}

		var message = $"{successfullyRemovedCount} out of {distinctGolferIdsToRemove.Count} requested golfer(s) were removed from the group.";
		if (successfullyRemovedCount < distinctGolferIdsToRemove.Count)
		{
			message += " Some golfers requested for removal may not have been members.";
		}

		var response = new RemoveGolfersFromGroupResponse(
			Message: message,
			GroupId: req.GroupId,
			RequestedToRemoveCount: distinctGolferIdsToRemove.Count,
			SuccessfullyRemovedCount: successfullyRemovedCount
		);

		await SendOkAsync(response, ct); // 200 OK with summary, or could be 204 if no body.
	}
}