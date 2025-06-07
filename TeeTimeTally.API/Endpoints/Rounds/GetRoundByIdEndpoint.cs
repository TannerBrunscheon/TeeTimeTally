using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using Dapper;
using TeeTimeTally.Shared.Auth;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TeeTimeTally.API.Models;
using System.Text.Json;
using TeeTimeTally.API.Features.Rounds.Endpoints.CompleteRound; // Reusing DTOs

namespace TeeTimeTally.API.Endpoints.Rounds;

// --- DTOs for this endpoint ---
public class GetRoundByIdRequest
{
	[FromRoute]
	public Guid RoundId { get; set; }
}

public record GolferParticipantResponse(Guid GolferId, string FullName);

public record TeamInRoundResponse(
	Guid TeamId,
	string TeamNameOrNumber,
	List<GolferParticipantResponse> Members,
	bool IsOverallWinner
);

public record ScoreDetailResponse(
	Guid TeamId,
	short HoleNumber, // Changed to short
	short Score,      // Changed to short
	bool IsSkinWinner,
	decimal SkinValueWon
);

public record RoundFinancialsResponse(
	Guid FinancialConfigurationIdUsed,
	decimal OriginalBuyInAmount,
	string OriginalSkinValueFormula,
	string OriginalCthPayoutFormula,
	decimal PerRoundCalculatedSkinValuePerHole,
	decimal PerRoundCalculatedCthPayout
);

public record GetRoundByIdResponse(
	Guid RoundId,
	DateTime RoundDate,
	string Status,
	Guid GroupId,
	string GroupName,
	Guid CourseId,
	string CourseName,
	short CourseCthHoleNumber, // Changed to short
	short NumPlayers,         // Changed to short
	decimal TotalPot,
	RoundFinancialsResponse Financials,
	List<TeamInRoundResponse> Teams,
	List<ScoreDetailResponse> Scores,
	Guid? CthWinnerGolferId,
	string? CthWinnerGolferName,
	decimal? FinalSkinRolloverAmount,
	decimal? FinalTotalSkinsPayout,
	decimal? FinalOverallWinnerPayoutAmount,
	DateTime? FinalizedAt,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	// Properties for finalized rounds
	List<PlayerPayoutSummaryResponse>? PlayerPayouts,
	string? PayoutVerificationMessage
);

file record RoundBaseInfo(
	Guid RoundId, DateTime RoundDate, string Status, Guid GroupId, string GroupName,
	Guid CourseId, string CourseName, short CourseCthHoleNumber, short NumPlayers, decimal TotalPot,
	Guid FinancialConfigurationIdUsed, decimal OriginalBuyInAmount, string OriginalSkinValueFormula, string OriginalCthPayoutFormula,
	decimal PerRoundCalculatedSkinValuePerHole, decimal PerRoundCalculatedCthPayout,
	Guid? CthWinnerGolferId, decimal? FinalSkinRolloverAmount, decimal? FinalTotalSkinsPayout,
	decimal? FinalOverallWinnerPayoutAmount, DateTime? FinalizedAt, DateTime CreatedAt, DateTime UpdatedAt
);

[FastEndpoints.HttpGet("/rounds/{RoundId:guid}"), Authorize(Policy = Auth0Scopes.ReadGroupRounds)]
public class GetRoundByIdEndpoint(NpgsqlDataSource dataSource, ILogger<GetRoundByIdEndpoint> logger) : Endpoint<GetRoundByIdRequest, GetRoundByIdResponse>
{
	public override async Task HandleAsync(GetRoundByIdRequest req, CancellationToken ct)
	{
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(auth0UserId))
		{
			await SendResultAsync(TypedResults.Problem(title: "Unauthorized", detail: "User identifier not found.", statusCode: StatusCodes.Status401Unauthorized));
			return;
		}

		await using var connection = await dataSource.OpenConnectionAsync(ct);

		// Fetch base round details along with joined info
		const string roundSql = @"
            SELECT 
                r.id AS RoundId, r.round_date AS RoundDate, r.status::TEXT AS Status, r.group_id AS GroupId, gr.name AS GroupName,
                r.course_id AS CourseId, c.name AS CourseName, c.cth_hole_number AS CourseCthHoleNumber,
                r.num_players AS NumPlayers, r.total_pot AS TotalPot,
                r.financial_configuration_id AS FinancialConfigurationIdUsed,
                gfc.buy_in_amount AS OriginalBuyInAmount, 
                gfc.skin_value_formula AS OriginalSkinValueFormula,
                gfc.cth_payout_formula AS OriginalCthPayoutFormula,
                r.calculated_skin_value_per_hole AS PerRoundCalculatedSkinValuePerHole,
                r.calculated_cth_payout AS PerRoundCalculatedCthPayout,
                r.cth_winner_golfer_id AS CthWinnerGolferId,
                r.final_skin_rollover_amount AS FinalSkinRolloverAmount,
                r.final_total_skins_payout AS FinalTotalSkinsPayout,
                r.final_overall_winner_payout_amount AS FinalOverallWinnerPayoutAmount,
                r.finalized_at AS FinalizedAt,
                r.created_at AS CreatedAt, r.updated_at AS UpdatedAt
            FROM rounds r
            JOIN groups gr ON r.group_id = gr.id
            JOIN courses c ON r.course_id = c.id
            LEFT JOIN group_financial_configurations gfc ON r.financial_configuration_id = gfc.id
            WHERE r.id = @RoundId AND r.is_deleted = FALSE AND gr.is_deleted = FALSE;";

