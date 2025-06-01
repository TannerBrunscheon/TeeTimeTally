using Dapper;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.API.Endpoints.Golfer;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Features.Golfers.Endpoints.CreateGolfer;

// --- DTOs for this endpoint ---
public record CreateGolferRequest(
	string FullName,
	string Email,
	string? Auth0UserId // Optional: can be null if creating before Auth0 sync
);

// Re-using GolferProfileResponse. Ensure it's defined consistently or shared.
public record CreateGolferResponse(
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

// --- Fluent Validator for CreateGolferRequest ---
public class CreateGolferRequestValidator : Validator<CreateGolferRequest>
{
	public CreateGolferRequestValidator()
	{
		RuleFor(x => x.FullName)
			.NotEmpty().WithMessage("Full name is required.")
			.MaximumLength(200).WithMessage("Full name cannot exceed 200 characters.");

		RuleFor(x => x.Email)
			.NotEmpty().WithMessage("Email is required.")
			.EmailAddress().WithMessage("A valid email address must be provided.")
			.MaximumLength(254).WithMessage("Email cannot exceed 254 characters.");

		RuleFor(x => x.Auth0UserId)
			.MaximumLength(128).WithMessage("Auth0 User ID cannot exceed 128 characters.")
			.When(x => !string.IsNullOrWhiteSpace(x.Auth0UserId));
	}
}


[HttpPost("/golfers"), Authorize(Policy = Auth0Scopes.CreateGolfers)]
public class CreateGolferEndpoint : Endpoint<CreateGolferRequest, CreateGolferResponse>
{
	private readonly NpgsqlDataSource _dataSource;
	private readonly ILogger<CreateGolferEndpoint> _logger;

	public CreateGolferEndpoint(NpgsqlDataSource dataSource, ILogger<CreateGolferEndpoint> logger)
	{
		_dataSource = dataSource;
		_logger = logger;
	}

	public override async Task HandleAsync(CreateGolferRequest req, CancellationToken ct)
	{
		// Request DTO format validation is handled by CreateGolferRequestValidator.
		var creatingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System"; // Log who is performing action

		// is_system_admin defaults to FALSE in the DB for new golfers.
		// is_deleted defaults to FALSE, deleted_at to NULL.
		const string insertSql = @"
            INSERT INTO golfers (full_name, email, auth0_user_id, is_system_admin, is_deleted, deleted_at)
            VALUES (@FullName, @Email, @Auth0UserId, FALSE, FALSE, NULL)
            RETURNING 
                id AS Id, 
                auth0_user_id AS Auth0UserId, 
                full_name AS FullName, 
                email AS Email, 
                is_system_admin AS IsSystemAdmin, 
                created_at AS CreatedAt, 
                updated_at AS UpdatedAt,
                is_deleted AS IsDeleted,
                deleted_at AS DeletedAt;
            ";

		CreateGolferResponse? newGolferProfile;

		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync(ct);
			newGolferProfile = await connection.QuerySingleAsync<CreateGolferResponse>(insertSql, new
			{
				req.FullName,
				req.Email,
				Auth0UserId = string.IsNullOrWhiteSpace(req.Auth0UserId) ? null : req.Auth0UserId // Ensure NULL if empty/whitespace
			});
		}
		catch (PostgresException ex) when (ex.SqlState == "23505") // Unique_violation
		{
			_logger.LogWarning(ex, "Unique constraint violation while creating golfer by {CreatingUser}. Request: {@Request}", creatingUserId, req);

			string conflictingField = "unknown";
			string errorMessage = "A golfer with the provided details already exists.";

			if (ex.ConstraintName != null)
			{
				if (ex.ConstraintName.Contains("email"))
				{
					conflictingField = nameof(req.Email);
					errorMessage = $"An active golfer with the email '{req.Email}' already exists.";
				}
				else if (ex.ConstraintName.Contains("auth0_user_id"))
				{
					conflictingField = nameof(req.Auth0UserId);
					errorMessage = $"An active golfer with the Auth0 User ID '{req.Auth0UserId}' already exists.";
				}
			}

			var conflictProblem = TypedResults.Problem(
				title: "Conflict",
				detail: errorMessage,
				statusCode: StatusCodes.Status409Conflict,
				extensions: new Dictionary<string, object?> { { "conflictingField", conflictingField } }
			);
			await SendResultAsync(conflictProblem);
			return;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error creating new golfer by {CreatingUser}. Request: {@Request}", creatingUserId, req);
			var errorProblem = TypedResults.Problem(
				title: "Internal Server Error",
				detail: "An unexpected error occurred while creating the golfer profile.",
				statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		if (newGolferProfile == null) // Should not happen if QuerySingleAsync is used after successful insert
		{
			_logger.LogError("Golfer creation did not return a profile despite no DB exception. Request: {@Request}", req);
			var creationErrorProblem = TypedResults.Problem(title: "Server Error", detail: "Failed to retrieve golfer profile after creation.", statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(creationErrorProblem);
			return;
		}

		_logger.LogInformation("Golfer profile {GolferId} created successfully by {CreatingUser} for email {Email}", newGolferProfile.Id, creatingUserId, newGolferProfile.Email);

		// Assuming a GetGolferByIdEndpoint exists to point to the location of the new resource.
		// If its DTOs are co-located, we'd need its specific namespace.
		// E.g., TeeTimeTally.API.Features.Golfers.Endpoints.GetGolferById.GetGolferByIdEndpoint
		await SendCreatedAtAsync<GetGolferByIdEndpoint>( // Adjust namespace if GetGolferByIdEndpoint is different
			routeValues: new { newGolferProfile.Id },
			responseBody: newGolferProfile,
			cancellation: ct);
	}
}