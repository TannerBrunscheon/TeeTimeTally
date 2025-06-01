using Dapper;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.API.Services;
using TeeTimeTally.Shared.Auth;

namespace TeeTimeTally.API.Endpoints.Rounds;


// --- DTOs for this endpoint ---

public class TeamDefinitionRequest
{
	public string TeamNameOrNumber { get; set; } = string.Empty;
	public List<Guid> GolferIdsInTeam { get; set; } = new();
}

public class StartRoundRequest
{
	[FromRoute]
	public Guid GroupId { get; set; }

	public Guid CourseId { get; set; }
	public List<Guid> AllParticipatingGolferIds { get; set; } = new(); // Flat list for easy validation
	public List<TeamDefinitionRequest> Teams { get; set; } = new();
	public DateTime? RoundDate { get; set; } // Optional, defaults to today
}

// --- Response DTOs ---
// For co-location:

public record FinancialConfigurationInputDTO(
	decimal BuyInAmount,
	string SkinValueFormula,
	string CthPayoutFormula
);

public record GolferBasicResponse(Guid GolferId, string FullName);

public record RoundTeamResponse(
	Guid TeamId,
	string TeamNameOrNumber,
	List<GolferBasicResponse> Members
);

public record StartRoundResponse(
	Guid RoundId,
	Guid GroupId,
	Guid CourseId,
	Guid FinancialConfigurationIdUsed, // The ID of the group_financial_configurations record used
	DateTime RoundDate,
	string Status, // From round_status_enum e.g., "SetupComplete"
	int NumPlayers,
	decimal TotalPot,
	decimal CalculatedSkinValuePerHole,
	decimal CalculatedCthPayout,
	List<RoundTeamResponse> TeamsInRound,
	DateTime CreatedAt
);

// Helper for fetching current user's golfer ID and admin status (file-scoped)
file record CurrentUserGolferInfo(Guid Id, bool IsSystemAdmin);
// Helper for fetching group's active financial config ID and golfer names
file record GroupDataForRoundStart(Guid ActiveFinancialConfigurationId, string Name, Guid Id); // Added GroupId as 'Id'
file record GolferDetail(Guid Id, string FullName); // For fetching golfer names


