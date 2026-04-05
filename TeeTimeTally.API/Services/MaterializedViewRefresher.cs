using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace TeeTimeTally.API.Services;

public class MaterializedViewRefresher : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<MaterializedViewRefresher> _logger;
    private readonly TimeSpan _interval;

    public MaterializedViewRefresher(NpgsqlDataSource dataSource, IConfiguration config, ILogger<MaterializedViewRefresher> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
        var minutes = config.GetValue<int?>("MaterializedViewRefresh:IntervalMinutes") ?? 1440; // default once per day
        _interval = TimeSpan.FromMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MaterializedViewRefresher started. Refresh interval: {Interval}", _interval);

        // Initial delay so the app can warm up; wait 30s before first refresh
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshViewsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while refreshing materialized views");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RefreshViewsAsync(CancellationToken ct)
    {
        _logger.LogInformation("Refreshing materialized views...");
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
    await conn.ExecuteAsync("REFRESH MATERIALIZED VIEW CONCURRENTLY mv_round_player_diffs;");
    await conn.ExecuteAsync("REFRESH MATERIALIZED VIEW CONCURRENTLY mv_round_team_diffs;");
        _logger.LogInformation("Materialized views refreshed.");
    }
}
