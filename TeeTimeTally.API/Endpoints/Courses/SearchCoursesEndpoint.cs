using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using Dapper;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes
using Microsoft.AspNetCore.Http; // For StatusCodes and TypedResults
using Microsoft.Extensions.Logging; // For ILogger
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text; // For StringBuilder
using FluentValidation;

namespace TeeTimeTally.API.Features.Courses.Endpoints.ListSearchCourses;

// --- DTOs for this endpoint ---
public class SearchCoursesRequest
{
	/// <summary>
	/// Optional search term to filter courses by name (case-insensitive partial match).
	/// </summary>
	public string? Search { get; set; }

	/// <summary>
	/// Optional: The maximum number of courses to return (for pagination). Defaults to 20.
	/// </summary>
	public int Limit { get; set; } = 20;

	/// <summary>
	/// Optional: The number of courses to skip (for pagination). Defaults to 0.
	/// </summary>
	public int Offset { get; set; } = 0;
}

public record SearchCoursesResponse(
	Guid Id,
	string Name,
	short CthHoleNumber,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	bool IsDeleted,      // Will be false for results from this query
	DateTime? DeletedAt  // Will be null for results from this query
);

// --- Fluent Validator for SearchCoursesRequestValidator ---
public class SearchCoursesRequestValidator : Validator<SearchCoursesRequest>
{
	public SearchCoursesRequestValidator()
	{
		RuleFor(x => x.Search)
			.MaximumLength(100).WithMessage("Search term cannot exceed 100 characters.")
			.When(x => !string.IsNullOrWhiteSpace(x.Search));

		RuleFor(x => x.Limit)
			.GreaterThan(0).WithMessage("Limit must be greater than 0.")
			.LessThanOrEqualTo(100).WithMessage("Limit cannot be greater than 100.");

		RuleFor(x => x.Offset)
			.GreaterThanOrEqualTo(0).WithMessage("Offset must be 0 or greater.");
	}
}

[HttpGet("/courses"), Authorize(Policy = Auth0Scopes.ReadCourses)]
public class SearchCoursesEndpoint(NpgsqlDataSource dataSource, ILogger<SearchCoursesEndpoint> logger) : Endpoint<SearchCoursesRequest, IEnumerable<SearchCoursesResponse>>
{
	public override async Task HandleAsync(SearchCoursesRequest req, CancellationToken ct)
	{
		// Input validation (Limit, Offset, Search length) is handled by SearchCoursesRequestValidator.
		// If validation fails, FastEndpoints returns a 400 Bad Request automatically.

		var sqlBuilder = new StringBuilder(@"
            SELECT 
                id AS Id, 
                name AS Name, 
                cth_hole_number AS CthHoleNumber, 
                created_at AS CreatedAt, 
                updated_at AS UpdatedAt,
                is_deleted AS IsDeleted,
                deleted_at AS DeletedAt
            FROM courses
            WHERE is_deleted = FALSE"); // Always fetch active courses

		var parameters = new DynamicParameters();

		if (!string.IsNullOrWhiteSpace(req.Search))
		{
			sqlBuilder.Append(" AND LOWER(name) LIKE LOWER(@SearchPattern)");
			parameters.Add("SearchPattern", $"%{req.Search}%");
		}

		sqlBuilder.Append(" ORDER BY name"); // Consistent ordering

		// Use validated Limit and Offset directly from the request DTO
		sqlBuilder.Append(" LIMIT @Limit OFFSET @Offset");
		parameters.Add("Limit", req.Limit);
		parameters.Add("Offset", req.Offset);

		IEnumerable<SearchCoursesResponse> courses;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);
			courses = await connection.QueryAsync<SearchCoursesResponse>(sqlBuilder.ToString(), parameters);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error searching for courses with request: {@SearchRequest}", req);
			var errorProblem = TypedResults.Problem(
				title: "Database Error",
				detail: "An error occurred while searching for courses.",
				statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		await SendOkAsync(courses ?? Enumerable.Empty<SearchCoursesResponse>(), ct);
	}
}