		var roundBaseInfo = await connection.QuerySingleOrDefaultAsync<RoundBaseInfo>(roundSql, new { req.RoundId });

		if (roundBaseInfo == null)
		{
			await SendNotFoundAsync(ct);
			return;
		}

		// --- Authorization Check: Admin, Scorer for the group, or Participant in the round ---
		var currentUserInfo = await connection.QuerySingleOrDefaultAsync<CurrentUserGolferInfo>(
			"SELECT id AS Id, is_system_admin AS IsSystemAdmin FROM golfers WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;",
			new { Auth0UserId = auth0UserId });

		if (currentUserInfo == null)
		{
			await SendResultAsync(TypedResults.Problem(title: "Forbidden", detail: "User profile not found.", statusCode: StatusCodes.Status403Forbidden));
			return;
		}

		bool isAuthorized = currentUserInfo.IsSystemAdmin;
		if (!isAuthorized)
		{
			var isScorer = await connection.QuerySingleOrDefaultAsync<bool>(
				"SELECT TRUE FROM group_members WHERE group_id = @GroupId AND golfer_id = @GolferId AND is_scorer = TRUE;",
				new { roundBaseInfo.GroupId, GolferId = currentUserInfo.Id });
			if (isScorer) isAuthorized = true;
		}
		if (!isAuthorized)
		{
			var isParticipant = await connection.QuerySingleOrDefaultAsync<bool>(
				"SELECT TRUE FROM round_participants WHERE round_id = @RoundId AND golfer_id = @GolferId;",
				new { req.RoundId, GolferId = currentUserInfo.Id });
			if (isParticipant) isAuthorized = true;
		}

		if (!isAuthorized)
		{
			logger.LogWarning("User {Auth0UserId} (GolferId {GolferId}) is not authorized to view round {RoundId}.", auth0UserId, currentUserInfo.Id, req.RoundId);
			await SendResultAsync(TypedResults.Problem(title: "Forbidden", detail: "User is not authorized to view this round.", statusCode: StatusCodes.Status403Forbidden));
			return;
		}
		logger.LogInformation("User {Auth0UserId} (GolferId: {GolferId}) authorized to view round {RoundId}.", auth0UserId, currentUserInfo.Id, req.RoundId);


		// Fetch Teams and their Members
		var teamsInRound = new List<TeamInRoundResponse>();
		const string teamsSql = "SELECT id AS TeamId, team_name_or_number AS TeamNameOrNumber, is_overall_winner as IsOverallWinner FROM round_teams WHERE round_id = @RoundId AND is_deleted = FALSE;";
		var dbTeams = await connection.QueryAsync(teamsSql, new { req.RoundId });

		const string participantsSql = "SELECT rp.round_team_id AS TeamId, rp.golfer_id AS GolferId, g.full_name AS FullName FROM round_participants rp JOIN golfers g ON rp.golfer_id = g.id WHERE rp.round_id = @RoundId AND g.is_deleted = FALSE;";
		var dbParticipants = (await connection.QueryAsync<dynamic>(participantsSql, new { req.RoundId })).ToList();

		foreach (var dbTeam in dbTeams)
		{
			var members = dbParticipants
				.Where(p => p.teamid == dbTeam.teamid)
				.Select(p => new GolferParticipantResponse((Guid)p.golferid, (string)p.fullname))
				.ToList();
			teamsInRound.Add(new TeamInRoundResponse((Guid)dbTeam.teamid, (string)dbTeam.teamnameornumber, members, (bool)dbTeam.isoverallwinner));
		}

		// Fetch Scores
		const string scoresSql = "SELECT round_team_id AS TeamId, hole_number AS HoleNumber, score AS Score, is_skin_winner AS IsSkinWinner, skin_value_won AS SkinValueWon FROM round_scores WHERE round_id = @RoundId;";
		var scores = (await connection.QueryAsync<ScoreDetailResponse>(scoresSql, new { req.RoundId })).ToList();

