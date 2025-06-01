using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using Dapper;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Endpoints.Groups;

// --- DTO for this endpoint ---
public class DeleteGroupRequest
{
	/// <summary>
	/// The ID of the group to be soft-deleted.
	/// </summary>
	public Guid Id { get; set; }
}

// No specific Response DTO for a successful 204 No Content.

// REQ-GR-001 covers Scorer creating groups. SRS does not explicitly state who deletes groups.
// Assuming deletion is an administrative task or requires higher privilege.
[HttpDelete("/groups/{Id:guid}"), Authorize(Policy = Auth0Scopes.ManageAllGroups)]
public class DeleteGroupEndpoint(NpgsqlDataSource dataSource, ILogger<DeleteGroupEndpoint> logger) : Endpoint<DeleteGroupRequest>
{
	public override async Task HandleAsync(DeleteGroupRequest req, CancellationToken ct)
	{
		var groupIdToDelete = req.Id; // ID from the route binding

		// SQL for soft deleting an active group
		const string softDeleteSql = @"
            UPDATE groups 
            SET is_deleted = TRUE, 
                deleted_at = NOW(),
                updated_at = NOW() -- Also update the 'updated_at' timestamp
            WHERE id = @Id AND is_deleted = FALSE;"; // Only soft delete if currently active

		int rowsAffected;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);
			rowsAffected = await connection.ExecuteAsync(softDeleteSql, new { Id = groupIdToDelete });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error soft deleting group with ID {GroupId}", groupIdToDelete);
			var errorProblem = TypedResults.Problem(
				title: "Internal Server Error",
				detail: "An unexpected error occurred while attempting to delete the group.",
				statusCode: StatusCodes.Status500InternalServerError
			);
			await SendResultAsync(errorProblem);
			return;
		}

		if (rowsAffected == 0)
		{
			// This means no *active* group with the given ID was found.
			// It could be that the group doesn't exist, or it was already soft-deleted.
			// Returning 404 is appropriate.
			await SendNotFoundAsync(ct);
			return;
		}

		// Successful soft delete
		await SendNoContentAsync(ct); // 204 No Content
	}
}