using Dapper;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using System.Security.Claims;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Endpoints.Groups;

// --- DTOs for this endpoint ---
public record GetGroupsFinancialConfigurationResponseDTO(
	Guid Id,
	Guid GroupId,
	decimal BuyInAmount,
	string SkinValueFormula,
	string CthPayoutFormula,
	bool IsValidated,
	DateTime CreatedAt,
	DateTime? ValidatedAt
);

public record GetGroupsResponse(
	Guid Id,
	string Name,
	Guid? DefaultCourseId,
	GetGroupsFinancialConfigurationResponseDTO? ActiveFinancialConfiguration,
	Guid CreatedByGolferId,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	bool IsDeleted,
	DateTime? DeletedAt
);

// Helper record to fetch golfer's admin status and internal ID
file record GolferAdminStatus(Guid Id, bool IsSystemAdmin);


[HttpGet("/groups"), Authorize(Policy = Auth0Scopes.ReadGroups)]
public class GetAllGroupsEndpoint(NpgsqlDataSource dataSource, ILogger<GetAllGroupsEndpoint> logger) : EndpointWithoutRequest<IEnumerable<GetGroupsResponse>>
{
	public override async Task HandleAsync(CancellationToken ct)
	{
		var auth0UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(auth0UserId))
		{
			var unauthorizedProblem = TypedResults.Problem(title: "Unauthorized", detail: "User identifier not found.", statusCode: StatusCodes.Status401Unauthorized);
			await SendResultAsync(unauthorizedProblem);
			return;
		}

		bool isSystemAdminUser = false;
		Guid currentGolferUuid = Guid.Empty;

		try
		{
			await using var prelimConnection = await dataSource.OpenConnectionAsync(ct);
			var golferStatus = await prelimConnection.QuerySingleOrDefaultAsync<GolferAdminStatus>(
				"SELECT id AS Id, is_system_admin AS IsSystemAdmin FROM golfers WHERE auth0_user_id = @Auth0UserId AND is_deleted = FALSE;",
				new { Auth0UserId = auth0UserId });

			if (golferStatus == null)
			{
				logger.LogWarning("No active golfer profile found for Auth0 User ID {Auth0UserId}. Returning empty list of groups.", auth0UserId);
				await SendOkAsync(Enumerable.Empty<GetGroupsResponse>(), ct);
				return;
			}
			currentGolferUuid = golferStatus.Id;
			isSystemAdminUser = golferStatus.IsSystemAdmin;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error fetching golfer profile/admin status for Auth0 User ID {Auth0UserId}", auth0UserId);
			var errorProblem = TypedResults.Problem(title: "Service Error", detail: "Could not retrieve user profile information.", statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		string sql;
		object? queryParams;

		if (isSystemAdminUser)
		{
			logger.LogInformation("Admin user {GolferId} fetching all active groups.", currentGolferUuid);
			sql = @"
                SELECT 
                    g.id AS Id, g.name AS Name, g.default_course_id AS DefaultCourseId,
                    g.created_by_golfer_id AS CreatedByGolferId, g.created_at AS CreatedAt, g.updated_at AS UpdatedAt,
                    g.is_deleted AS IsDeleted, g.deleted_at AS DeletedAt,
                    gfc.id AS ActiveConfig_Id, gfc.group_id AS ActiveConfig_GroupId, 
                    gfc.buy_in_amount AS ActiveConfig_BuyInAmount, gfc.skin_value_formula AS ActiveConfig_SkinValueFormula,
                    gfc.cth_payout_formula AS ActiveConfig_CthPayoutFormula, gfc.is_validated AS ActiveConfig_IsValidated,
                    gfc.created_at AS ActiveConfig_CreatedAt, gfc.validated_at AS ActiveConfig_ValidatedAt
                FROM groups g
                LEFT JOIN group_financial_configurations gfc 
                    ON g.active_financial_configuration_id = gfc.id AND gfc.is_deleted = FALSE
                WHERE g.is_deleted = FALSE
                ORDER BY g.name;";
			queryParams = null;
		}
		else
		{
			logger.LogInformation("Non-admin user {GolferId} fetching their groups.", currentGolferUuid);
			sql = @"
                SELECT 
                    g.id AS Id, g.name AS Name, g.default_course_id AS DefaultCourseId,
                    g.created_by_golfer_id AS CreatedByGolferId, g.created_at AS CreatedAt, g.updated_at AS UpdatedAt,
                    g.is_deleted AS IsDeleted, g.deleted_at AS DeletedAt,
                    gfc.id AS ActiveConfig_Id, gfc.group_id AS ActiveConfig_GroupId, 
                    gfc.buy_in_amount AS ActiveConfig_BuyInAmount, gfc.skin_value_formula AS ActiveConfig_SkinValueFormula,
                    gfc.cth_payout_formula AS ActiveConfig_CthPayoutFormula, gfc.is_validated AS ActiveConfig_IsValidated,
                    gfc.created_at AS ActiveConfig_CreatedAt, gfc.validated_at AS ActiveConfig_ValidatedAt
                FROM groups g
                INNER JOIN group_members gm ON g.id = gm.group_id
                LEFT JOIN group_financial_configurations gfc 
                    ON g.active_financial_configuration_id = gfc.id AND gfc.is_deleted = FALSE
                WHERE g.is_deleted = FALSE AND gm.golfer_id = @CurrentGolferId
                ORDER BY g.name;";
			queryParams = new { CurrentGolferId = currentGolferUuid };
		}

		IEnumerable<GetGroupsResponse> groupResponses;

		try
		{
			await using var connection = await dataSource.OpenConnectionAsync(ct);
			var resultRows = await connection.QueryAsync<dynamic>(sql, queryParams);

			groupResponses = resultRows.Select(row =>
			{
				GetGroupsFinancialConfigurationResponseDTO? activeConfig = null;
				// Access properties directly from the dynamic row using the exact casing provided
				if (row.activeconfig_id != null) // Check if the property exists and is not null
				{
					activeConfig = new GetGroupsFinancialConfigurationResponseDTO(
						Id: (Guid)row.activeconfig_id,
						GroupId: (Guid)row.activeconfig_groupid,
						BuyInAmount: (decimal)row.activeconfig_buyinamount,
						SkinValueFormula: (string)row.activeconfig_skinvalueformula,
						CthPayoutFormula: (string)row.activeconfig_cthpayoutformula,
						IsValidated: (bool)row.activeconfig_isvalidated,
						CreatedAt: (DateTime)row.activeconfig_createdat,
						ValidatedAt: (DateTime?)row.activeconfig_validatedat
					);
				}
				return new GetGroupsResponse(
					Id: (Guid)row.id,
					Name: (string)row.name,
					DefaultCourseId: (Guid?)row.defaultcourseid,
					ActiveFinancialConfiguration: activeConfig,
					CreatedByGolferId: (Guid)row.createdbygolferid,
					CreatedAt: (DateTime)row.createdat,
					UpdatedAt: (DateTime)row.updatedat,
					IsDeleted: (bool)row.isdeleted,
					DeletedAt: (DateTime?)row.deletedat
				);
			}).ToList();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error fetching groups. Admin query: {IsAdmin}", isSystemAdminUser);
			var errorProblem = TypedResults.Problem(
				title: "Database Error",
				detail: "An error occurred while fetching groups data.",
				statusCode: StatusCodes.Status500InternalServerError);
			await SendResultAsync(errorProblem);
			return;
		}

		await SendOkAsync(groupResponses, ct);
	}
}