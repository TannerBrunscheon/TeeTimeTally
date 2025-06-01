using Dapper;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Endpoints.Courses; // Using a more specific namespace

// 1. Request DTO
public class GetCourseByIdRequest
{
	/// <summary>
	/// The ID of the course to retrieve.
	/// </summary>
	public Guid Id { get; set; }
}

// 2. Response DTO
public record GetSingleCourseResponse(
	Guid Id,
	string Name,
	int CthHoleNumber,
	DateTime CreatedAt,
	DateTime UpdatedAt
);

// 3. Endpoint Class
[HttpGet("/courses/{Id}"), Authorize(Policy = Auth0Scopes.ReadCourses)]
public class GetCourseByIdEndpoint(NpgsqlDataSource dataSource, ILogger<GetCourseByIdEndpoint> logger) : Endpoint<GetCourseByIdRequest, GetSingleCourseResponse>
{
	public override async Task HandleAsync(GetCourseByIdRequest req, CancellationToken ct)
	{
		const string sql = @"
            SELECT id AS Id, 
                   name AS Name, 
                   cth_hole_number AS CthHoleNumber, 
                   created_at AS CreatedAt, 
                   updated_at AS UpdatedAt
            FROM courses
            WHERE id = @Id;";

		GetSingleCourseResponse? course;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);
			course = await connection.QuerySingleOrDefaultAsync<GetSingleCourseResponse>(sql, new { req.Id });
		}
		catch (Exception ex) // Includes NpgsqlException, but also any other during DB interaction
		{
			logger.LogError(ex, "Error fetching course with ID {CourseId}.", req.Id);
			var errorProblem = TypedResults.Problem(
				title: "Database Error",
				detail: "An error occurred while fetching the course data.",
				statusCode: StatusCodes.Status500InternalServerError
			);
			await SendResultAsync(errorProblem);
			return;
		}

		if (course == null)
		{
			await SendNotFoundAsync(ct); // FastEndpoints method for 404
			return;
		}

		await SendOkAsync(course, ct); // FastEndpoints method for 200 OK with response
	}
}