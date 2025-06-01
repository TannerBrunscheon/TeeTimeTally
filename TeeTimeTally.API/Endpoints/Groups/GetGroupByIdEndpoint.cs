using Dapper;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Endpoints.Groups;

// --- DTOs for this endpoint ---

public class GetGroupByIdRequest
{
	public Guid Id { get; set; }
}

public record GetGroupFinancialConfigurationResponseDTO(
	Guid Id,
	Guid GroupId,
	decimal BuyInAmount,
	string SkinValueFormula,
	string CthPayoutFormula,
	bool IsValidated,
	DateTime CreatedAt,
	DateTime? ValidatedAt
);

public record GetGroupResponse(
	Guid Id,
	string Name,
	Guid? DefaultCourseId,
	GetGroupFinancialConfigurationResponseDTO? ActiveFinancialConfiguration,
	Guid CreatedByGolferId,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	bool IsDeleted,
	DateTime? DeletedAt
);

[HttpGet("/groups/{Id}"), Authorize(Policy = Auth0Scopes.ReadGroups)]
public class GetGroupByIdEndpoint(NpgsqlDataSource dataSource, ILogger<GetGroupByIdEndpoint> logger) : Endpoint<GetGroupByIdRequest, GetGroupResponse>
{
	public override async Task HandleAsync(GetGroupByIdRequest req, CancellationToken ct)
	{
		const string sql = @"
            SELECT 
                g.id AS Id, 
                g.name AS Name, 
                g.default_course_id AS DefaultCourseId,
                g.created_by_golfer_id AS CreatedByGolferId,
                g.created_at AS CreatedAt, 
                g.updated_at AS UpdatedAt,
                g.is_deleted AS IsDeleted,
                g.deleted_at AS DeletedAt,
                gfc.id AS ActiveConfig_Id, 
                gfc.group_id AS ActiveConfig_GroupId, 
                gfc.buy_in_amount AS ActiveConfig_BuyInAmount,
                gfc.skin_value_formula AS ActiveConfig_SkinValueFormula,
                gfc.cth_payout_formula AS ActiveConfig_CthPayoutFormula,
                gfc.is_validated AS ActiveConfig_IsValidated,
                gfc.created_at AS ActiveConfig_CreatedAt,
                gfc.validated_at AS ActiveConfig_ValidatedAt
            FROM groups g
            LEFT JOIN group_financial_configurations gfc 
                ON g.active_financial_configuration_id = gfc.id AND gfc.is_deleted = FALSE
            WHERE g.id = @Id AND g.is_deleted = FALSE;";

		GetGroupResponse? groupResponse = null;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);
			var resultRow = (await connection.QueryAsync<dynamic>(sql, new { req.Id })).FirstOrDefault();

			if (resultRow != null)
			{
				GetGroupFinancialConfigurationResponseDTO? activeConfig = null;
				if (resultRow.ActiveConfig_Id != null)
				{
					activeConfig = new GetGroupFinancialConfigurationResponseDTO(
						Id: (Guid)resultRow.ActiveConfig_Id,
						GroupId: (Guid)resultRow.ActiveConfig_GroupId,
						BuyInAmount: (decimal)resultRow.ActiveConfig_BuyInAmount,
						SkinValueFormula: (string)resultRow.ActiveConfig_SkinValueFormula,
						CthPayoutFormula: (string)resultRow.ActiveConfig_CthPayoutFormula,
						IsValidated: (bool)resultRow.ActiveConfig_IsValidated,
						CreatedAt: (DateTime)resultRow.ActiveConfig_CreatedAt,
						ValidatedAt: (DateTime?)resultRow.ActiveConfig_ValidatedAt
					);
				}

				groupResponse = new GetGroupResponse(
					Id: (Guid)resultRow.Id,
					Name: (string)resultRow.Name,
					DefaultCourseId: (Guid?)resultRow.DefaultCourseId,
					ActiveFinancialConfiguration: activeConfig,
					CreatedByGolferId: (Guid)resultRow.CreatedByGolferId,
					CreatedAt: (DateTime)resultRow.CreatedAt,
					UpdatedAt: (DateTime)resultRow.UpdatedAt,
					IsDeleted: (bool)resultRow.IsDeleted,
					DeletedAt: (DateTime?)resultRow.DeletedAt
				);
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error fetching group with ID {GroupId}.", req.Id); // Use injected _logger
			var errorProblem = TypedResults.Problem(
				title: "Database Error",
				detail: "An error occurred while fetching group data.",
				statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		if (groupResponse == null)
		{
			await SendNotFoundAsync(ct);
			return;
		}

		await SendOkAsync(groupResponse, ct);
	}
}