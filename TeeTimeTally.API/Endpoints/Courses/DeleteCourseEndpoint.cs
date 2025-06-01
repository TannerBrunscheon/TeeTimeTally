using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using Dapper;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Endpoints.Courses;

// 1. Request DTO
public class DeleteCourseRequest
{
	/// <summary>
	/// The ID of the course to soft delete.
	/// </summary>
	public Guid Id { get; set; }
}

// 2. Endpoint Class
[HttpDelete("/courses/{Id}"), Authorize(Policy = Auth0Scopes.ManageCourses)]
public class DeleteCourseEndpoint(NpgsqlDataSource dataSource, ILogger<DeleteCourseEndpoint> logger) : Endpoint<DeleteCourseRequest>
{
	public override async Task HandleAsync(DeleteCourseRequest req, CancellationToken ct)
	{
		// SQL for soft deleting an active course
		const string softDeleteSql = @"
            UPDATE courses 
            SET is_deleted = TRUE, 
                deleted_at = NOW(),
                updated_at = NOW() -- Also update the 'updated_at' timestamp
            WHERE id = @Id AND is_deleted = FALSE;"; // Only soft delete if currently active

		int rowsAffected;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);
			rowsAffected = await connection.ExecuteAsync(softDeleteSql, new { req.Id });
		}
		catch (Exception ex) // General catch for unexpected database or other errors
		{
			logger.LogError(ex, "Error soft deleting course with ID {CourseId}", req.Id);
			var errorProblem = TypedResults.Problem(
				title: "Internal Server Error",
				detail: "An unexpected error occurred while attempting to delete the course.",
				statusCode: StatusCodes.Status500InternalServerError
			);
			await SendResultAsync(errorProblem);
			return;
		}

		if (rowsAffected == 0)
		{
			// This means no *active* course with the given ID was found to be soft-deleted.
			// It could be that the course doesn't exist, or it was already soft-deleted.
			// Consistently returning 404 is appropriate here.
			await SendNotFoundAsync(ct);
			return;
		}

		// Successful soft delete
		await SendNoContentAsync(ct); // 204 No Content
	}
}