		// Fetch CTH Winner Golfer Name if ID exists
		string? cthWinnerName = null;
		if (roundBaseInfo.CthWinnerGolferId.HasValue && roundBaseInfo.CthWinnerGolferId.Value != Guid.Empty)
		{
			cthWinnerName = await connection.QuerySingleOrDefaultAsync<string>(
				"SELECT full_name FROM golfers WHERE id = @GolferId AND is_deleted = FALSE;",
				new { GolferId = roundBaseInfo.CthWinnerGolferId.Value });
		}

		// --- NEW LOGIC for Finalized Rounds ---
		List<PlayerPayoutSummaryResponse>? playerPayouts = null;
		string? payoutVerificationMessage = null;

		if (roundBaseInfo.Status == "Finalized")
		{
			const string selectPayoutsSql = "SELECT g.full_name as FullName, rt.team_name_or_number as TeamName, rps.* FROM round_payout_summary rps JOIN golfers g ON rps.golfer_id = g.id JOIN round_teams rt ON rps.team_id = rt.id WHERE rps.round_id = @RoundId;";
			var payoutSummariesFromDb = await connection.QueryAsync<dynamic>(selectPayoutsSql, new { req.RoundId });

			playerPayouts = payoutSummariesFromDb.Select(p => {
				var breakdown = JsonSerializer.Deserialize<PlayerPayoutBreakdown>((string)p.breakdown) ?? new PlayerPayoutBreakdown(0, 0, 0);
				return new PlayerPayoutSummaryResponse((Guid)p.golfer_id, (string)p.fullname, (Guid)p.team_id, (string)p.teamname, (decimal)p.total_winnings, breakdown);
			}).ToList();

			decimal totalSkins = roundBaseInfo.FinalTotalSkinsPayout ?? 0;
			decimal cthPayout = roundBaseInfo.CthWinnerGolferId.HasValue ? roundBaseInfo.PerRoundCalculatedCthPayout : 0;
			decimal overallPayout = roundBaseInfo.FinalOverallWinnerPayoutAmount ?? 0;
			decimal rollover = roundBaseInfo.FinalSkinRolloverAmount ?? 0;
			decimal totalDistributed = totalSkins + cthPayout + overallPayout + rollover;

			payoutVerificationMessage = $"Verification: Total Pot ({roundBaseInfo.TotalPot:C2}). Distributed: Skins ({totalSkins:C2}) + CTH ({cthPayout:C2}) + Overall Winners ({overallPayout:C2}) + Rollover ({rollover:C2}) = {totalDistributed:C2}.";
			if (Math.Abs(totalDistributed - roundBaseInfo.TotalPot) > 0.01m * roundBaseInfo.NumPlayers)
			{
				payoutVerificationMessage += " DISCREPANCY DETECTED!";
			}
			else
			{
				payoutVerificationMessage += " BALANCED.";
			}
		}


		// Construct the final response
		var response = new GetRoundByIdResponse(
			RoundId: roundBaseInfo.RoundId,
			RoundDate: roundBaseInfo.RoundDate,
			Status: roundBaseInfo.Status,
			GroupId: roundBaseInfo.GroupId,
			GroupName: roundBaseInfo.GroupName,
			CourseId: roundBaseInfo.CourseId,
			CourseName: roundBaseInfo.CourseName,
			CourseCthHoleNumber: roundBaseInfo.CourseCthHoleNumber,
			NumPlayers: roundBaseInfo.NumPlayers,
			TotalPot: roundBaseInfo.TotalPot,
			Financials: new RoundFinancialsResponse(
				FinancialConfigurationIdUsed: roundBaseInfo.FinancialConfigurationIdUsed,
				OriginalBuyInAmount: roundBaseInfo.OriginalBuyInAmount,
				OriginalSkinValueFormula: roundBaseInfo.OriginalSkinValueFormula,
				OriginalCthPayoutFormula: roundBaseInfo.OriginalCthPayoutFormula,
				PerRoundCalculatedSkinValuePerHole: roundBaseInfo.PerRoundCalculatedSkinValuePerHole,
				PerRoundCalculatedCthPayout: roundBaseInfo.PerRoundCalculatedCthPayout
			),
			Teams: teamsInRound,
			Scores: scores,
			CthWinnerGolferId: roundBaseInfo.CthWinnerGolferId,
			CthWinnerGolferName: cthWinnerName,
			FinalSkinRolloverAmount: roundBaseInfo.FinalSkinRolloverAmount,
			FinalTotalSkinsPayout: roundBaseInfo.FinalTotalSkinsPayout,
			FinalOverallWinnerPayoutAmount: roundBaseInfo.FinalOverallWinnerPayoutAmount,
			FinalizedAt: roundBaseInfo.FinalizedAt,
			CreatedAt: roundBaseInfo.CreatedAt,
			UpdatedAt: roundBaseInfo.UpdatedAt,
			PlayerPayouts: playerPayouts, // Add new property
			PayoutVerificationMessage: payoutVerificationMessage // Add new property
		);

		await SendOkAsync(response, ct);
	}
}
