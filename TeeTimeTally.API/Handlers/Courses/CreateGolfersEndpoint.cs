// TeeTimeTally.API/Features/Courses/Endpoints/CreateCourseEndpoint.cs
using Dapper;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.API.Features.Courses.Endpoints.GetCourseById; // Assuming your DTOs are here
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

// Define this if not already globally available
// public record SimpleErrorResponse(string Message);

namespace TeeTimeTally.API.Features.Courses.Endpoints;

// 1. Request DTO
public record CreateCourseRequest(string Name, int CthHoleNumber);
// 1. Response DTO
public record CourseResponse(Guid Id, string Name, int CthHoleNumber, DateTime CreatedAt, DateTime UpdatedAt);


[HttpPost("/courses"), Authorize(Policy = Auth0Scopes.CreateCourses)]
public class CreateCourseEndpoint(NpgsqlDataSource dataSource) : Endpoint<CreateCourseRequest, CourseResponse>
{
	public override async Task HandleAsync(CreateCourseRequest req, CancellationToken ct)
	{
		var adminGolferIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(adminGolferIdString) || !Guid.TryParse(adminGolferIdString, out var adminGolferId))
		{
			await SendUnauthorizedAsync(ct); // This correctly sends a 401 and terminates.
			return; // Good practice to explicitly return after sending a terminal response.
		}

		const string sql = @"
            INSERT INTO courses (name, cth_hole_number, created_by_golfer_id)
            VALUES (@Name, @CthHoleNumber, @CreatedByGolferId)
            RETURNING id AS Id, 
                      name AS Name, 
                      cth_hole_number AS CthHoleNumber, 
                      created_at AS CreatedAt, 
                      updated_at AS UpdatedAt;";

		CourseResponse? createdCourseResponse;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);

			createdCourseResponse = await connection.QuerySingleOrDefaultAsync<CourseResponse>(sql, new
			{
				Name = req.Name,
				CthHoleNumber = req.CthHoleNumber,
				CreatedByGolferId = adminGolferId
			});
		}
		catch (PostgresException ex)
		{
			Logger.LogError(ex, "Database error creating course. SQLState: {SqlState}", ex.SqlState);
			if (ex.SqlState == "23505") // Unique violation for course name
			{
				var conflictProblem = TypedResults.Problem(
					title: "Conflict",
					detail: $"A course with the name '{req.Name}' already exists.",
					statusCode: StatusCodes.Status409Conflict,
					extensions: new Dictionary<string, object?> { { "conflictingField", nameof(req.Name) } }
				);
				await SendResultAsync(conflictProblem);
				return; // Explicit return after SendResultAsync
			}

			var dbErrorProblem = TypedResults.Problem(
				title: "Database Error",
				detail: "An unexpected database error occurred while creating the course.",
				statusCode: StatusCodes.Status500InternalServerError
			);
			await SendResultAsync(dbErrorProblem);
			return; // Explicit return
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Unexpected error creating course.");
			var generalErrorProblem = TypedResults.Problem(
				title: "Internal Server Error",
				detail: "An unexpected error occurred.",
				statusCode: StatusCodes.Status500InternalServerError
			);
			await SendResultAsync(generalErrorProblem);
			return; // Explicit return
		}

		if (createdCourseResponse == null)
		{
			Logger.LogWarning("Course creation did not return a course record after insert, for request: {@Request}", req);
			var creationErrorProblem = TypedResults.Problem(
				title: "Creation Error",
				detail: "Failed to create the course or retrieve creation details.",
				statusCode: StatusCodes.Status500InternalServerError
			);
			await SendResultAsync(creationErrorProblem);
			return; // Explicit return
		}

		// Send 201 Created for success
		await SendCreatedAtAsync<GetCourseByIdEndpoint>(
			routeValues: new { createdCourseResponse.Id },
			responseBody: createdCourseResponse,
			cancellation: ct);
	}
}
