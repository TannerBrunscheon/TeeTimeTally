using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using Dapper;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes
using System.Text; // For StringBuilder
using FluentValidation;

namespace TeeTimeTally.API.Endpoints.Golfer;

// --- DTOs for this endpoint ---
public class SearchGolfersRequest
{
	public string? Search { get; set; }
	public string? Email { get; set; }
	public int Limit { get; set; } = 20; // Default value
	public int Offset { get; set; } = 0;  // Default value
}

// Re-using GolferProfileResponse.
public record SearchGolfersResponse(
	Guid Id,
	string? Auth0UserId,
	string FullName,
	string Email,
	bool IsSystemAdmin,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	bool IsDeleted,
	DateTime? DeletedAt
);

// --- Fluent Validator for SearchGolfersRequest ---
public class SearchGolfersRequestValidator : Validator<SearchGolfersRequest>
{
	public SearchGolfersRequestValidator()
	{
		RuleFor(x => x.Search)
			.MaximumLength(100).WithMessage("Search term cannot exceed 100 characters.")
			.When(x => !string.IsNullOrWhiteSpace(x.Search));

		RuleFor(x => x.Email)
			.EmailAddress().WithMessage("A valid email address must be provided.")
			.MaximumLength(100).WithMessage("Email cannot exceed 100 characters.")
			.When(x => !string.IsNullOrWhiteSpace(x.Email));

		RuleFor(x => x.Limit)
			.GreaterThan(0).WithMessage("Limit must be greater than 0.")
			.LessThanOrEqualTo(100).WithMessage("Limit cannot be greater than 100.");

		RuleFor(x => x.Offset)
			.GreaterThanOrEqualTo(0).WithMessage("Offset must be 0 or greater.");
	}
}


[HttpGet("/golfers"), Authorize(Policy = Auth0Scopes.ReadGolfers)]
public class SearchGolfersEndpoint(NpgsqlDataSource dataSource, ILogger<SearchGolfersEndpoint> logger) : Endpoint<SearchGolfersRequest, IEnumerable<SearchGolfersResponse>>
{
	public override async Task HandleAsync(SearchGolfersRequest req, CancellationToken ct)
	{

		var sqlBuilder = new StringBuilder(@"
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
            WHERE is_deleted = FALSE");

		var parameters = new DynamicParameters();

		if (!string.IsNullOrWhiteSpace(req.Search))
		{
			sqlBuilder.Append(" AND (LOWER(full_name) LIKE LOWER(@SearchPattern) OR LOWER(email) LIKE LOWER(@SearchPattern))");
			parameters.Add("SearchPattern", $"%{req.Search}%");
		}

		if (!string.IsNullOrWhiteSpace(req.Email))
		{
			sqlBuilder.Append(" AND LOWER(email) = LOWER(@Email)");
			parameters.Add("Email", req.Email);
		}

		sqlBuilder.Append(" ORDER BY full_name, created_at");

		// Use validated Limit and Offset directly from the request DTO
		sqlBuilder.Append(" LIMIT @Limit OFFSET @Offset");
		parameters.Add("Limit", req.Limit);
		parameters.Add("Offset", req.Offset);

		IEnumerable<SearchGolfersResponse> golfers;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);
			golfers = await connection.QueryAsync<SearchGolfersResponse>(sqlBuilder.ToString(), parameters);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error searching for golfers with request: {@SearchRequest}", req);
			var errorProblem = TypedResults.Problem(
				title: "Database Error",
				detail: "An error occurred while searching for golfers.",
				statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		await SendOkAsync(golfers ?? Enumerable.Empty<SearchGolfersResponse>(), ct);
	}
}