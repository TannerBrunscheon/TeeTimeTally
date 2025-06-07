using Dapper;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;
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


[FastEndpoints.HttpGet("/api/groups/{GroupId:guid}/rounds/history"), Authorize(Policy = Auth0Scopes.ReadGroupRounds)]
public class GetGroupRoundHistoryEndpoint : Endpoint<GetGroupRoundHistoryRequest, GetGroupRoundHistoryResponse>
{
	private readonly NpgsqlDataSource _dataSource;
	private readonly ILogger<GetGroupRoundHistoryEndpoint> _logger;

	public GetGroupRoundHistoryEndpoint(NpgsqlDataSource dataSource, ILogger<GetGroupRoundHistoryEndpoint> logger)
	{
		_dataSource = dataSource;
		_logger = logger;
	}

	public override async Task HandleAsync(GetGroupRoundHistoryRequest req, CancellationToken ct)
	{
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(auth0UserId))
		{
			await SendUnauthorizedAsync(ct);
			return;
		}

		await using var connection = await _dataSource.OpenConnectionAsync(ct);

		var golferId = await connection.QuerySingleOrDefaultAsync<Guid?>(
			"SELECT id FROM golfers WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;",
			new { Auth0UserId = auth0UserId });

		if (golferId == null)
		{
			await SendForbiddenAsync(ct);
			return;
		}

		// Authorization: Ensure the user is a member of the group they are requesting history for.
		var isGroupMember = await connection.ExecuteScalarAsync<bool>(
			"SELECT EXISTS (SELECT 1 FROM group_members WHERE group_id = @GroupId AND golfer_id = @GolferId AND is_deleted = FALSE)",
			new { req.GroupId, GolferId = golferId });

		if (!isGroupMember)
		{
			_logger.LogWarning("User {UserId} (GolferId: {GolferId}) attempted to access round history for non-member group {GroupId}.",
				auth0UserId, golferId, req.GroupId);
			await SendForbiddenAsync(ct);
			return;
		}

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
