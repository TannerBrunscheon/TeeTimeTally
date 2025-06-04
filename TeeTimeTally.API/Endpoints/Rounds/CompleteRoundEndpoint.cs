using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc; // For FromRoute
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.Shared.Auth;
using Dapper;
using Microsoft.AspNetCore.Http; // For StatusCodes and TypedResults
using Microsoft.Extensions.Logging; // For ILogger
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using FluentValidation;
using TeeTimeTally.API.Models;

namespace TeeTimeTally.API.Features.Rounds.Endpoints.CompleteRound;

// --- DTOs for this endpoint ---

public class CompleteRoundRequest
{
	[FromRoute]
	public Guid RoundId { get; set; }

	// TeamScores list is removed. Endpoint assumes scores are already in DB and round is "Completed".
	public Guid CthWinnerGolferId { get; set; }
	public Guid? OverallWinnerTeamIdOverride { get; set; } // Made nullable for optional override
}

// --- Response DTOs ---
public record SkinPayoutDetailResponse(Guid TeamId, string TeamName, int HoleNumber, decimal AmountWon, bool IsCarryOverWin);
public record CthPayoutDetailResponse(Guid WinningGolferId, string WinningGolferName, Guid WinningTeamId, string WinningTeamName, decimal Amount);
public record OverallWinnerPayoutResponse(Guid TeamId, string TeamName, decimal Amount);

// DTO for the 409 Conflict response when there's a tie for overall winner
public record TiedOverallWinnerInfo(Guid TeamId, string TeamNameOrNumber);


public record CompleteRoundResponse(
	Guid RoundId,
	string FinalStatus, // Will be "Finalized"
	decimal TotalPot,
	List<SkinPayoutDetailResponse> SkinPayouts,
	decimal TotalSkinsPaidOut,
	decimal FinalSkinRolloverAmount,
	CthPayoutDetailResponse? CthPayout,
	List<OverallWinnerPayoutResponse> OverallWinnerPayouts, // Will contain one winner if finalized
	decimal TotalOverallWinnerPayout,
	string PayoutVerificationMessage
);

// --- Helper DTOs/Records for internal logic ---
file record RoundDetailsForCompletion(
	Guid Id, Guid GroupId, Guid CourseId, Guid FinancialConfigurationId, string Status,
	int NumPlayers, decimal TotalPot, decimal CalculatedSkinValuePerHole, decimal CalculatedCthPayout
);
file record TeamInfoForRound(Guid Id, string TeamNameOrNumber);
file record ParticipantInfoForRound(Guid GolferId, string FullName, Guid TeamId);
file record StoredScoreInfo(Guid TeamId, int HoleNumber, int Score);
file record TeamTotalScore(Guid TeamId, string TeamNameOrNumber, int TotalScore);


// --- Fluent Validator for CompleteRoundRequest ---
public class CompleteRoundRequestValidator : Validator<CompleteRoundRequest>
{
	private readonly NpgsqlDataSource _dataSource;

	public CompleteRoundRequestValidator(NpgsqlDataSource dataSource)
	{
		_dataSource = dataSource;

		RuleFor(x => x.RoundId)
			.NotEmpty().WithMessage("RoundId is required.")
			.MustAsync(async (roundId, cancellationToken) => await RoundExistsAndIsReadyForFinalizationAsync(roundId, cancellationToken))
			.WithMessage(req => $"Round with ID '{req.RoundId}' not found, not in 'Completed' status, or is deleted.");

		RuleFor(x => x.CthWinnerGolferId)
			.NotEmpty().WithMessage("CTH Winner Golfer ID is required.")
			.MustAsync(async (req, cthWinnerId, context, cancellationToken) =>
				await IsGolferParticipantInRoundAsync(req.RoundId, cthWinnerId, cancellationToken))
			.WithMessage("CTH winner is not a valid participant in this round.");

		// OverallWinnerTeamIdOverride is optional, its validity (if provided) will be checked in the handler.
	}

	private async Task<bool> RoundExistsAndIsReadyForFinalizationAsync(Guid roundId, CancellationToken token)
	{
		if (roundId == Guid.Empty) return false;
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		var roundStatus = await connection.QuerySingleOrDefaultAsync<string>(
			"SELECT status::TEXT FROM rounds WHERE id = @RoundId AND is_deleted = FALSE",
			new { RoundId = roundId });

		if (roundStatus == null) return false;
		return roundStatus == "Completed";
	}

