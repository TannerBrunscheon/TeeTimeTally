using Dapper;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc; // For FromRoute
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.API.Services; // For IFinancialValidationService
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Features.Groups.Endpoints.UpdateGroup;

// --- DTOs for this endpoint ---

public record NewFinancialConfigurationDetails(
	decimal BuyInAmount,
	string SkinValueFormula,
	string CthPayoutFormula
);

public class UpdateGroupRequest
{
	[FromRoute]
	public Guid GroupId { get; set; }

	// Fields for basic group update (optional)
	public string? Name { get; set; }
	public Guid? DefaultCourseId { get; set; }

	// Option 1: Link to an existing, validated financial configuration
	public Guid? ExistingActiveFinancialConfigurationId { get; set; }

	// Option 2: Provide new financial details to create and link a new configuration
	public NewFinancialConfigurationDetails? NewFinancials { get; set; }
}

// Response DTOs (ensure these are consistent with other group endpoints or shared)
public record FinancialConfigurationFromUpdateResponseDTO(
	Guid Id,
	Guid GroupId,
	decimal BuyInAmount,
	string SkinValueFormula,
	string CthPayoutFormula,
	bool IsValidated,
	DateTime CreatedAt,
	DateTime? ValidatedAt,
	bool IsDeleted,
	DateTime? DeletedAt
);

public record UpdateGroupResponse(
	Guid Id,
	string Name,
	Guid? DefaultCourseId,
	FinancialConfigurationFromUpdateResponseDTO? ActiveFinancialConfiguration,
	Guid CreatedByGolferId,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	bool IsDeleted,
	DateTime? DeletedAt
);

// Helper for fetching current user's golfer ID and admin status
file record CurrentUserGolferInfo(Guid Id, bool IsSystemAdmin);
// Helper to fetch current group details
file record CurrentGroupData(
	string CurrentName,
	Guid? CurrentDefaultCourseId,
	Guid? CurrentActiveFinancialConfigurationId
);


// --- Fluent Validator for UpdateGroupRequest ---
public class UpdateGroupRequestValidator : Validator<UpdateGroupRequest>
{
	private readonly NpgsqlDataSource _dataSource;

	public UpdateGroupRequestValidator(NpgsqlDataSource dataSource)
	{
		_dataSource = dataSource;

		RuleFor(x => x.GroupId)
			.NotEmpty().WithMessage("GroupId is required.");

		RuleFor(x => x.Name)
			.NotEmpty().WithMessage("Group name cannot be empty if provided.")
			.MaximumLength(100).WithMessage("Group name cannot exceed 100 characters.")
			.MustAsync(async (req, newName, context, cancellationToken) =>
				await IsGroupNameUniqueAsync(newName!, req.GroupId, cancellationToken))
			.WithMessage((req, name) => $"A group with the name '{name}' already exists.")
			.When(x => x.Name != null); // Only validate if Name is being updated

		RuleFor(x => x.DefaultCourseId)
			.MustAsync(async (courseId, cancellationToken) =>
				courseId == null || courseId == Guid.Empty || await CourseExistsAndIsActiveAsync(courseId.Value, cancellationToken))
			.WithMessage("Specified DefaultCourseId does not exist or is not active.")
			.When(x => x.DefaultCourseId.HasValue && x.DefaultCourseId.Value != Guid.Empty); // Check only if a non-empty Guid is provided

		RuleFor(x => x.ExistingActiveFinancialConfigurationId)
			.MustAsync(async (req, configId, cancellationToken) =>
				configId == null || configId == Guid.Empty || await FinancialConfigExistsAndIsValidatedForGroupAsync(configId.Value, req.GroupId, cancellationToken))
			.WithMessage("Specified ExistingActiveFinancialConfigurationId does not exist, is not validated, or does not belong to this group.")
			.When(x => x.ExistingActiveFinancialConfigurationId.HasValue && x.ExistingActiveFinancialConfigurationId.Value != Guid.Empty);

		RuleFor(x => x)
			.Must(x => !(x.ExistingActiveFinancialConfigurationId.HasValue && x.NewFinancials != null))
			.WithMessage("Cannot provide both an existing financial configuration ID and new financial details simultaneously.");

		When(x => x.NewFinancials != null, () =>
		{
			RuleFor(x => x.NewFinancials!.BuyInAmount)
				.GreaterThan(0).WithMessage("Buy-in amount must be greater than zero.");
			RuleFor(x => x.NewFinancials!.SkinValueFormula)
				.NotEmpty().WithMessage("Skin value formula cannot be empty.")
				.MaximumLength(255).WithMessage("Skin value formula is too long.");
			RuleFor(x => x.NewFinancials!.CthPayoutFormula)
				.NotEmpty().WithMessage("CTH payout formula cannot be empty.")
				.MaximumLength(255).WithMessage("CTH payout formula is too long.");
		});
	}

