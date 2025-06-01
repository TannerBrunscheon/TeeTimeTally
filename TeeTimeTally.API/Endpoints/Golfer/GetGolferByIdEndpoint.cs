using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using Dapper;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes
using Microsoft.AspNetCore.Mvc;

namespace TeeTimeTally.API.Endpoints.Golfer;

// --- DTOs for this endpoint ---
public class GetGolferByIdRequest
{
	/// <summary>
	/// The ID (UUID) of the golfer to retrieve.
	/// </summary>
	[FromRoute] // Ensure this is bound from the route if not relying on property name matching
	public Guid Id { get; set; }
}

// Re-using GolferProfileResponse. Ensure it's defined consistently or shared.
// For co-location, defining it here:
public record GetGolferByIdResponse(
	Guid Id,
	string? Auth0UserId,
	string FullName,
	string Email,
	bool IsSystemAdmin,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	bool IsDeleted, // Will be false if record is found by this endpoint's query
	DateTime? DeletedAt
);

[FastEndpoints.HttpGet("/golfers/{Id:guid}"), Authorize(Policy = Auth0Scopes.ReadGolfers)]
public class GetGolferByIdEndpoint(NpgsqlDataSource dataSource, ILogger<GetGolferByIdEndpoint> logger) : Endpoint<GetGolferByIdRequest, GetGolferByIdResponse>
{
	public override async Task HandleAsync(GetGolferByIdRequest req, CancellationToken ct)
	{
		const string sql = @"
            SELECT 
                id AS Id, 
                auth0_user_id AS Auth0UserId, 
                full_name AS FullName, 
                email AS Email, 
                is_system_admin AS IsSystemAdmin, 
                created_at AS CreatedAt, 
                updated_at AS UpdatedAt,
                is_deleted AS IsDeleted,
                deleted_at AS DeletedAt
            FROM golfers
            WHERE id = @Id AND is_deleted = FALSE;"; // Only fetch active golfers

		GetGolferByIdResponse? golfer;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);
			golfer = await connection.QuerySingleOrDefaultAsync<GetGolferByIdResponse>(sql, new { req.Id });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error fetching golfer with ID {GolferId}.", req.Id);
			var errorProblem = TypedResults.Problem(
				title: "Database Error",
				detail: "An error occurred while fetching the golfer's data.",
				statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		if (golfer == null)
		{
			// No active golfer found with this ID
			await SendNotFoundAsync(ct);
			return;
		}

		await SendOkAsync(golfer, ct);
	}
}