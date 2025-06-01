using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using Dapper;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes
using Microsoft.AspNetCore.Mvc;

namespace TeeTimeTally.API.Endpoints.Golfer;

// --- DTO for this endpoint ---
public class DeleteGolferRequest
{
	/// <summary>
	/// The ID (UUID) of the golfer to be soft-deleted.
	/// </summary>
	[FromRoute] // Ensure this is bound from the route
	public Guid Id { get; set; }
}

// No specific Response DTO for a successful 204 No Content.

// Assuming 'ManageGolfers' is a scope defined in Auth0Scopes and assigned to Admins.
// Alternatively, an in-handler check for is_system_admin could be used if no specific scope.
[FastEndpoints.HttpDelete("/golfers/{Id:guid}"), Authorize(Policy = Auth0Scopes.CreateGolfers)]
public class DeleteGolferEndpoint(NpgsqlDataSource dataSource, ILogger<DeleteGolferEndpoint> logger) : Endpoint<DeleteGolferRequest>
{
	public override async Task HandleAsync(DeleteGolferRequest req, CancellationToken ct)
	{
		var golferIdToDelete = req.Id;

		// SQL for soft deleting an active golfer
		const string softDeleteSql = @"
            UPDATE golfers 
            SET is_deleted = TRUE, 
                deleted_at = NOW(),
                updated_at = NOW() -- Also update the 'updated_at' timestamp
            WHERE id = @Id AND is_deleted = FALSE;"; // Only soft delete if currently active

		int rowsAffected;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);
			rowsAffected = await connection.ExecuteAsync(softDeleteSql, new { Id = golferIdToDelete });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error soft deleting golfer with ID {GolferId}", golferIdToDelete);
			var errorProblem = TypedResults.Problem(
				title: "Internal Server Error",
				detail: "An unexpected error occurred while attempting to delete the golfer.",
				statusCode: StatusCodes.Status500InternalServerError
			);
			await SendResultAsync(errorProblem);
			return;
		}

		if (rowsAffected == 0)
		{
			// This means no *active* golfer with the given ID was found.
			// It could be that the golfer doesn't exist, or it was already soft-deleted.
			// Returning 404 is appropriate.
			await SendNotFoundAsync(ct);
			return;
		}

		// Successful soft delete
		logger.LogInformation("Successfully soft-deleted golfer with ID {GolferId}", golferIdToDelete);
		await SendNoContentAsync(ct); // 204 No Content
	}
}