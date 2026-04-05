using Dapper;
using Npgsql;
using TeeTimeTally.Shared.Reports;

namespace TeeTimeTally.API.Services;

public class ReportService
{
    private readonly NpgsqlDataSource _dataSource;

    public ReportService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<GroupYearEndReportDto> GetGroupYearEndReportAsync(Guid groupId, int year, CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct);

        const string roundsSql = @"
            SELECT id FROM rounds
            WHERE group_id = @GroupId AND status = 'Finalized' AND date_part('year', round_date) = @Year;
        ";
        var roundIds = (await connection.QueryAsync<Guid>(roundsSql, new { GroupId = groupId, Year = year })).ToList();

        if (!roundIds.Any())
        {
            return new GroupYearEndReportDto(groupId, year, new List<PlayerYearStatsDto>(), null, new GroupYearSummaryDto(groupId, 0, 0m, 0m, 0m, 0m));
        }
        const string playersSql = @"
            SELECT p.golfer_id AS GolferId, g.full_name AS FullName, COUNT(DISTINCT p.round_id) AS TimesPlayed
            FROM round_participants p
            JOIN golfers g ON p.golfer_id = g.id
            WHERE p.round_id = ANY(@RoundIds)
            GROUP BY p.golfer_id, g.full_name;
        ";
        var players = (await connection.QueryAsync(playersSql, new { RoundIds = roundIds.ToArray() }))
            .Select(r => new PlayerYearStatsDto((Guid)r.golferid, (string)r.fullname, (int)r.timesplayed, 0m, 0m, 0m))
            .ToDictionary(p => p.GolferId);

        const string payoutsSql = @"
            SELECT golfer_id AS GolferId, SUM(total_winnings) AS Net
            FROM round_payout_summary
            WHERE round_id = ANY(@RoundIds)
            GROUP BY golfer_id;
        ";
        var payouts = (await connection.QueryAsync(payoutsSql, new { RoundIds = roundIds.ToArray() }))
            .ToDictionary(r => (Guid)r.golferid, r => (decimal)r.net);

        foreach (var kv in players.ToList())
        {
            if (payouts.TryGetValue(kv.Key, out var net))
            {
                players[kv.Key] = kv.Value with { NetWinnings = Math.Round(net, 2) };
            }
        }

        const string vsParSql = @"
            SELECT rp.golfer_id AS GolferId, AVG(player_round_diff)::numeric AS AvgVsParPerRound
            FROM (
                SELECT rp.golfer_id, rs.round_id,
                       SUM(rs.score - ch.par) AS player_round_diff
                FROM round_scores rs
                JOIN round_teams rt ON rs.round_team_id = rt.id
                JOIN round_participants rp ON rp.round_id = rs.round_id AND rp.team_id = rt.id
                JOIN course_holes ch ON ch.course_id = rt.course_id AND ch.hole_number = rs.hole_number
                WHERE rs.round_id = ANY(@RoundIds)
                GROUP BY rp.golfer_id, rs.round_id
            ) t
            GROUP BY rp.golfer_id;
        ";
        var vsPars = (await connection.QueryAsync(vsParSql, new { RoundIds = roundIds.ToArray() }))
            .ToDictionary(r => (Guid)r.golferid, r => (decimal)r.avgvsparperround);

        foreach (var kv in players.ToList())
        {
            if (vsPars.TryGetValue(kv.Key, out var avg))
            {
                players[kv.Key] = kv.Value with { AvgVsParPerRound = Math.Round(avg, 2) };
            }
        }

