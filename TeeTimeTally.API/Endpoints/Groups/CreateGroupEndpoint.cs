using Dapper;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.API.Services;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Endpoints.Groups;

// --- DTOs for this endpoint ---

// This DTO is used within CreateGroupRequest
public record CreateGroupFinancialConfigurationInputDTO(
	decimal BuyInAmount,
	string SkinValueFormula,
	string CthPayoutFormula
);

public record CreateGroupFinancialConfigurationResponseDTO(
	Guid Id,
	Guid GroupId,
	decimal BuyInAmount,
	string SkinValueFormula,
	string CthPayoutFormula,
	bool IsValidated,
	DateTime CreatedAt,
	DateTime? ValidatedAt
);

public record CreateGroupRequest(
	string Name,
	Guid? DefaultCourseId, // Optional
	CreateGroupFinancialConfigurationInputDTO? OptionalInitialFinancials // Optional
);

public record CreateGroupResponse(
	Guid Id,
	string Name,
	Guid? DefaultCourseId,
	CreateGroupFinancialConfigurationResponseDTO? ActiveFinancialConfiguration, // Full DTO
	Guid CreatedByGolferId,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	bool IsDeleted,
	DateTime? DeletedAt
);

[HttpPost("/groups"), Authorize(Policy = Auth0Scopes.CreateGroups)]
public class CreateGroupEndpoint(NpgsqlDataSource dataSource, ILogger<CreateGroupEndpoint> logger) : Endpoint<CreateGroupRequest, CreateGroupResponse>
{
	public override async Task HandleAsync(CreateGroupRequest req, CancellationToken ct)
	{
		var creatorGolferIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(creatorGolferIdString) || !Guid.TryParse(creatorGolferIdString, out var creatorGolferId))
		{
			var unauthorizedProblem = TypedResults.Problem(
				title: "Unauthorized", detail: "User identifier is missing or invalid.", statusCode: StatusCodes.Status401Unauthorized);
			await SendResultAsync(unauthorizedProblem);
			return;
		}

		Guid newGroupId = Guid.NewGuid();
		Guid? newFinancialConfigId = null;

		await using var connection = await dataSource.OpenConnectionAsync(ct);
		await using var transaction = await connection.BeginTransactionAsync(ct);

