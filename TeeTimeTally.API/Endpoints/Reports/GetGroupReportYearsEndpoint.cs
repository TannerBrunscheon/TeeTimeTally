using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using TeeTimeTally.Shared.Auth;
using TeeTimeTally.API.Services;

namespace TeeTimeTally.API.Endpoints.Reports;

[HttpGet("/groups/{GroupId:guid}/reports/years"), Authorize(Policy = Auth0Scopes.ReadGroupRounds)]
public class GetGroupReportYearsEndpoint : EndpointWithoutRequest<List<int>>
{
    private readonly ReportService _reportService;

    public GetGroupReportYearsEndpoint(ReportService reportService)
    {
        _reportService = reportService;
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var groupId = Guid.Parse(Route<string>("GroupId")!);
        var years = await _reportService.GetGroupReportYearsAsync(groupId, ct);
        await SendOkAsync(years, ct);
    }
}
