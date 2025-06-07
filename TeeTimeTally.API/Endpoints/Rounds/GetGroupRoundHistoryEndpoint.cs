using Dapper;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.API.Models;
using TeeTimeTally.Shared.Auth;

namespace TeeTimeTally.API.Features.Rounds.Endpoints;

public class GetGroupRoundHistoryRequest
{
	[FromRoute]
	public Guid GroupId { get; set; }
}

public class RoundHistoryItem
{
	public Guid RoundId { get; set; }
	public DateTime RoundDate { get; set; }
	public string CourseName { get; set; } = string.Empty;
	public short NumPlayers { get; set; }
	public decimal TotalPot { get; set; }
	public string Status { get; set; } = string.Empty;
}

public class GetGroupRoundHistoryResponse
{
	public List<RoundHistoryItem> Rounds { get; set; } = new();
}

public class GetGroupRoundHistoryRequestValidator : Validator<GetGroupRoundHistoryRequest>
{
	public GetGroupRoundHistoryRequestValidator()
	{
		RuleFor(x => x.GroupId).NotEmpty();
	}
}


[FastEndpoints.HttpGet("/groups/{GroupId:guid}/rounds/history"), Authorize(Policy = Auth0Scopes.ReadGroupRounds)]
public class GetGroupRoundHistoryEndpoint(NpgsqlDataSource dataSource, ILogger<GetGroupRoundHistoryEndpoint> logger) : Endpoint<GetGroupRoundHistoryRequest, GetGroupRoundHistoryResponse>
{
	public override async Task HandleAsync(GetGroupRoundHistoryRequest req, CancellationToken ct)
	{
		await using var connection = await dataSource.OpenConnectionAsync(ct);

		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		// Note: currentUserIdString null/empty check might be less critical if [Authorize] guarantees it,
		// but good for defense. The validator doesn't usually handle auth context.
		if (string.IsNullOrEmpty(auth0UserId))
		{
			await SendResultAsync(TypedResults.Problem(title: "Unauthorized", detail: "User identifier not found.", statusCode: StatusCodes.Status401Unauthorized));
			return;
		}

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
				"SELECT TRUE FROM group_members WHERE group_id = @GroupId AND golfer_id = @GolferId;",
				new { req.GroupId, GolferId = currentUserInfo.Id });
			if (!isScorer)
			{
				await SendResultAsync(TypedResults.Problem(title: "Forbidden", detail: "User is not authorized to view rounds for this group.", statusCode: StatusCodes.Status403Forbidden));
				return;
			}
		}
		logger.LogInformation("User {UserId} (GolferId: {GolferId}) authorized for viewing rounds in group {GroupId}.", auth0UserId, currentUserInfo.Id, req.GroupId);

		const string sql = @"
            SELECT
                r.id AS RoundId,
                r.round_date AS RoundDate,
                c.name AS CourseName,
                r.num_players AS NumPlayers,
                r.total_pot AS TotalPot,
                r.status::TEXT AS Status
            FROM
                rounds r
            JOIN
                courses c ON r.course_id = c.id
            WHERE
                r.group_id = @GroupId
                AND r.is_deleted = FALSE
            ORDER BY
                r.round_date DESC;";

		var rounds = await connection.QueryAsync<RoundHistoryItem>(sql, new { req.GroupId });

		var response = new GetGroupRoundHistoryResponse
		{
			Rounds = rounds.ToList()
		};

		await SendOkAsync(response, ct);
	}
}