	private async Task<bool> CourseExistsAndIsActiveAsync(Guid courseId, CancellationToken token)
	{
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		return await connection.ExecuteScalarAsync<bool>(
			"SELECT EXISTS (SELECT 1 FROM courses WHERE id = @CourseId AND is_deleted = FALSE)",
			new { CourseId = courseId });
	}

	private async Task<bool> FinancialConfigExistsAndIsValidatedForGroupAsync(Guid configId, Guid groupId, CancellationToken token)
	{
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		return await connection.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM group_financial_configurations 
                WHERE id = @ConfigId AND group_id = @GroupId AND is_validated = TRUE AND is_deleted = FALSE
            )", new { ConfigId = configId, GroupId = groupId });
	}

	private async Task<bool> IsGroupNameUniqueAsync(string newName, Guid currentGroupId, CancellationToken token)
	{
		await using var connection = await _dataSource.OpenConnectionAsync(token);
		return await connection.ExecuteScalarAsync<bool>(@"
            SELECT NOT EXISTS (
                SELECT 1 FROM groups 
                WHERE LOWER(name) = LOWER(@NewName) AND id <> @CurrentGroupId AND is_deleted = FALSE
            )", new { NewName = newName, CurrentGroupId = currentGroupId });
	}
}


[FastEndpoints.HttpPut("/groups/{GroupId:guid}"), Authorize(Policy = Auth0Scopes.ManageGroupSettings)]
public class UpdateGroupEndpoint(
	NpgsqlDataSource dataSource,
	ILogger<UpdateGroupEndpoint> logger)
	: Endpoint<UpdateGroupRequest, UpdateGroupResponse>
{
	public override async Task HandleAsync(UpdateGroupRequest req, CancellationToken ct)
	{
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(auth0UserId))
		{
			await SendResultAsync(TypedResults.Problem(title: "Unauthorized", detail: "User identifier not found.", statusCode: StatusCodes.Status401Unauthorized));
			return;
		}

		var groupIdFromRoute = req.GroupId;

		await using var connection = await dataSource.OpenConnectionAsync(ct);

		var currentUserInfo = await connection.QuerySingleOrDefaultAsync<CurrentUserGolferInfo>(
			"SELECT id AS Id, is_system_admin AS IsSystemAdmin FROM golfers WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;",
			new { Auth0UserId = auth0UserId });

		if (currentUserInfo == null)
		{
			await SendResultAsync(TypedResults.Problem(title: "Forbidden", detail: "User profile not found or inactive.", statusCode: StatusCodes.Status403Forbidden));
			return;
		}

		if (!currentUserInfo.IsSystemAdmin)
		{
			var isScorer = await connection.QuerySingleOrDefaultAsync<bool>(
				"SELECT TRUE FROM group_members WHERE group_id = @GroupId AND golfer_id = @GolferId AND is_scorer = TRUE;",
				new { GroupId = groupIdFromRoute, GolferId = currentUserInfo.Id });
			if (!isScorer)
			{
				await SendResultAsync(TypedResults.Problem(title: "Forbidden", detail: "User is not authorized to update this group.", statusCode: StatusCodes.Status403Forbidden));
				return;
			}
		}
		logger.LogInformation("User {Auth0UserId} (GolferId: {GolferId}) authorized to update group {GroupId}.", auth0UserId, currentUserInfo.Id, groupIdFromRoute);

		var currentGroupData = await connection.QuerySingleOrDefaultAsync<CurrentGroupData>(
			 "SELECT name AS CurrentName, default_course_id AS CurrentDefaultCourseId, active_financial_configuration_id AS CurrentActiveFinancialConfigurationId FROM groups WHERE id = @GroupId AND is_deleted = FALSE;",
			 new { GroupId = groupIdFromRoute });

		if (currentGroupData == null)
		{
			await SendNotFoundAsync(ct); // Group not found or deleted
			return;
		}

		Guid? targetFinancialConfigId = currentGroupData.CurrentActiveFinancialConfigurationId; // Start with current
		bool newFinancialsProvidedAndValid = false;

		if (req.NewFinancials != null) // Highest precedence: create new financials
		{
			var (isValid, validationErrors) = await FinancialValidationService.ValidateConfigurationAsync(
				req.NewFinancials.BuyInAmount, req.NewFinancials.SkinValueFormula, req.NewFinancials.CthPayoutFormula, logger);

			if (!isValid)
			{
				logger.LogWarning("New financial configuration validation failed for group {GroupId}: {Errors}", groupIdFromRoute, string.Join("; ", validationErrors));
				var validationProblem = TypedResults.ValidationProblem(
					title: "New Financial Configuration Validation Failed",
					errors: validationErrors.ToDictionary(e => "NewFinancials", e => new[] { e }), // Simplified error structure
					detail: "The provided new financial configuration is invalid.");
				await SendResultAsync(validationProblem);
				return;
			}
			newFinancialsProvidedAndValid = true;
			// targetFinancialConfigId will be set after inserting this new config inside the transaction
		}
		else if (req.ExistingActiveFinancialConfigurationId.HasValue) // Second precedence: link existing
		{
			// Validator ensures this ID is valid, exists, and belongs to the group.
			targetFinancialConfigId = req.ExistingActiveFinancialConfigurationId.Value;
		}

		await using var transaction = await connection.BeginTransactionAsync(ct);
		try
		{
			if (newFinancialsProvidedAndValid)
			{
				const string insertFinancialsSql = @"
                    INSERT INTO group_financial_configurations 
                        (group_id, buy_in_amount, skin_value_formula, cth_payout_formula, is_validated, validated_at, is_deleted, deleted_at)
                    VALUES (@GroupId, @BuyInAmount, @SkinValueFormula, @CthPayoutFormula, TRUE, NOW(), FALSE, NULL)
                    RETURNING id;";
				targetFinancialConfigId = await connection.ExecuteScalarAsync<Guid>(insertFinancialsSql, new
				{
					GroupId = groupIdFromRoute,
					req.NewFinancials!.BuyInAmount, // Not null due to newFinancialsProvidedAndValid flag
					req.NewFinancials.SkinValueFormula,
					req.NewFinancials.CthPayoutFormula
				}, transaction);
			}

			var updateSetClauses = new List<string>();
			var updateParameters = new DynamicParameters();
			updateParameters.Add("GroupId", groupIdFromRoute);
			updateParameters.Add("UpdatedAt", DateTime.UtcNow); // Always set updated_at

			if (req.Name != null)
			{
				updateSetClauses.Add("name = @Name");
				updateParameters.Add("Name", req.Name);
			}
			if (req.DefaultCourseId.HasValue) // Client can send Guid.Empty to clear, or a valid Guid
			{
				updateSetClauses.Add("default_course_id = @DefaultCourseId");
				updateParameters.Add("DefaultCourseId", req.DefaultCourseId.Value == Guid.Empty ? (Guid?)null : req.DefaultCourseId.Value);
			}
			if (targetFinancialConfigId.HasValue) // This will be the new ID if created, or the existing ID if linked
			{
				updateSetClauses.Add("active_financial_configuration_id = @ActiveFinancialConfigId");
				updateParameters.Add("ActiveFinancialConfigId", targetFinancialConfigId.Value);
			}

			if (updateSetClauses.Count != 0)
			{
				updateSetClauses.Add("updated_at = @UpdatedAt");
				string updateGroupSql = $"UPDATE groups SET {string.Join(", ", updateSetClauses)} WHERE id = @GroupId AND is_deleted = FALSE;";
				await connection.ExecuteAsync(updateGroupSql, updateParameters, transaction);
			}

			await transaction.CommitAsync(ct);
		}
		catch (PostgresException ex)
		{
			await transaction.RollbackAsync(ct);
			logger.LogError(ex, "Database error updating group {GroupId}. SQLState: {SqlState}", groupIdFromRoute, ex.SqlState);
			var problemTitle = "Database Error";
			var problemDetail = "An unexpected database error occurred while updating the group.";
			var statusCode = StatusCodes.Status500InternalServerError;

			if (ex.SqlState == "23505") // Unique violation
			{
				problemTitle = "Conflict";
				problemDetail = $"A group with the name '{req.Name}' already exists, or another unique constraint was violated.";
				statusCode = StatusCodes.Status409Conflict;
			}
			else if (ex.SqlState == "23503") // Foreign key violation
			{
				problemTitle = "Bad Request";
				problemDetail = "Invalid DefaultCourseId or ActiveFinancialConfigurationId. Referenced entity does not exist or is invalid.";
				statusCode = StatusCodes.Status400BadRequest;
			}
			await SendResultAsync(TypedResults.Problem(title: problemTitle, detail: problemDetail, statusCode: statusCode));
			return;
		}
		catch (Exception ex)
		{
			await transaction.RollbackAsync(ct);
			logger.LogError(ex, "Unexpected error updating group {GroupId}.", groupIdFromRoute);
			var generalErrorProblem = TypedResults.Problem(title: "Internal Server Error", detail: "An unexpected error occurred.", statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(generalErrorProblem);
			return;
		}

		// Fetch and return the updated group
		const string selectUpdatedGroupSql = @"
            SELECT 
                g.id AS Id, g.name AS Name, g.default_course_id AS DefaultCourseId,
                g.created_by_golfer_id AS CreatedByGolferId, g.created_at AS CreatedAt, g.updated_at AS UpdatedAt,
                g.is_deleted AS IsDeleted, g.deleted_at AS DeletedAt,
                gfc.id AS ActiveConfig_Id, gfc.group_id AS ActiveConfig_GroupId, 
                gfc.buy_in_amount AS ActiveConfig_BuyInAmount, gfc.skin_value_formula AS ActiveConfig_SkinValueFormula,
                gfc.cth_payout_formula AS ActiveConfig_CthPayoutFormula, gfc.is_validated AS ActiveConfig_IsValidated,
                gfc.created_at AS ActiveConfig_CreatedAt, gfc.validated_at AS ActiveConfig_ValidatedAt,
                gfc.is_deleted AS ActiveConfig_IsDeleted, gfc.deleted_at AS ActiveConfig_DeletedAt
            FROM groups g
            LEFT JOIN group_financial_configurations gfc 
                ON g.active_financial_configuration_id = gfc.id AND gfc.is_deleted = FALSE
            WHERE g.id = @GroupId AND g.is_deleted = FALSE;";

		var resultRow = (await connection.QueryAsync<dynamic>(selectUpdatedGroupSql, new { GroupId = groupIdFromRoute })).FirstOrDefault();

		if (resultRow == null)
		{
			logger.LogError("Failed to fetch group {GroupId} after update operation. This indicates the group may have been deleted or an issue occurred.", groupIdFromRoute);
			await SendNotFoundAsync(ct); // Or 500 if group should definitely exist
			return;
		}

		FinancialConfigurationFromUpdateResponseDTO? activeConfig = null;
		if (resultRow.activeconfig_id != null)
		{
			activeConfig = new FinancialConfigurationFromUpdateResponseDTO(
						Id: (Guid)resultRow.activeconfig_id,
						GroupId: (Guid)resultRow.activeconfig_groupid,
						BuyInAmount: (decimal)resultRow.activeconfig_buyinamount,
						SkinValueFormula: (string)resultRow.activeconfig_skinvalueformula,
						CthPayoutFormula: (string)resultRow.activeconfig_cthpayoutformula,
						IsValidated: (bool)resultRow.activeconfig_isvalidated,
						CreatedAt: (DateTime)resultRow.activeconfig_createdat,
						ValidatedAt: (DateTime?)resultRow.activeconfig_validatedat,
						IsDeleted: (bool)resultRow.isdeleted, 
						DeletedAt: (DateTime?)resultRow.deletedat
			);
		}

		var updatedGroupResponse = new UpdateGroupResponse(
				Id: (Guid)resultRow.id,
					Name: (string)resultRow.name,
					DefaultCourseId: (Guid?)resultRow.defaultcourseid,
					ActiveFinancialConfiguration: activeConfig,
					CreatedByGolferId: (Guid)resultRow.createdbygolferid,
					CreatedAt: (DateTime)resultRow.createdat,
					UpdatedAt: (DateTime)resultRow.updatedat,
					IsDeleted: (bool)resultRow.isdeleted,
					DeletedAt: (DateTime?)resultRow.deletedat
		);

		await SendOkAsync(updatedGroupResponse, ct);
	}
}