[FastEndpoints.HttpPost("/groups/{GroupId:guid}/rounds"), Authorize(Policy = Auth0Scopes.CreateRounds)]
public class StartRoundEndpoint(
	NpgsqlDataSource dataSource,
	ILogger<StartRoundEndpoint> logger) : Endpoint<StartRoundRequest, StartRoundResponse>
{
	public override async Task HandleAsync(StartRoundRequest req, CancellationToken ct)
	{
		// Most request DTO validations are now handled by StartRoundRequestValidator.
		// If validation fails, this HandleAsync method won't be reached.

		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		// Note: currentUserIdString null/empty check might be less critical if [Authorize] guarantees it,
		// but good for defense. The validator doesn't usually handle auth context.
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

		if (!currentUserInfo.IsSystemAdmin)
		{
			var isScorer = await connection.QuerySingleOrDefaultAsync<bool>(
				"SELECT TRUE FROM group_members WHERE group_id = @GroupId AND golfer_id = @GolferId AND is_scorer = TRUE;",
				new { req.GroupId, GolferId = currentUserInfo.Id });
			if (!isScorer)
			{
				await SendResultAsync(TypedResults.Problem(title: "Forbidden", detail: "User is not authorized to start rounds for this group.", statusCode: StatusCodes.Status403Forbidden));
				return;
			}
		}
		logger.LogInformation("User {UserId} (GolferId: {GolferId}) authorized for starting round in group {GroupId}.", auth0UserId, currentUserInfo.Id, req.GroupId);

		// Fetch Group's Active Financial Config (Validator already checked Course existence)
		var groupData = await connection.QuerySingleOrDefaultAsync<GroupDataForRoundStart>( // Assuming GroupDataForRoundStart DTO is defined
			"SELECT active_financial_configuration_id AS ActiveFinancialConfigurationId FROM groups WHERE id = @GroupId AND is_deleted = FALSE;",
			new { req.GroupId });

		// Validator should make group existence less of an issue here, but a direct check after auth is still good.
		if (groupData == null)
		{
			await SendResultAsync(TypedResults.Problem(title: "Not Found", detail: "Group not found or is inactive.", statusCode: StatusCodes.Status404NotFound));
			return;
		}
		if (groupData.ActiveFinancialConfigurationId == Guid.Empty)
		{
			await SendResultAsync(TypedResults.Problem(title: "Bad Request", detail: "The selected group does not have an active financial configuration set up.", statusCode: StatusCodes.Status400BadRequest));
			return;
		}

		// Re-using FinancialConfigurationInputDTO for fetching here, as it matches the structure needed for evaluation.
		var financialConfig = await connection.QuerySingleOrDefaultAsync<FinancialConfigurationInputDTO>(
			"SELECT buy_in_amount AS BuyInAmount, skin_value_formula AS SkinValueFormula, cth_payout_formula AS CthPayoutFormula FROM group_financial_configurations WHERE id = @ConfigId AND is_deleted = FALSE AND is_validated = TRUE;",
			new { ConfigId = groupData.ActiveFinancialConfigurationId });

		if (financialConfig == null)
		{
			await SendResultAsync(TypedResults.Problem(title: "Configuration Error", detail: "Active financial configuration for the group is missing, not validated, or inactive.", statusCode: StatusCodes.Status500InternalServerError));
			return;
		}

		// Fetch golfer names for response construction (Validator confirmed they are valid group members)
		var golferDetails = (await connection.QueryAsync<GolferDetail>( // Assuming GolferDetail DTO is defined
			"SELECT id AS Id, full_name AS FullName FROM golfers WHERE id = ANY(@GolferIds);",
			new { GolferIds = req.AllParticipatingGolferIds })).ToDictionary(g => g.Id);

		int numPlayers = req.AllParticipatingGolferIds.Count;
		decimal totalPot = numPlayers * financialConfig.BuyInAmount;
		var formulaParams = new Dictionary<string, object> { { "roundPlayers", numPlayers } };

		var (skinEvalSuccess, skinValuePerHole) = await FinancialValidationService.EvaluateFormulaAsync(financialConfig.SkinValueFormula, formulaParams, logger);
		var (cthEvalSuccess, cthPayoutValue) = await FinancialValidationService.EvaluateFormulaAsync(financialConfig.CthPayoutFormula, formulaParams, logger);

		if (!skinEvalSuccess || !cthEvalSuccess)
		{
			await SendResultAsync(TypedResults.Problem(title: "Configuration Error", detail: "Failed to evaluate financial formulas for the round.", statusCode: StatusCodes.Status500InternalServerError));
			return;
		}

		// --- Database Operations (Transaction) ---
		Guid newRoundId = Guid.NewGuid(); // Placeholder
		var teamResponses = new List<RoundTeamResponse>(); // Placeholder

		// For this example, let's assume the DB part was successful and we have newRoundId and teamResponses
		await using var transaction = await connection.BeginTransactionAsync(ct);
		try
		{
			// 1. Create Round
			const string insertRoundSql = @"
                INSERT INTO rounds (group_id, course_id, financial_configuration_id, round_date, status, 
                                    num_players, total_pot, calculated_skin_value_per_hole, calculated_cth_payout, 
                                    created_by_golfer_id, is_deleted, deleted_at)
                VALUES (@GroupId, @CourseId, @FinancialConfigurationId, @RoundDate, @Status, 
                        @NumPlayers, @TotalPot, @CalculatedSkinValuePerHole, @CalculatedCthPayout, 
                        @CreatedByGolferId, FALSE, NULL)
                RETURNING id;";
			newRoundId = await connection.ExecuteScalarAsync<Guid>(insertRoundSql, new
			{
				req.GroupId,
				req.CourseId,
				FinancialConfigurationId = groupData.ActiveFinancialConfigurationId,
				RoundDate = req.RoundDate ?? DateTime.UtcNow.Date,
				Status = "SetupComplete",
				NumPlayers = numPlayers,
				TotalPot = totalPot,
				CalculatedSkinValuePerHole = skinValuePerHole,
				CalculatedCthPayout = cthPayoutValue,
				CreatedByGolferId = currentUserInfo.Id
			}, transaction);

			// 2. Create Teams and Participants
			foreach (var teamDef in req.Teams)
			{
				const string insertTeamSql = @"
                    INSERT INTO round_teams (round_id, team_name_or_number, is_overall_winner, is_deleted, deleted_at)
                    VALUES (@RoundId, @TeamNameOrNumber, FALSE, FALSE, NULL)
                    RETURNING id;";
				var newTeamId = await connection.ExecuteScalarAsync<Guid>(insertTeamSql, new
				{
					RoundId = newRoundId,
					teamDef.TeamNameOrNumber
				}, transaction);

				var teamMembersForResponse = new List<GolferBasicResponse>();
				foreach (var golferIdInTeam in teamDef.GolferIdsInTeam)
				{
					const string insertParticipantSql = @"
                        INSERT INTO round_participants (round_id, golfer_id, round_team_id, buy_in_paid)
                        VALUES (@RoundId, @GolferId, @RoundTeamId, TRUE);";
					await connection.ExecuteAsync(insertParticipantSql, new
					{
						RoundId = newRoundId,
						GolferId = golferIdInTeam,
						RoundTeamId = newTeamId
					}, transaction);
					teamMembersForResponse.Add(new GolferBasicResponse(golferIdInTeam, golferDetails.TryGetValue(golferIdInTeam, out var detail) ? detail.FullName : "Unknown Golfer"));
				}
				teamResponses.Add(new RoundTeamResponse(newTeamId, teamDef.TeamNameOrNumber, teamMembersForResponse));
			}

			await transaction.CommitAsync(ct);
		}
		catch (Exception ex)
		{
			await transaction.RollbackAsync(ct);
			logger.LogError(ex, "Error during database transaction for starting round in group {GroupId}.", req.GroupId);
			var errorProblem = TypedResults.Problem(title: "Database Error", detail: "An error occurred while saving round details.", statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}


		var response = new StartRoundResponse(
			RoundId: newRoundId,
			GroupId: req.GroupId,
			CourseId: req.CourseId,
			FinancialConfigurationIdUsed: groupData.ActiveFinancialConfigurationId,
			RoundDate: req.RoundDate ?? DateTime.UtcNow.Date,
			Status: "SetupComplete",
			NumPlayers: numPlayers,
			TotalPot: totalPot,
			CalculatedSkinValuePerHole: skinValuePerHole,
			CalculatedCthPayout: cthPayoutValue,
			TeamsInRound: teamResponses,
			CreatedAt: DateTime.UtcNow // Ideally from DB RETURNING if round.created_at is returned
		);

		await SendAsync(response, StatusCodes.Status201Created, ct);
	}
}


public class StartRoundRequestValidator : Validator<StartRoundRequest>
{
	private readonly NpgsqlDataSource _dataSource;

	public StartRoundRequestValidator(NpgsqlDataSource dataSource) // Inject NpgsqlDataSource for DB checks
	{
		_dataSource = dataSource;

		RuleFor(x => x.GroupId)
			.NotEmpty().WithMessage("GroupId is required.");

		RuleFor(x => x.CourseId)
			.NotEmpty().WithMessage("CourseId is required.")
			.MustAsync(async (courseId, cancellation) => await DoesCourseExistAndIsActiveAsync(courseId, cancellation))
			.WithMessage("The specified course does not exist or is not active.");

		RuleFor(x => x.AllParticipatingGolferIds)
			.NotEmpty().WithMessage("At least one golfer must be selected.")
			.Must(ids => ids.Count >= 6).WithMessage("A minimum of 6 golfers are required for a round (REQ-RS-008).")
			.Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Golfer list contains duplicates.")
			.MustAsync(async (req, golferIds, context, cancellation) =>
				await AreAllGolfersValidAndMembersAsync(golferIds, req.GroupId, cancellation))
			.WithMessage("One or more selected golfers are not valid, not active, or not members of this group. Please check the list.");

		RuleFor(x => x.Teams)
			.NotEmpty().WithMessage("Team definitions are required.")
			.Must((req, teams) => // Custom rule to check overall team structure against AllParticipatingGolferIds
			{
				var allGolfersInTeamsFlattened = teams.SelectMany(t => t.GolferIdsInTeam).ToList();
				if (allGolfersInTeamsFlattened.Count != req.AllParticipatingGolferIds.Count) return false;
				if (allGolfersInTeamsFlattened.Distinct().Count() != allGolfersInTeamsFlattened.Count) return false; // duplicates within/across teams
				return new HashSet<Guid>(req.AllParticipatingGolferIds).SetEquals(new HashSet<Guid>(allGolfersInTeamsFlattened));
			}).WithMessage("Mismatch between all participating golfers and golfers assigned to teams, or duplicate assignments. Ensure every selected golfer is in exactly one team.");

		RuleForEach(x => x.Teams).ChildRules(team =>
		{
			team.RuleFor(t => t.TeamNameOrNumber)
				.NotEmpty().WithMessage("Team name or number cannot be empty.");
			team.RuleFor(t => t.GolferIdsInTeam)
				.NotEmpty().WithMessage(t => $"Team '{t.TeamNameOrNumber}' must have golfers.")
				.Must(ids => ids.Count >= 2 && ids.Count <= 3)
				.WithMessage(t => $"Team '{t.TeamNameOrNumber}' has an invalid size ({t.GolferIdsInTeam.Count}). Teams must have 2 or 3 players.");
		});

		// Complex rule for team sizes based on total player parity (REQ-RS-003.2)
		RuleFor(x => x) // Validating the whole request object
			.Must(req =>
			{
				if (req.Teams.Count == 0 || req.AllParticipatingGolferIds.Count == 0) return true; // Handled by other rules

				int totalPlayers = req.AllParticipatingGolferIds.Count;
				int threePlayerTeams = req.Teams.Count(t => t.GolferIdsInTeam.Count == 3);
				int twoPlayerTeams = req.Teams.Count(t => t.GolferIdsInTeam.Count == 2);

				if (totalPlayers % 2 != 0) // Odd number of total players
				{
					// Expect exactly one 3-player team, or an odd number of 3-player teams if many players
					// For simplicity with REQ-RS-003.2 ("one or more teams of 3"), let's ensure there's at least one 3-player team.
					// And that total players = 3 * threePlayerTeams + 2 * twoPlayerTeams
					return threePlayerTeams >= 1 && threePlayerTeams * 3 + twoPlayerTeams * 2 == totalPlayers;
				}
				else // Even number of total players
				{
					// Expect zero 3-player teams
					return threePlayerTeams == 0 && twoPlayerTeams * 2 == totalPlayers;
				}
			}).WithMessage("Team composition is incorrect for the number of players. For odd totals, use three-player teams as needed. For even totals, all teams should be two players.");
	}

	private async Task<bool> DoesCourseExistAndIsActiveAsync(Guid courseId, CancellationToken token)
	{
		if (courseId == Guid.Empty) return false;
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		return await connection.ExecuteScalarAsync<bool>(
			"SELECT EXISTS (SELECT 1 FROM courses WHERE id = @CourseId AND is_deleted = FALSE)",
			new { CourseId = courseId });
	}

	private async Task<bool> AreAllGolfersValidAndMembersAsync(List<Guid> golferIds, Guid groupId, CancellationToken token)
	{
		if (golferIds == null || golferIds.Count == 0) return true; // Handled by NotEmpty rule
		await using var connection = await _dataSource.OpenConnectionAsync(token);

		var validMemberCount = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(DISTINCT g.id) 
            FROM golfers g
            INNER JOIN group_members gm ON g.id = gm.golfer_id
            WHERE g.id = ANY(@GolferIds) AND g.is_deleted = FALSE AND gm.group_id = @GroupId;",
			new { GolferIds = golferIds, GroupId = groupId });

		return validMemberCount == golferIds.Distinct().Count();
	}
}