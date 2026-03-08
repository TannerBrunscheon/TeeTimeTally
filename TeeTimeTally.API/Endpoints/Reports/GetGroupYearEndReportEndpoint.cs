
using Dapper;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using TeeTimeTally.Shared.Auth;
using TeeTimeTally.Shared.Reports;
using TeeTimeTally.API.Services;

namespace TeeTimeTally.API.Endpoints.Reports;

[HttpGet("/groups/{GroupId:guid}/reports/year/{Year:int}"), Authorize(Policy = Auth0Scopes.ReadGroupRounds)]
public class GetGroupYearEndReportEndpoint : EndpointWithoutRequest<GroupYearEndReportDto>
{
    private readonly ReportService _reportService;

    public GetGroupYearEndReportEndpoint(ReportService reportService)
    {
        _reportService = reportService;
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var groupId = Guid.Parse(Route<string>("GroupId")!);
        var year = int.Parse(Route<string>("Year")!);

        var report = await _reportService.GetGroupYearEndReportAsync(groupId, year, ct);
        await SendOkAsync(report, ct);
    }
}