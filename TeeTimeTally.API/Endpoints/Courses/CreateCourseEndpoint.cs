// TeeTimeTally.API/Features/Courses/Endpoints/CreateCourseEndpoint.cs
using Dapper;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.API.Models;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

// Define this if not already globally available
// public record SimpleErrorResponse(string Message);

namespace TeeTimeTally.API.Endpoints.Courses;

// 1. Request DTO
public record CreateCourseRequest(string Name, int CthHoleNumber);
// 1. Response DTO
public record CreateCourseResponse(Guid Id, string Name, short CthHoleNumber, DateTime CreatedAt, DateTime UpdatedAt);


[HttpPost("/courses"), Authorize(Policy = Auth0Scopes.CreateCourses)]
public class CreateCourseEndpoint(NpgsqlDataSource dataSource, ILogger<DeleteCourseEndpoint> logger) : Endpoint<CreateCourseRequest, CreateCourseResponse>
{
	public override async Task HandleAsync(CreateCourseRequest req, CancellationToken ct)
	{
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(auth0UserId))
		{
			await SendResultAsync(TypedResults.Problem(title: "Unauthorized", detail: "User identifier not found.", statusCode: StatusCodes.Status401Unauthorized));
			return;
		}

		await using var connection = await dataSource.OpenConnectionAsync(ct);

		var currentUserInfo = await connection.QuerySingleOrDefaultAsync<CurrentUserGolferInfo>(
			"SELECT id AS Id, is_system_admin AS IsSystemAdmin FROM golfers WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;",
			new { Auth0UserId = auth0UserId });

		if (currentUserInfo == null || !currentUserInfo.IsSystemAdmin)
		{
			await SendResultAsync(TypedResults.Problem(title: "Forbidden", detail: "User profile not found or inactive.", statusCode: StatusCodes.Status403Forbidden));
			return;
		}

		const string sql = @"
            INSERT INTO courses (name, cth_hole_number, created_by_golfer_id)
            VALUES (@Name, @CthHoleNumber, @CreatedByGolferId)
            RETURNING id AS Id, 
                      name AS Name, 
                      cth_hole_number AS CthHoleNumber, 
                      created_at AS CreatedAt, 
                      updated_at AS UpdatedAt;";

		CreateCourseResponse? createdCourseResponse;

		try
		{
			createdCourseResponse = await connection.QuerySingleOrDefaultAsync<CreateCourseResponse>(sql, new
			{
				req.Name,
				req.CthHoleNumber,
				CreatedByGolferId = currentUserInfo.Id
			});
		}
		catch (PostgresException ex)
		{
			logger.LogError(ex, "Database error creating course. SQLState: {SqlState}", ex.SqlState);
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
			logger.LogError(ex, "Unexpected error creating course.");
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
			logger.LogWarning("Course creation did not return a course record after insert, for request: {@Request}", req);
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
