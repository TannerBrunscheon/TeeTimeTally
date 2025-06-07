using Dapper;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using System.Text.Json;
using TeeTimeTally.API.Models;
using TeeTimeTally.Shared.Auth;

namespace TeeTimeTally.API.Features.Rounds.Endpoints.CompleteRound;

// --- DTOs for this endpoint ---

public class CompleteRoundRequest
{
	[FromRoute]
	public Guid RoundId { get; set; }
	public Guid CthWinnerGolferId { get; set; }
	public Guid? OverallWinnerTeamIdOverride { get; set; }
}

// --- Response DTOs ---
public record SkinPayoutDetailResponse(Guid TeamId, string TeamName, short HoleNumber, decimal AmountWon, bool IsCarryOverWin);
public record CthPayoutDetailResponse(Guid WinningGolferId, string WinningGolferName, Guid WinningTeamId, string WinningTeamName, decimal Amount);
public record OverallWinnerPayoutResponse(Guid TeamId, string TeamName, decimal Amount);
public record TiedOverallWinnerInfo(Guid TeamId, string TeamNameOrNumber);

public record PlayerPayoutSummaryResponse(
	Guid GolferId,
	string FullName,
	Guid TeamId,
	string TeamName,
	decimal TotalWinnings,
	PlayerPayoutBreakdown Breakdown
);

public record PlayerPayoutBreakdown(
	decimal SkinsWinnings,
	decimal CthWinnings,
	decimal OverallWinnings
);


public record CompleteRoundResponse(
	Guid RoundId,
	string FinalStatus,
	decimal TotalPot,
	List<SkinPayoutDetailResponse> SkinPayouts,
	decimal TotalSkinsPaidOut,
	decimal FinalSkinRolloverAmount,
	CthPayoutDetailResponse? CthPayout,
	List<OverallWinnerPayoutResponse> OverallWinnerPayouts,
	decimal TotalOverallWinnerPayout,
	List<PlayerPayoutSummaryResponse> PlayerPayouts,
	string PayoutVerificationMessage
);