        const string vsParMedianSql = @"
            SELECT golfer_id AS GolferId,
                   percentile_cont(0.5) WITHIN GROUP (ORDER BY player_round_diff)::numeric AS MedianVsParPerRound
            FROM (
                SELECT rp.golfer_id, rs.round_id,
                       SUM(rs.score - ch.par) AS player_round_diff
                FROM round_scores rs
                JOIN round_teams rt ON rs.round_team_id = rt.id
                JOIN round_participants rp ON rp.round_id = rs.round_id AND rp.team_id = rt.id
                JOIN course_holes ch ON ch.course_id = rt.course_id AND ch.hole_number = rs.hole_number
                WHERE rs.round_id = ANY(@RoundIds)
                GROUP BY rp.golfer_id, rs.round_id
            ) t
            GROUP BY golfer_id;
        ";
        var vsParsMedian = (await connection.QueryAsync(vsParMedianSql, new { RoundIds = roundIds.ToArray() }))
            .ToDictionary(r => (Guid)r.golferid, r => (decimal?)r.medianvsparperround);

        foreach (var kv in players.ToList())
        {
            if (vsParsMedian.TryGetValue(kv.Key, out var med) && med.HasValue)
            {
                players[kv.Key] = kv.Value with { MedianVsParPerRound = Math.Round(med.Value, 2) };
            }
        }

        const string groupAvgSql = @"
            SELECT AVG(team_round_diff)::numeric AS AvgGroupVsPar
            FROM (
                SELECT rs.round_id, rs.round_team_id, SUM(rs.score - ch.par) AS team_round_diff
                FROM round_scores rs
                JOIN round_teams rt ON rs.round_team_id = rt.id
                JOIN course_holes ch ON ch.course_id = rt.course_id AND ch.hole_number = rs.hole_number
                WHERE rs.round_id = ANY(@RoundIds)
                GROUP BY rs.round_id, rs.round_team_id
            ) t;
        ";
        var groupAvg = (await connection.QuerySingleAsync<decimal?>(groupAvgSql, new { RoundIds = roundIds.ToArray() })) ?? 0m;

        const string groupMedianSql = @"
            SELECT percentile_cont(0.5) WITHIN GROUP (ORDER BY team_round_diff)::numeric AS MedianGroupVsPar
            FROM (
                SELECT rs.round_id, rs.round_team_id, SUM(rs.score - ch.par) AS team_round_diff
                FROM round_scores rs
                JOIN round_teams rt ON rs.round_team_id = rt.id
                JOIN course_holes ch ON ch.course_id = rt.course_id AND ch.hole_number = rs.hole_number
                WHERE rs.round_id = ANY(@RoundIds)
                GROUP BY rs.round_id, rs.round_team_id
            ) t;
        ";
        var groupMedian = (await connection.QuerySingleAsync<decimal?>(groupMedianSql, new { RoundIds = roundIds.ToArray() })) ?? 0m;

        const string potSql = @"
            SELECT COALESCE(SUM(total_pot),0)::numeric AS TotalPotSum, COALESCE(MAX(total_pot),0)::numeric AS MaxPot
            FROM rounds
            WHERE id = ANY(@RoundIds);
        ";
        var pot = await connection.QuerySingleAsync(potSql, new { RoundIds = roundIds.ToArray() });
        decimal totalPot = pot.totalpotsum is decimal d1 ? Math.Round(d1, 2) : 0m;
        decimal maxPot = pot.maxpot is decimal d2 ? Math.Round(d2, 2) : 0m;

        var playersList = players.Values.OrderByDescending(p => p.TimesPlayed).ToList();
        var bestPlayer = playersList.OrderBy(p => p.AvgVsParPerRound).FirstOrDefault();

        var summary = new GroupYearSummaryDto(groupId, roundIds.Count, Math.Round(groupAvg, 2), Math.Round(groupMedian, 2), totalPot, maxPot);
        return new GroupYearEndReportDto(groupId, year, playersList, bestPlayer, summary);
    }

    public async Task<List<int>> GetGroupReportYearsAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        const string sql = @"
            SELECT DISTINCT date_part('year', round_date)::int AS Year
            FROM rounds
            WHERE group_id = @GroupId AND status = 'Finalized'
            ORDER BY Year DESC;
        ";
        var years = (await connection.QueryAsync<int>(sql, new { GroupId = groupId })).ToList();
        return years;
    }
}
