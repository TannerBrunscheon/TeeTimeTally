using Dapper;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc; // For [FromRoute]
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Endpoints.Groups.GroupManagement;

// --- DTOs for this endpoint ---
public class AddGolfersToGroupRequest
{
	[FromRoute]
	public Guid GroupId { get; set; }
	public List<Guid> GolferIds { get; set; } = new();
}

public record AddGolfersToGroupResponse(
	string Message,
	Guid GroupId,
	int GolfersRequestedCount,
	int GolfersSuccessfullyAddedCount,
	List<Guid> GolfersAlreadyMembers
);

// Helper for fetching current user's golfer ID and admin status
file record CurrentUserGolferInfo(Guid Id, bool IsSystemAdmin);
// Helper for checking if user is a scorer for the group
file record GroupScorerCheck(bool IsScorer);

// --- Fluent Validator for AddGolfersToGroupRequest ---
public class AddGolfersToGroupRequestValidator : Validator<AddGolfersToGroupRequest>
{
	private readonly NpgsqlDataSource _dataSource;

	public AddGolfersToGroupRequestValidator(NpgsqlDataSource dataSource)
	{
		_dataSource = dataSource;

		RuleFor(x => x.GroupId)
			.NotEmpty().WithMessage("GroupId is required.")
			.MustAsync(async (groupId, cancellationToken) => await GroupExistsAndIsActiveAsync(groupId, cancellationToken))
			.WithMessage(x => $"Group with ID '{x.GroupId}' not found or is inactive.");

		RuleFor(x => x.GolferIds)
			.NotEmpty().WithMessage("At least one GolferId must be provided.")
			.Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("GolferIds list contains duplicates.")
			.MustAsync(async (golferIds, cancellationToken) => await AllGolfersExistAndAreActiveAsync(golferIds, cancellationToken))
			.WithMessage("One or more provided GolferIds do not correspond to existing, active golfers.");
	}

	private async Task<bool> GroupExistsAndIsActiveAsync(Guid groupId, CancellationToken token)
	{
		if (groupId == Guid.Empty) return false;
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		return await connection.ExecuteScalarAsync<bool>(
			"SELECT EXISTS (SELECT 1 FROM groups WHERE id = @GroupId AND is_deleted = FALSE)",
			new { GroupId = groupId });
	}

	private async Task<bool> AllGolfersExistAndAreActiveAsync(List<Guid> golferIds, CancellationToken token)
	{
		if (golferIds == null || golferIds.Count == 0) return true; // Handled by NotEmpty rule for the list itself
		var distinctIds = golferIds.Distinct().ToList();
		if (distinctIds.Count == 0) return true;

		await using var connection = await _dataSource.OpenConnectionAsync(token);
		var existingActiveCount = await connection.ExecuteScalarAsync<int>(
			"SELECT COUNT(DISTINCT id) FROM golfers WHERE id = ANY(@GolferIds) AND is_deleted = FALSE;",
			new { GolferIds = distinctIds });
		return existingActiveCount == distinctIds.Count;
	}
}


