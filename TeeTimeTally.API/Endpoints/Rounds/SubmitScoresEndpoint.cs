using Dapper;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.API.Models;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Features.Rounds.Endpoints.SubmitScores;

// --- DTOs for this endpoint ---
public record TeamHoleScoreRequest(
	Guid TeamId,
	int HoleNumber,
	int Score
);

public class SubmitScoresRequest
{
	[FromRoute]
	public Guid RoundId { get; set; }

	public List<TeamHoleScoreRequest> ScoresToSubmit { get; set; } = new();
}

public record SubmitScoresResponse(
	string Message,
	Guid RoundId,
	int ScoresProcessedSuccessfully,
	string? RoundStatusAfterSubmit // New field to indicate if round became "Completed"
);

file record RoundValidationInfo(Guid Id, string Status, Guid GroupId);
file record TeamInRoundInfo(Guid TeamId, string TeamNameOrNumber);
file record StoredScoreInfo(Guid TeamId, int HoleNumber, int Score);


// --- Fluent Validator for SubmitScoresRequest ---
public class SubmitScoresRequestValidator : Validator<SubmitScoresRequest>
{
	private readonly NpgsqlDataSource _dataSource;

	public SubmitScoresRequestValidator(NpgsqlDataSource dataSource)
	{
		_dataSource = dataSource;

		RuleFor(x => x.RoundId)
			.NotEmpty().WithMessage("RoundId is required.")
			.MustAsync(async (roundId, cancellationToken) => await RoundExistsAndIsOpenForScoringAsync(roundId, cancellationToken))
			.WithMessage(req => $"Round with ID '{req.RoundId}' not found, not in a state for score entry (must be PendingSetup, SetupComplete, or InProgress), or is deleted.");

		RuleFor(x => x.ScoresToSubmit)
			.NotEmpty().WithMessage("At least one score entry must be provided.")
			.Must(scores =>
			{
				if (scores == null) return true;
				var distinctEntries = scores.Select(s => new { s.TeamId, s.HoleNumber }).Distinct().Count();
				return distinctEntries == scores.Count;
			}).WithMessage("Duplicate team/hole score entries found in the request.");

		RuleForEach(x => x.ScoresToSubmit).ChildRules(scoreEntry =>
		{
			scoreEntry.RuleFor(s => s.TeamId).NotEmpty();
			scoreEntry.RuleFor(s => s.HoleNumber).InclusiveBetween(1, 18);
			scoreEntry.RuleFor(s => s.Score).GreaterThan(0).WithMessage("Score must be a positive value.");
		});

		RuleFor(x => x)
			.CustomAsync(async (req, context, cancellationToken) =>
			{
				if (req.RoundId == Guid.Empty || req.ScoresToSubmit == null || req.ScoresToSubmit.Count == 0)
				{
					return;
				}

				var teamsInRound = (await GetTeamsInRoundAsync(req.RoundId, cancellationToken)).ToHashSet();
				if (teamsInRound.Count == 0 && req.ScoresToSubmit.Count != 0)
				{
					context.AddFailure(nameof(req.RoundId), "No teams found for the specified round, cannot submit scores.");
					return;
				}

				foreach (var scoreEntry in req.ScoresToSubmit)
				{
					if (!teamsInRound.Contains(scoreEntry.TeamId))
					{
						context.AddFailure(nameof(scoreEntry.TeamId), $"Team ID '{scoreEntry.TeamId}' in scores list is not a valid team for round '{req.RoundId}'.");
					}
				}
			});
	}

	private async Task<bool> RoundExistsAndIsOpenForScoringAsync(Guid roundId, CancellationToken token)
	{
		if (roundId == Guid.Empty) return false;
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		var roundStatus = await connection.QuerySingleOrDefaultAsync<string>(
			"SELECT status::TEXT FROM rounds WHERE id = @RoundId AND is_deleted = FALSE",
			new { RoundId = roundId });

		if (roundStatus == null) return false;
		return roundStatus == "PendingSetup" || roundStatus == "SetupComplete" || roundStatus == "InProgress";
	}

	private async Task<IEnumerable<Guid>> GetTeamsInRoundAsync(Guid roundId, CancellationToken token)
	{
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		return await connection.QueryAsync<Guid>(
			"SELECT id FROM round_teams WHERE round_id = @RoundId AND is_deleted = FALSE;",
			new { RoundId = roundId });
	}
}


[FastEndpoints.HttpPost("/rounds/{RoundId:guid}/scores"), Authorize(Policy = Auth0Scopes.ManageRoundScores)]
public class SubmitScoresEndpoint(NpgsqlDataSource dataSource, ILogger<SubmitScoresEndpoint> logger) : Endpoint<SubmitScoresRequest, SubmitScoresResponse>
{
	public override async Task HandleAsync(SubmitScoresRequest req, CancellationToken ct)
	{
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(auth0UserId))
		{
			var unauthorizedProblem = TypedResults.Problem(title: "Unauthorized", detail: "User identifier not found.", statusCode: StatusCodes.Status401Unauthorized);
			await SendResultAsync(unauthorizedProblem);
			return;
		}

		await using var connection = await dataSource.OpenConnectionAsync(ct);

		var roundValidationInfo = await connection.QuerySingleOrDefaultAsync<RoundValidationInfo>(
			 "SELECT id AS Id, status::TEXT AS Status, group_id AS GroupId FROM rounds WHERE id = @RoundId AND is_deleted = FALSE;", new { req.RoundId });

		if (roundValidationInfo == null) // Should be caught by validator
		{
			await SendNotFoundAsync(ct);
			return;
		}
		if (roundValidationInfo.Status == "Completed" || roundValidationInfo.Status == "Finalized")
		{
			await SendResultAsync(TypedResults.Problem(title: "Conflict", detail: $"Scores cannot be submitted for a round that is already '{roundValidationInfo.Status}'.", statusCode: StatusCodes.Status409Conflict));
			return;
		}