	private async Task<bool> IsGolferParticipantInRoundAsync(Guid roundId, Guid golferId, CancellationToken token)
	{
		if (golferId == Guid.Empty) return false;
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		return await connection.ExecuteScalarAsync<bool>(
			"SELECT EXISTS (SELECT 1 FROM round_participants rp JOIN golfers g ON rp.golfer_id = g.id WHERE rp.round_id = @RoundId AND rp.golfer_id = @GolferId AND g.is_deleted = FALSE);",
			new { RoundId = roundId, GolferId = golferId });
	}
}


[FastEndpoints.HttpPost("/rounds/{RoundId:guid}/complete"), Authorize(Policy = Auth0Scopes.FinalizeRounds)]
public class CompleteRoundEndpoint(NpgsqlDataSource dataSource, ILogger<CompleteRoundEndpoint> logger)
	: Endpoint<CompleteRoundRequest, CompleteRoundResponse>
{
	public override async Task HandleAsync(CompleteRoundRequest req, CancellationToken ct)
	{
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(auth0UserId))
		{
			await SendResultAsync(TypedResults.Problem(title: "Unauthorized", detail: "User identifier not found.", statusCode: StatusCodes.Status401Unauthorized));
			return;
		}

		await using var connection = await dataSource.OpenConnectionAsync(ct);

		var currentUserInfo = await connection.QuerySingleOrDefaultAsync<CurrentUserGolferInfo>(
			"SELECT id AS Id, is_system_admin AS IsSystemAdmin FROM golfers WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;",
			new { Auth0UserId = auth0UserId });

		if (currentUserInfo == null)
		{
			await SendResultAsync(TypedResults.Problem(title: "Forbidden", detail: "User profile not found or inactive.", statusCode: StatusCodes.Status403Forbidden));
			return;
		}

		var roundDetails = await connection.QuerySingleOrDefaultAsync<RoundDetailsForCompletion>(@"
            SELECT r.id AS Id, r.group_id AS GroupId, r.course_id AS CourseId, r.financial_configuration_id AS FinancialConfigurationId,
                   r.status AS Status, r.num_players AS NumPlayers, r.total_pot AS TotalPot,
                   r.calculated_skin_value_per_hole AS CalculatedSkinValuePerHole, r.calculated_cth_payout AS CalculatedCthPayout
            FROM rounds r
            WHERE r.id = @RoundId AND r.is_deleted = FALSE;",
			new { req.RoundId });

		if (roundDetails == null)
		{
			await SendNotFoundAsync(ct);
			return;
		}
		// Validator ensures status is 'Completed'. This is a defense-in-depth check.
		if (roundDetails.Status != "Completed")
		{
			logger.LogWarning("Attempt to finalize round {RoundId} which is not in 'Completed' status. Current status: {Status}", req.RoundId, roundDetails.Status);
			await SendResultAsync(TypedResults.Problem(title: "Conflict", detail: $"Round must be in 'Completed' status to be finalized. Current status: {roundDetails.Status}.", statusCode: StatusCodes.Status409Conflict));
			return;
		}

		if (!currentUserInfo.IsSystemAdmin)
		{
			var isScorer = await connection.QuerySingleOrDefaultAsync<bool>(
				"SELECT TRUE FROM group_members WHERE group_id = @GroupId AND golfer_id = @GolferId AND is_scorer = TRUE;",
				new { roundDetails.GroupId, GolferId = currentUserInfo.Id });
			if (!isScorer)
			{
				await SendResultAsync(TypedResults.Problem(title: "Forbidden", detail: "User is not authorized to complete this round.", statusCode: StatusCodes.Status403Forbidden));
				return;
			}
		}
		logger.LogInformation("User {Auth0UserId} (GolferId: {GolferId}) authorized to finalize round {RoundId}.", auth0UserId, currentUserInfo.Id, req.RoundId);

		var teamsInRoundData = (await connection.QueryAsync<TeamInfoForRound>(
			"SELECT id AS Id, team_name_or_number AS TeamNameOrNumber FROM round_teams WHERE round_id = @RoundId AND is_deleted = FALSE;",
			new { req.RoundId })).ToList();
		var teamsInRound = teamsInRoundData.ToDictionary(t => t.Id);


		var participantsInRound = (await connection.QueryAsync<ParticipantInfoForRound>(
			"SELECT rp.golfer_id AS GolferId, g.full_name AS FullName, rp.round_team_id AS TeamId FROM round_participants rp JOIN golfers g ON rp.golfer_id = g.id WHERE rp.round_id = @RoundId AND g.is_deleted = FALSE;",
			new { req.RoundId })).ToDictionary(p => p.GolferId);

		var allScoresFromDb = (await connection.QueryAsync<StoredScoreInfo>(
			"SELECT round_team_id AS TeamId, hole_number AS HoleNumber, score AS Score FROM round_scores WHERE round_id = @RoundId;",
			new { req.RoundId })).ToList();

		// Scorecard completeness should be guaranteed by the "Completed" status.
		// Defensive check (optional, but good for data integrity):
		var scoresGroupedByTeamForValidation = allScoresFromDb.GroupBy(s => s.TeamId).ToDictionary(g => g.Key, g => g.Select(s => s.HoleNumber).ToHashSet());
		foreach (var teamEntry in teamsInRound)
		{
			if (!scoresGroupedByTeamForValidation.TryGetValue(teamEntry.Key, out var holesScored) || holesScored.Count != 18)
			{
				logger.LogError("Critical: Round {RoundId} is 'Completed' but team {TeamId} has incomplete scores ({ScoreCount}/18). Data integrity issue.", req.RoundId, teamEntry.Key, holesScored?.Count ?? 0);
				await SendResultAsync(TypedResults.Problem(title: "Internal Server Error", detail: "Data integrity issue: Round marked as completed but scorecard is incomplete.", statusCode: StatusCodes.Status500InternalServerError));
				return;
			}
		}
		logger.LogInformation("All teams have complete scorecards for round {RoundId}. Proceeding to finalization.", req.RoundId);

		// --- Calculate Overall Winner ---
		var teamTotalScores = allScoresFromDb
			.GroupBy(s => s.TeamId)
			.Select(g => new TeamTotalScore(
				TeamId: g.Key,
				TeamNameOrNumber: teamsInRound.TryGetValue(g.Key, out var teamInfo) ? teamInfo.TeamNameOrNumber : "Unknown Team",
				TotalScore: g.Sum(s => s.Score)
			))
			.OrderBy(ts => ts.TotalScore) // Lowest score wins
			.ToList();

		List<Guid> actualOverallWinnerTeamIds = [];

		if (req.OverallWinnerTeamIdOverride.HasValue)
		{
			var overrideTeamId = req.OverallWinnerTeamIdOverride.Value;
			if (!teamsInRound.TryGetValue(overrideTeamId, out TeamInfoForRound? value))
			{
				await SendResultAsync(TypedResults.Problem(title: "Bad Request", detail: $"Provided OverallWinnerTeamIdOverride '{overrideTeamId}' is not a valid team in this round.", statusCode: StatusCodes.Status400BadRequest));
				return;
			}
			// Validate that the override is one of the teams that *could* have won (i.e., had the lowest score)
			var lowestScore = teamTotalScores.FirstOrDefault()?.TotalScore;
			if (lowestScore == null || !teamTotalScores.Any(t => t.TeamId == overrideTeamId && t.TotalScore == lowestScore))
			{
				await SendResultAsync(TypedResults.Problem(title: "Bad Request", detail: $"Override winner '{value.TeamNameOrNumber}' did not have the lowest score.", statusCode: StatusCodes.Status400BadRequest));
				return;
			}
			actualOverallWinnerTeamIds.Add(overrideTeamId);
			logger.LogInformation("Overall winner for round {RoundId} set by override to TeamId: {OverrideTeamId}", req.RoundId, overrideTeamId);
		}
		else
		{
			if (teamTotalScores.Count == 0)
			{
				// Should not happen if there are teams and scores
				await SendResultAsync(TypedResults.Problem(title: "Internal Server Error", detail: "Cannot determine overall winner: no team scores found.", statusCode: StatusCodes.Status500InternalServerError));
				return;
			}
			var lowestScore = teamTotalScores.First().TotalScore;
			var tiedTeams = teamTotalScores.Where(ts => ts.TotalScore == lowestScore).ToList();

			if (tiedTeams.Count > 1)
			{
				logger.LogWarning("Tie for overall winner in round {RoundId} between teams: {TiedTeamIds}. Override required.", req.RoundId, string.Join(", ", tiedTeams.Select(t => t.TeamId)));
				var tiedTeamInfos = tiedTeams.Select(t => new TiedOverallWinnerInfo(t.TeamId, t.TeamNameOrNumber)).ToList();
				var problemDetails = new ValidationProblemDetails(new Dictionary<string, string[]>()) // No specific field, general problem
				{
					Title = "Overall Winner Tie",
					Detail = "Multiple teams are tied for the lowest score. An 'OverallWinnerTeamIdOverride' must be provided to finalize the round.",
					Status = StatusCodes.Status409Conflict,
					Extensions = { { "TiedTeams", tiedTeamInfos } }
				};
				await SendResultAsync(TypedResults.Problem(problemDetails));
				return;
			}
			actualOverallWinnerTeamIds.Add(tiedTeams.First().TeamId);
			logger.LogInformation("Overall winner for round {RoundId} determined: TeamId: {WinnerTeamId}", req.RoundId, actualOverallWinnerTeamIds.First());
		}


		// --- Start Main Finalization Transaction ---
		await using var finalizationTransaction = await connection.BeginTransactionAsync(ct);
		try
		{
			var scoresByHoleThenTeamForCalc = allScoresFromDb
				.GroupBy(s => s.HoleNumber)
				.OrderBy(g => g.Key)
				.ToDictionary(g => g.Key, g => g.ToDictionary(s => s.TeamId, s => s.Score));

			// 2. Skins Calculation
			var skinPayoutsResponse = new List<SkinPayoutDetailResponse>();
			decimal totalSkinsPaidOut = 0;
			decimal currentCarryOverSkinValue = 0;

			for (int holeNumber = 1; holeNumber <= 18; holeNumber++)
			{
				decimal baseSkinValueForThisHole = roundDetails.CalculatedSkinValuePerHole;
				decimal skinValueAtStake = baseSkinValueForThisHole + currentCarryOverSkinValue;
				currentCarryOverSkinValue = 0;

				if (!scoresByHoleThenTeamForCalc.TryGetValue(holeNumber, out var scoresForThisHole))
				{
					throw new InvalidOperationException($"Scores missing for hole {holeNumber} in a 'Completed' round.");
				}

				var minScoreOnHole = scoresForThisHole.Values.Min();
				var winningTeamIdsForHole = scoresForThisHole.Where(kvp => kvp.Value == minScoreOnHole).Select(kvp => kvp.Key).ToList();

				if (winningTeamIdsForHole.Count == 1)
				{
					var winningTeamId = winningTeamIdsForHole.First();
					await connection.ExecuteAsync(
						"UPDATE round_scores SET is_skin_winner = TRUE, skin_value_won = @Amount WHERE round_id = @RoundId AND round_team_id = @TeamId AND hole_number = @HoleNumber;",
						new { Amount = skinValueAtStake, req.RoundId, TeamId = winningTeamId, HoleNumber = holeNumber }, finalizationTransaction);

					skinPayoutsResponse.Add(new SkinPayoutDetailResponse(winningTeamId, teamsInRound[winningTeamId].TeamNameOrNumber, holeNumber, skinValueAtStake, skinValueAtStake > baseSkinValueForThisHole));
					totalSkinsPaidOut += skinValueAtStake;
				}
				else
				{
					currentCarryOverSkinValue = skinValueAtStake;
				}
			}
			decimal finalSkinRolloverAmount = currentCarryOverSkinValue;

			// 3. CTH Payout
			CthPayoutDetailResponse? cthPayoutResponse = null;
			decimal actualCthPaidOut = 0;
			if (participantsInRound.TryGetValue(req.CthWinnerGolferId, out var cthWinnerProfile) && roundDetails.CalculatedCthPayout > 0)
			{
				if (teamsInRound.TryGetValue(cthWinnerProfile.TeamId, out var cthWinningTeamInfo))
				{
					cthPayoutResponse = new CthPayoutDetailResponse(
						req.CthWinnerGolferId, cthWinnerProfile.FullName, cthWinningTeamInfo.Id, cthWinningTeamInfo.TeamNameOrNumber, roundDetails.CalculatedCthPayout);
					actualCthPaidOut = roundDetails.CalculatedCthPayout;
				}
			}

			// 4. Overall Winner Payout (using actualOverallWinnerTeamIds determined above)
			decimal overallWinnerPayoutPool = roundDetails.TotalPot - totalSkinsPaidOut - actualCthPaidOut;
			var overallWinnerPayoutsResponse = new List<OverallWinnerPayoutResponse>();
			decimal totalOverallWinnerPayoutPaid = 0;

			if (overallWinnerPayoutPool > 0 && actualOverallWinnerTeamIds.Count != 0)
			{
				// Since actualOverallWinnerTeamIds will have 1 entry after tie resolution/override
				decimal payoutPerWinningTeam = Math.Round(overallWinnerPayoutPool / actualOverallWinnerTeamIds.Count, 2, MidpointRounding.ToEven);

				foreach (var winnerTeamId in actualOverallWinnerTeamIds) // Should be just one
				{
					await connection.ExecuteAsync(
						"UPDATE round_teams SET is_overall_winner = TRUE WHERE id = @TeamId AND round_id = @RoundId;",
						new { TeamId = winnerTeamId, req.RoundId }, finalizationTransaction);

					overallWinnerPayoutsResponse.Add(new OverallWinnerPayoutResponse(winnerTeamId, teamsInRound[winnerTeamId].TeamNameOrNumber, payoutPerWinningTeam));
					totalOverallWinnerPayoutPaid += payoutPerWinningTeam;
				}
				if (Math.Abs(totalOverallWinnerPayoutPaid - overallWinnerPayoutPool) > 0.005m && actualOverallWinnerTeamIds.Count == 1)
				{ // Simpler check for single winner
					logger.LogWarning("Overall winner payout pool {Pool} vs paid {Paid} for round {RoundId} has a rounding difference.", overallWinnerPayoutPool, totalOverallWinnerPayoutPaid, req.RoundId);
				}
			}

			// 5. Final Update to Round Record (Set to Finalized)
			const string updateRoundToFinalizedSql = @"
                UPDATE rounds
                SET status = 'Finalized', 
                    cth_winner_golfer_id = @CthWinnerGolferId,
                    final_skin_rollover_amount = @FinalSkinRolloverAmount,
                    final_total_skins_payout = @FinalTotalSkinsPayout,
                    final_overall_winner_payout_amount = @FinalOverallWinnerPayoutAmount,
                    finalized_at = NOW(),
                    updated_at = NOW()
                WHERE id = @RoundId AND status = 'Completed';";
			var finalUpdateRowsAffected = await connection.ExecuteAsync(updateRoundToFinalizedSql, new
			{
				req.CthWinnerGolferId,
				FinalSkinRolloverAmount = finalSkinRolloverAmount,
				FinalTotalSkinsPayout = totalSkinsPaidOut,
				FinalOverallWinnerPayoutAmount = totalOverallWinnerPayoutPaid,
				req.RoundId
			}, finalizationTransaction);

			if (finalUpdateRowsAffected == 0)
			{
				await finalizationTransaction.RollbackAsync(ct);
				logger.LogError("Failed to finalize round {RoundId} as it was not in 'Completed' state during final update step, or already finalized.", req.RoundId);
				var stateConflictProblem = TypedResults.Problem(title: "Conflict", detail: "Round state was not 'Completed' during final update, or it was already finalized by another process.", statusCode: StatusCodes.Status409Conflict);
				await SendResultAsync(stateConflictProblem);
				return;
			}

			await finalizationTransaction.CommitAsync(ct);

			// 6. Verification Check
			decimal totalDistributed = totalSkinsPaidOut + actualCthPaidOut + totalOverallWinnerPayoutPaid + finalSkinRolloverAmount;
			string verificationMessage = $"Verification: Total Pot ({roundDetails.TotalPot:C2}). Distributed: Skins ({totalSkinsPaidOut:C2}) + CTH ({actualCthPaidOut:C2}) + Overall Winners ({totalOverallWinnerPayoutPaid:C2}) + Rollover ({finalSkinRolloverAmount:C2}) = {totalDistributed:C2}.";
			if (Math.Abs(totalDistributed - roundDetails.TotalPot) > 0.01m * roundDetails.NumPlayers)
			{
				logger.LogError("Payout verification FAILED for Round {RoundId}! Pot: {TotalPot}, Distributed: {TotalDistributed}. Difference: {Difference}",
					req.RoundId, roundDetails.TotalPot, totalDistributed, roundDetails.TotalPot - totalDistributed);
				verificationMessage += " DISCREPANCY DETECTED!";
			}
			else
			{
				verificationMessage += " BALANCED.";
			}

			var finalResponse = new CompleteRoundResponse(
				req.RoundId, "Finalized", roundDetails.TotalPot, skinPayoutsResponse, totalSkinsPaidOut,
				finalSkinRolloverAmount, cthPayoutResponse, overallWinnerPayoutsResponse, totalOverallWinnerPayoutPaid,
				verificationMessage
			);
			await SendOkAsync(finalResponse, ct);

		}
		catch (Exception ex)
		{
			if (finalizationTransaction.Connection != null && finalizationTransaction.Connection.State == System.Data.ConnectionState.Open)
			{
				try { await finalizationTransaction.RollbackAsync(ct); }
				catch (Exception rbEx) { logger.LogError(rbEx, "Exception during finalization transaction rollback for round {RoundId}", req.RoundId); }
			}
			logger.LogError(ex, "Error during finalization phase of completing round {RoundId}", req.RoundId);
			var errorProblem = TypedResults.Problem(title: "Internal Server Error", detail: "An unexpected error occurred while finalizing the round and calculating payouts.", statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
		}
	}
}