// --- Helper DTOs/Records for internal logic ---
file record RoundDetailsForCompletion(
	Guid Id, Guid GroupId, Guid CourseId, Guid FinancialConfigurationId, string Status,
	short NumPlayers, decimal TotalPot, decimal CalculatedSkinValuePerHole, decimal CalculatedCthPayout
);
file record TeamInfoForRound(Guid Id, string TeamNameOrNumber);
file record ParticipantInfoForRound(Guid GolferId, string FullName, Guid TeamId);
file record StoredScoreInfo(Guid TeamId, short HoleNumber, short Score);
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

		var teamsInRoundData = (await connection.QueryAsync<TeamInfoForRound>(
			"SELECT id AS Id, team_name_or_number AS TeamNameOrNumber FROM round_teams WHERE round_id = @RoundId AND is_deleted = FALSE;",
			new { req.RoundId })).ToList();
		var teamsInRound = teamsInRoundData.ToDictionary(t => t.Id);

		var participants = (await connection.QueryAsync<ParticipantInfoForRound>(
			"SELECT rp.golfer_id AS GolferId, g.full_name AS FullName, rp.round_team_id AS TeamId FROM round_participants rp JOIN golfers g ON rp.golfer_id = g.id WHERE rp.round_id = @RoundId AND g.is_deleted = FALSE;",
			new { req.RoundId })).ToList();
		var participantsByTeam = participants.GroupBy(p => p.TeamId).ToDictionary(g => g.Key, g => g.ToList());
		var participantsById = participants.ToDictionary(p => p.GolferId);

		var allScoresFromDb = (await connection.QueryAsync<StoredScoreInfo>(
			"SELECT round_team_id AS TeamId, hole_number AS HoleNumber, score AS Score FROM round_scores WHERE round_id = @RoundId;",
			new { req.RoundId })).ToList();

		var teamTotalScores = allScoresFromDb
			.GroupBy(s => s.TeamId)
			.Select(g => new TeamTotalScore(
				TeamId: g.Key,
				TeamNameOrNumber: teamsInRound.TryGetValue(g.Key, out var teamInfo) ? teamInfo.TeamNameOrNumber : "Unknown Team",
				TotalScore: g.Sum(s => s.Score)
			))
			.OrderBy(ts => ts.TotalScore)
			.ToList();

		List<Guid> actualOverallWinnerTeamIds = [];

		if (req.OverallWinnerTeamIdOverride.HasValue)
		{
			var overrideTeamId = req.OverallWinnerTeamIdOverride.Value;
			if (!teamsInRound.ContainsKey(overrideTeamId))
			{
				await SendResultAsync(TypedResults.Problem(title: "Bad Request", detail: $"Provided OverallWinnerTeamIdOverride '{overrideTeamId}' is not a valid team in this round.", statusCode: StatusCodes.Status400BadRequest));
				return;
			}
			var lowestScore = teamTotalScores.FirstOrDefault()?.TotalScore;
			if (lowestScore == null || !teamTotalScores.Any(t => t.TeamId == overrideTeamId && t.TotalScore == lowestScore))
			{
				await SendResultAsync(TypedResults.Problem(title: "Bad Request", detail: $"Override winner did not have the lowest score.", statusCode: StatusCodes.Status400BadRequest));
				return;
			}
			actualOverallWinnerTeamIds.Add(overrideTeamId);
		}
		else
		{
			var lowestScore = teamTotalScores.First().TotalScore;
			var tiedTeams = teamTotalScores.Where(ts => ts.TotalScore == lowestScore).ToList();

			if (tiedTeams.Count > 1)
			{
				var tiedTeamInfos = tiedTeams.Select(t => new TiedOverallWinnerInfo(t.TeamId, t.TeamNameOrNumber)).ToList();
				var problemDetails = new ValidationProblemDetails(new Dictionary<string, string[]>())
				{
					Title = "Overall Winner Tie",
					Detail = "Multiple teams are tied for the lowest score. An 'OverallWinnerTeamIdOverride' must be provided to finalize the round.",
					Status = StatusCodes.Status409Conflict,
					Extensions = { { "tiedTeams", tiedTeamInfos } }
				};
				await SendResultAsync(TypedResults.Problem(problemDetails));
				return;
			}
			actualOverallWinnerTeamIds.Add(tiedTeams.First().TeamId);
		}

		await using var finalizationTransaction = await connection.BeginTransactionAsync(ct);
		try
		{
			var scoresByHoleThenTeamForCalc = allScoresFromDb.GroupBy(s => s.HoleNumber).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.ToDictionary(s => s.TeamId, s => s.Score));
			var skinPayoutsResponse = new List<SkinPayoutDetailResponse>();
			var perPlayerWinnings = participants.ToDictionary(p => p.GolferId, p => new PlayerPayoutBreakdown(0, 0, 0));
			decimal totalSkinsPaidOut = 0;
			decimal currentCarryOverSkinValue = 0;
			for (short holeNumber = 1; holeNumber <= 18; holeNumber++)
			{
				decimal skinValueAtStake = roundDetails.CalculatedSkinValuePerHole + currentCarryOverSkinValue;
				currentCarryOverSkinValue = 0;
				if (!scoresByHoleThenTeamForCalc.TryGetValue(holeNumber, out var scoresForThisHole)) continue;
				var minScoreOnHole = scoresForThisHole.Values.Min();
				var winningTeamIdsForHole = scoresForThisHole.Where(kvp => kvp.Value == minScoreOnHole).Select(kvp => kvp.Key).ToList();
				if (winningTeamIdsForHole.Count == 1)
				{
					var winningTeamId = winningTeamIdsForHole.First();
					await connection.ExecuteAsync("UPDATE round_scores SET is_skin_winner = TRUE, skin_value_won = @Amount WHERE round_id = @RoundId AND round_team_id = @TeamId AND hole_number = @HoleNumber;", new { Amount = skinValueAtStake, req.RoundId, TeamId = winningTeamId, HoleNumber = holeNumber }, finalizationTransaction);
					skinPayoutsResponse.Add(new SkinPayoutDetailResponse(winningTeamId, teamsInRound[winningTeamId].TeamNameOrNumber, holeNumber, skinValueAtStake, skinValueAtStake > roundDetails.CalculatedSkinValuePerHole));
					totalSkinsPaidOut += skinValueAtStake;
					var members = participantsByTeam[winningTeamId];
					foreach (var member in members)
					{
						perPlayerWinnings[member.GolferId] = perPlayerWinnings[member.GolferId] with { SkinsWinnings = perPlayerWinnings[member.GolferId].SkinsWinnings + (skinValueAtStake / members.Count) };
					}
				}
				else { currentCarryOverSkinValue = skinValueAtStake; }
			}
			decimal finalSkinRolloverAmount = currentCarryOverSkinValue;

			CthPayoutDetailResponse? cthPayoutResponse = null;
			decimal actualCthPaidOut = 0;
			if (participantsById.TryGetValue(req.CthWinnerGolferId, out var cthWinnerProfile) && roundDetails.CalculatedCthPayout > 0)
			{
				if (teamsInRound.TryGetValue(cthWinnerProfile.TeamId, out var cthWinningTeamInfo))
				{
					cthPayoutResponse = new CthPayoutDetailResponse(req.CthWinnerGolferId, cthWinnerProfile.FullName, cthWinningTeamInfo.Id, cthWinningTeamInfo.TeamNameOrNumber, roundDetails.CalculatedCthPayout);
					actualCthPaidOut = roundDetails.CalculatedCthPayout;

					// --- CORRECTED LOGIC ---
					// CTH winnings go ONLY to the CthWinnerGolferId, not the whole team.
					perPlayerWinnings[req.CthWinnerGolferId] = perPlayerWinnings[req.CthWinnerGolferId] with { CthWinnings = perPlayerWinnings[req.CthWinnerGolferId].CthWinnings + actualCthPaidOut };
				}
			}

			decimal overallWinnerPayoutPool = roundDetails.TotalPot - totalSkinsPaidOut - finalSkinRolloverAmount - actualCthPaidOut;
			var overallWinnerPayoutsResponse = new List<OverallWinnerPayoutResponse>();
			decimal totalOverallWinnerPayoutPaid = 0;
			if (overallWinnerPayoutPool > 0 && actualOverallWinnerTeamIds.Any())
			{
				decimal payoutPerWinningTeam = Math.Round(overallWinnerPayoutPool / actualOverallWinnerTeamIds.Count, 2, MidpointRounding.ToEven);
				foreach (var winnerTeamId in actualOverallWinnerTeamIds)
				{
					await connection.ExecuteAsync("UPDATE round_teams SET is_overall_winner = TRUE WHERE id = @TeamId AND round_id = @RoundId;", new { TeamId = winnerTeamId, req.RoundId }, finalizationTransaction);
					overallWinnerPayoutsResponse.Add(new OverallWinnerPayoutResponse(winnerTeamId, teamsInRound[winnerTeamId].TeamNameOrNumber, payoutPerWinningTeam));
					totalOverallWinnerPayoutPaid += payoutPerWinningTeam;
					var members = participantsByTeam[winnerTeamId];
					foreach (var member in members)
					{
						perPlayerWinnings[member.GolferId] = perPlayerWinnings[member.GolferId] with { OverallWinnings = perPlayerWinnings[member.GolferId].OverallWinnings + (payoutPerWinningTeam / members.Count) };
					}
				}
			}

			const string insertPayoutSummarySql = @"
                INSERT INTO round_payout_summary (round_id, golfer_id, team_id, total_winnings, breakdown, calculated_at)
                VALUES (@RoundId, @GolferId, @TeamId, @TotalWinnings, @Breakdown::jsonb, NOW());";
			foreach (var participant in participants)
			{
				var breakdown = perPlayerWinnings[participant.GolferId];
				var totalWinnings = breakdown.SkinsWinnings + breakdown.CthWinnings + breakdown.OverallWinnings;
				await connection.ExecuteAsync(insertPayoutSummarySql, new
				{
					req.RoundId,
					participant.GolferId,
					participant.TeamId,
					TotalWinnings = totalWinnings,
					Breakdown = JsonSerializer.Serialize(breakdown)
				}, finalizationTransaction);
			}

			const string updateRoundToFinalizedSql = @"
                UPDATE rounds SET status = 'Finalized'::round_status_enum, cth_winner_golfer_id = @CthWinnerGolferId, final_skin_rollover_amount = @FinalSkinRolloverAmount, final_total_skins_payout = @FinalTotalSkinsPayout, final_overall_winner_payout_amount = @FinalOverallWinnerPayoutAmount, finalized_at = NOW(), updated_at = NOW()
                WHERE id = @RoundId AND status = 'Completed'::round_status_enum;";
			await connection.ExecuteAsync(updateRoundToFinalizedSql, new { req.CthWinnerGolferId, FinalSkinRolloverAmount = finalSkinRolloverAmount, FinalTotalSkinsPayout = totalSkinsPaidOut, FinalOverallWinnerPayoutAmount = totalOverallWinnerPayoutPaid, req.RoundId }, finalizationTransaction);

			await finalizationTransaction.CommitAsync(ct);

			const string selectPayoutsSql = "SELECT golfer_id as GolferId, total_winnings as TotalWinnings, breakdown FROM round_payout_summary WHERE round_id = @RoundId;";
			var payoutSummariesFromDb = await connection.QueryAsync<(Guid GolferId, decimal TotalWinnings, string Breakdown)>(selectPayoutsSql, new { req.RoundId });

			var playerPayouts = payoutSummariesFromDb.Select(p => {
				var breakdown = JsonSerializer.Deserialize<PlayerPayoutBreakdown>(p.Breakdown) ?? new PlayerPayoutBreakdown(0, 0, 0);
				var participant = participantsById[p.GolferId];
				var team = teamsInRound[participant.TeamId];
				return new PlayerPayoutSummaryResponse(p.GolferId, participant.FullName, team.Id, team.TeamNameOrNumber, p.TotalWinnings, breakdown);
			}).ToList();


			decimal totalDistributed = totalSkinsPaidOut + actualCthPaidOut + totalOverallWinnerPayoutPaid + finalSkinRolloverAmount;
			string verificationMessage = $"Verification: Total Pot ({roundDetails.TotalPot:C2}). Distributed: Skins ({totalSkinsPaidOut:C2}) + CTH ({actualCthPaidOut:C2}) + Overall Winners ({totalOverallWinnerPayoutPaid:C2}) + Rollover ({finalSkinRolloverAmount:C2}) = {totalDistributed:C2}.";

			var finalResponse = new CompleteRoundResponse(req.RoundId, "Finalized", roundDetails.TotalPot, skinPayoutsResponse, totalSkinsPaidOut, finalSkinRolloverAmount, cthPayoutResponse, overallWinnerPayoutsResponse, totalOverallWinnerPayoutPaid, playerPayouts, verificationMessage);
			await SendOkAsync(finalResponse, ct);
		}
		catch (Exception ex)
		{
			await finalizationTransaction.RollbackAsync(ct);
			logger.LogError(ex, "Error during finalization phase of completing round {RoundId}", req.RoundId);
			var errorProblem = TypedResults.Problem(title: "Internal Server Error", detail: "An unexpected error occurred while finalizing the round and calculating payouts.", statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
		}
	}
}