		var currentUserInfo = await connection.QuerySingleOrDefaultAsync<CurrentUserGolferInfo>(
			"SELECT id AS Id, is_system_admin AS IsSystemAdmin FROM golfers WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;",
			new { Auth0UserId = auth0UserId });

		if (currentUserInfo == null)
		{
			var forbiddenProblem = TypedResults.Problem(title: "Forbidden", detail: "User profile not found or inactive.", statusCode: StatusCodes.Status403Forbidden);
			await SendResultAsync(forbiddenProblem);
			return;
		}

		if (!currentUserInfo.IsSystemAdmin)
		{
			var isScorer = await connection.QuerySingleOrDefaultAsync<bool>(
				"SELECT TRUE FROM group_members WHERE group_id = @GroupId AND golfer_id = @GolferId AND is_scorer = TRUE;",
				new { roundValidationInfo.GroupId, GolferId = currentUserInfo.Id });
			if (!isScorer)
			{
				var forbiddenProblem = TypedResults.Problem(title: "Forbidden", detail: "User is not authorized to submit scores for this round.", statusCode: StatusCodes.Status403Forbidden);
				await SendResultAsync(forbiddenProblem);
				return;
			}
		}
		logger.LogInformation("User {Auth0UserId} (GolferId: {GolferId}) authorized to submit scores for round {RoundId}.", auth0UserId, currentUserInfo.Id, req.RoundId);

		int scoresProcessedCount = 0;
		string newRoundStatus = roundValidationInfo.Status; // Start with current status

		await using var transaction = await connection.BeginTransactionAsync(ct);
		try
		{
			const string upsertScoreSql = @"
                INSERT INTO round_scores (round_id, round_team_id, hole_number, score, entered_at, is_skin_winner, skin_value_won)
                VALUES (@RoundId, @TeamId, @HoleNumber, @Score, NOW(), FALSE, 0.00)
                ON CONFLICT (round_team_id, hole_number) 
                DO UPDATE SET
                    score = EXCLUDED.score,
                    entered_at = NOW(),
                    is_skin_winner = FALSE, 
                    skin_value_won = 0.00;";

			foreach (var scoreEntry in req.ScoresToSubmit)
			{
				var rowsAffected = await connection.ExecuteAsync(upsertScoreSql, new
				{
					req.RoundId,
					scoreEntry.TeamId,
					scoreEntry.HoleNumber,
					scoreEntry.Score
				}, transaction);
				if (rowsAffected > 0) scoresProcessedCount++;
			}

			// Update round's updated_at timestamp
			await connection.ExecuteAsync("UPDATE rounds SET updated_at = NOW() WHERE id = @RoundId", new { req.RoundId }, transaction);

			// Check for scorecard completeness
			var teamsInRound = (await connection.QueryAsync<TeamInRoundInfo>(
				"SELECT id AS TeamId, team_name_or_number AS TeamNameOrNumber FROM round_teams WHERE round_id = @RoundId AND is_deleted = FALSE;",
				new { req.RoundId }, transaction)).ToList();

			if (teamsInRound.Count != 0) // Only check completeness if there are teams
			{
				var allScoresFromDb = (await connection.QueryAsync<StoredScoreInfo>(
					"SELECT round_team_id AS TeamId, hole_number AS HoleNumber FROM round_scores WHERE round_id = @RoundId;",
					new { req.RoundId }, transaction)).ToList();

				var scoresGroupedByTeam = allScoresFromDb
					.GroupBy(s => s.TeamId)
					.ToDictionary(g => g.Key, g => g.Select(s => s.HoleNumber).ToHashSet());

				bool allTeamsComplete = true;
				foreach (var team in teamsInRound)
				{
					if (!scoresGroupedByTeam.TryGetValue(team.TeamId, out var holesScored) || holesScored.Count != 18)
					{
						allTeamsComplete = false;
						logger.LogInformation("Scorecard for team {TeamName} (ID: {TeamId}) in round {RoundId} is incomplete ({HoleCount}/18).",
							team.TeamNameOrNumber, team.TeamId, req.RoundId, holesScored?.Count ?? 0);
						break;
					}
				}

				if (allTeamsComplete && roundValidationInfo.Status != "Completed" && roundValidationInfo.Status != "Finalized")
				{
					const string updateStatusToCompletedSql = @"
                        UPDATE rounds SET status = 'Completed', updated_at = NOW() 
                        WHERE id = @RoundId AND status <> 'Completed' AND status <> 'Finalized';";
					await connection.ExecuteAsync(updateStatusToCompletedSql, new { req.RoundId }, transaction);
					newRoundStatus = "Completed";
					logger.LogInformation("All scores entered for round {RoundId}. Status updated to 'Completed'.", req.RoundId);
				}
			}
			else
			{
				logger.LogInformation("No teams found for round {RoundId}, skipping completeness check.", req.RoundId);
			}


			await transaction.CommitAsync(ct);
		}
		catch (Exception ex)
		{
			await transaction.RollbackAsync(ct);
			logger.LogError(ex, "Error submitting/updating scores for round {RoundId}.", req.RoundId);
			var errorProblem = TypedResults.Problem(title: "Database Error", detail: "An error occurred while saving scores.", statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		var response = new SubmitScoresResponse(
			Message: $"{scoresProcessedCount} score(s) processed successfully for round {req.RoundId}. Round status: {newRoundStatus}.",
			RoundId: req.RoundId,
			ScoresProcessedSuccessfully: scoresProcessedCount,
			RoundStatusAfterSubmit: newRoundStatus
		);

		await SendOkAsync(response, ct);
	}
}