[FastEndpoints.HttpPost("/groups/{GroupId:guid}/members"), Authorize(Policy = Auth0Scopes.ManageGroupMembers)]
public class AddGolfersToGroupEndpoint(NpgsqlDataSource dataSource, ILogger<AddGolfersToGroupEndpoint> logger)
	: Endpoint<AddGolfersToGroupRequest, AddGolfersToGroupResponse>
{
	public override async Task HandleAsync(AddGolfersToGroupRequest req, CancellationToken ct)
	{
		// Request DTO validation (GroupId exists, GolferIds exist & active) is now handled by AddGolfersToGroupRequestValidator.
		// If validation fails, FastEndpoints returns a 400 Bad Request automatically.

		var distinctGolferIds = req.GolferIds.Distinct().ToList(); // Already validated for presence by validator
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

		if (string.IsNullOrEmpty(auth0UserId)) // Should be caught by [Authorize] but defensive check
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
			logger.LogWarning("No active golfer profile found for Auth0 User ID {Auth0UserId} attempting to add members to group {GroupId}.", auth0UserId, req.GroupId);
			var forbiddenProblem = TypedResults.Problem(title: "Forbidden", detail: "User profile not found or inactive.", statusCode: StatusCodes.Status403Forbidden);
			await SendResultAsync(forbiddenProblem);
			return;
		}

		if (!currentUserInfo.IsSystemAdmin)
		{
			var scorerCheck = await connection.QuerySingleOrDefaultAsync<GroupScorerCheck>(
				"SELECT is_scorer AS IsScorer FROM group_members WHERE group_id = @GroupId AND golfer_id = @GolferId;",
				new { req.GroupId, GolferId = currentUserInfo.Id });

			if (scorerCheck == null || !scorerCheck.IsScorer)
			{
				logger.LogWarning("User {UserId} (GolferId: {GolferId}) is not an admin and not a scorer for group {GroupId}. Attempt to add members denied.",
					auth0UserId, currentUserInfo.Id, req.GroupId);
				var forbiddenProblem = TypedResults.Problem(title: "Forbidden", detail: "User is not authorized to add members to this group.", statusCode: StatusCodes.Status403Forbidden);
				await SendResultAsync(forbiddenProblem);
				return;
			}
		}
		logger.LogInformation("User {UserId} (GolferId: {GolferId}, IsAdmin: {IsAdmin}) authorized to add members to group {GroupId}.",
			auth0UserId, currentUserInfo.Id, currentUserInfo.IsSystemAdmin, req.GroupId);

		var golfersSuccessfullyAddedCount = 0;
		var golfersAlreadyMembers = new List<Guid>();

		if (distinctGolferIds.Count != 0)
		{
			await using var transaction = await connection.BeginTransactionAsync(ct);
			try
			{
				const string insertMemberSql = @"
                    INSERT INTO group_members (group_id, golfer_id, is_scorer, joined_at)
                    VALUES (@GroupId, @GolferId, FALSE, NOW()) 
                    ON CONFLICT (group_id, golfer_id) DO NOTHING;";

				foreach (var golferId in distinctGolferIds)
				{
					var rowsAffected = await connection.ExecuteAsync(insertMemberSql,
						new { req.GroupId, GolferId = golferId },
						transaction);

					if (rowsAffected > 0)
					{
						golfersSuccessfullyAddedCount++;
					}
					else
					{
						golfersAlreadyMembers.Add(golferId);
					}
				}
				await transaction.CommitAsync(ct);
			}
			catch (Exception ex)
			{
				await transaction.RollbackAsync(ct);
				logger.LogError(ex, "Error during transaction while adding golfers to group {GroupId}.", req.GroupId);
				var errorProblem = TypedResults.Problem(title: "Database Error", detail: "An error occurred while adding golfers to the group.", statusCode: StatusCodes.Status500InternalServerError);
				await SendResultAsync(errorProblem);
				return;
			}
		}

		var message = $"Processed {distinctGolferIds.Count} unique golfer IDs. {golfersSuccessfullyAddedCount} added.";
		if (golfersAlreadyMembers.Count != 0) message += $" {golfersAlreadyMembers.Count} were already members.";
		var response = new AddGolfersToGroupResponse(
			Message: message,
			GroupId: req.GroupId,
			GolfersRequestedCount: req.GolferIds.Distinct().Count(), // Use original distinct count from request for reporting
			GolfersSuccessfullyAddedCount: golfersSuccessfullyAddedCount,
			GolfersAlreadyMembers: golfersAlreadyMembers
		);

		var finalResponse = new AddGolfersToGroupResponse(
			Message: message,
			GroupId: req.GroupId,
			GolfersRequestedCount: req.GolferIds.Distinct().Count(),
			GolfersSuccessfullyAddedCount: golfersSuccessfullyAddedCount,
			GolfersAlreadyMembers: golfersAlreadyMembers
		);


		await SendOkAsync(finalResponse, ct);
	}
}