		try
		{
			const string insertGroupSql = @"
                INSERT INTO groups (id, name, default_course_id, created_by_golfer_id, active_financial_configuration_id)
                VALUES (@Id, @Name, @DefaultCourseId, @CreatedByGolferId, NULL)
                RETURNING id;";

			await connection.ExecuteScalarAsync<Guid>(insertGroupSql, new
			{
				Id = newGroupId,
				req.Name,
				req.DefaultCourseId,
				CreatedByGolferId = creatorGolferId
			}, transaction);

			if (req.OptionalInitialFinancials != null)
			{
				// Call validation service with individual parameters
				var validationResult = await FinancialValidationService.ValidateConfigurationAsync(
					req.OptionalInitialFinancials.BuyInAmount,
					req.OptionalInitialFinancials.SkinValueFormula,
					req.OptionalInitialFinancials.CthPayoutFormula,
					logger
				);

				if (!validationResult.IsValid)
				{
					// Construct a dictionary for ValidationProblem details
					var errorsDict = new Dictionary<string, string[]>();
					if (validationResult.Errors.Count != 0)
					{
						errorsDict.Add("OptionalInitialFinancials", validationResult.Errors.ToArray());
					}

					var validationProblem = TypedResults.ValidationProblem(
						title: "Financial Configuration Validation Failed",
						errors: errorsDict,
						detail: "The provided initial financial configuration is invalid."
					);
					await SendResultAsync(validationProblem);
					await transaction.RollbackAsync(ct);
					return;
				}

				const string insertFinancialsSql = @"
                    INSERT INTO group_financial_configurations 
                        (group_id, buy_in_amount, skin_value_formula, cth_payout_formula, is_validated, validated_at)
                    VALUES (@GroupId, @BuyInAmount, @SkinValueFormula, @CthPayoutFormula, TRUE, NOW())
                    RETURNING id;";

				newFinancialConfigId = await connection.ExecuteScalarAsync<Guid>(insertFinancialsSql, new
				{
					GroupId = newGroupId,
					req.OptionalInitialFinancials.BuyInAmount,
					req.OptionalInitialFinancials.SkinValueFormula,
					req.OptionalInitialFinancials.CthPayoutFormula
				}, transaction);

				const string updateGroupSql = @"
                    UPDATE groups 
                    SET active_financial_configuration_id = @ActiveFinancialConfigId, updated_at = NOW()
                    WHERE id = @GroupId;";
				await connection.ExecuteAsync(updateGroupSql, new
				{
					ActiveFinancialConfigId = newFinancialConfigId,
					GroupId = newGroupId
				}, transaction);
			}

			await transaction.CommitAsync(ct);

			const string selectGroupSql = @"
                SELECT 
                    g.id AS Id, 
                    g.name AS Name, 
                    g.default_course_id AS DefaultCourseId,
                    g.created_by_golfer_id AS CreatedByGolferId,
                    g.created_at AS CreatedAt, 
                    g.updated_at AS UpdatedAt,
                    g.is_deleted AS IsDeleted,
                    g.deleted_at AS DeletedAt,
                    gfc.id AS GfcId, 
                    gfc.group_id AS GfcGroupId, 
                    gfc.buy_in_amount AS GfcBuyInAmount,
                    gfc.skin_value_formula AS GfcSkinValueFormula,
                    gfc.cth_payout_formula AS GfcCthPayoutFormula,
                    gfc.is_validated AS GfcIsValidated,
                    gfc.created_at AS GfcCreatedAt,
                    gfc.validated_at AS GfcValidatedAt
                FROM groups g
                LEFT JOIN group_financial_configurations gfc ON g.active_financial_configuration_id = gfc.id AND gfc.is_deleted = FALSE
                WHERE g.id = @GroupId AND g.is_deleted = FALSE;";

			var resultRow = (await connection.QueryAsync(selectGroupSql, new { GroupId = newGroupId })).FirstOrDefault();

			if (resultRow == null)
			{
				var fetchErrorProblem = TypedResults.Problem(title: "Fetch Error", detail: "Could not retrieve the newly created group.", statusCode: StatusCodes.Status500InternalServerError);
				await SendResultAsync(fetchErrorProblem);
				return;
			}

			CreateGroupFinancialConfigurationResponseDTO? activeConfig = null;
			if (resultRow.GfcId != null) // Check if financial config data exists
			{
				activeConfig = new CreateGroupFinancialConfigurationResponseDTO(
					(Guid)resultRow.GfcId, (Guid)resultRow.GfcGroupId, (decimal)resultRow.GfcBuyInAmount,
					(string)resultRow.GfcSkinValueFormula, (string)resultRow.GfcCthPayoutFormula, (bool)resultRow.GfcIsValidated,
					(DateTime)resultRow.GfcCreatedAt, (DateTime?)resultRow.GfcValidatedAt
				);
			}

			var response = new CreateGroupResponse(
				(Guid)resultRow.Id, (string)resultRow.Name, (Guid?)resultRow.DefaultCourseId, activeConfig,
				(Guid)resultRow.CreatedByGolferId, (DateTime)resultRow.CreatedAt, (DateTime)resultRow.UpdatedAt,
				(bool)resultRow.IsDeleted, (DateTime?)resultRow.DeletedAt
			);

			await SendCreatedAtAsync<GetGroupByIdEndpoint>(
				routeValues: new { response.Id },
				responseBody: response,
				cancellation: ct);
		}
		catch (PostgresException ex)
		{
			await transaction.RollbackAsync(ct);
			logger.LogError(ex, "Database error during group creation transaction. SQLState: {SqlState}", ex.SqlState);
			var problemDetails = TypedResults.Problem(
				title: ex.SqlState == "23505" ? "Conflict" : "Database Error",
				detail: ex.SqlState == "23505" ? $"A group with the name '{req.Name}' already exists or another unique constraint was violated." : "An unexpected database error occurred.",
				statusCode: ex.SqlState == "23505" ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError
			);
			await SendResultAsync(problemDetails);
			return;
		}
		catch (Exception ex)
		{
			await transaction.RollbackAsync(ct);
			logger.LogError(ex, "Unexpected error during group creation transaction.");
			var generalErrorProblem = TypedResults.Problem(title: "Internal Server Error", detail: "An unexpected error occurred.", statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(generalErrorProblem);
			return;
		}
	}
}