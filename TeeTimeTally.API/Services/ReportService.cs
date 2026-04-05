using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            var emptySummary = new GroupYearSummaryDto(groupId, 0, null, null, 0m, 0m);
            return new GroupYearEndReportDto(groupId, year, new List<PlayerYearStatsDto>(), null, null, null, null, null, null, emptySummary);
        }
        const string playersSql = @"
            SELECT p.golfer_id AS GolferId, g.full_name AS FullName, COUNT(DISTINCT p.round_id) AS TimesPlayed
            FROM round_participants p
            JOIN golfers g ON p.golfer_id = g.id
            WHERE p.round_id = ANY(@RoundIds)
            GROUP BY p.golfer_id, g.full_name;
        ";
        var players = (await connection.QueryAsync(playersSql, new { RoundIds = roundIds.ToArray() }))
            .Select(r => new PlayerYearStatsDto((Guid)r.golferid, (string)r.fullname, (int)r.timesplayed, 0m, null, null, 0))
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

        // We don't currently collect hole-by-hole par data, so compute averages/medians
        // over total round scores (lower is better). These populate the same DTO
        // fields previously named 'vs par' so UI continues to work without DB schema changes.
        const string vsParSql = @"
            SELECT t.golfer_id AS GolferId, AVG(player_round_score_total)::numeric AS AvgVsParPerRound
            FROM (
                SELECT rp.golfer_id, rs.round_id,
                       SUM(rs.score) AS player_round_score_total
                FROM round_scores rs
                JOIN round_teams rt ON rs.round_team_id = rt.id
                JOIN round_participants rp ON rp.round_id = rs.round_id AND rp.round_team_id = rt.id
                WHERE rs.round_id = ANY(@RoundIds)
                GROUP BY rp.golfer_id, rs.round_id
            ) t
            GROUP BY t.golfer_id;
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
                   percentile_cont(0.5) WITHIN GROUP (ORDER BY player_round_score_total)::numeric AS MedianVsParPerRound
            FROM (
                SELECT rp.golfer_id, rs.round_id,
                       SUM(rs.score) AS player_round_score_total
                FROM round_scores rs
                JOIN round_teams rt ON rs.round_team_id = rt.id
                JOIN round_participants rp ON rp.round_id = rs.round_id AND rp.round_team_id = rt.id
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
            SELECT AVG(team_round_score_total)::numeric AS AvgGroupVsPar
            FROM (
                SELECT rs.round_id, rs.round_team_id, SUM(rs.score) AS team_round_score_total
                FROM round_scores rs
                JOIN round_teams rt ON rs.round_team_id = rt.id
                WHERE rs.round_id = ANY(@RoundIds)
                GROUP BY rs.round_id, rs.round_team_id
            ) t;
        ";
    var groupAvg = await connection.QuerySingleAsync<decimal?>(groupAvgSql, new { RoundIds = roundIds.ToArray() });

        const string groupMedianSql = @"
            SELECT percentile_cont(0.5) WITHIN GROUP (ORDER BY team_round_score_total)::numeric AS MedianGroupVsPar
            FROM (
                SELECT rs.round_id, rs.round_team_id, SUM(rs.score) AS team_round_score_total
                FROM round_scores rs
                JOIN round_teams rt ON rs.round_team_id = rt.id
                WHERE rs.round_id = ANY(@RoundIds)
                GROUP BY rs.round_id, rs.round_team_id
            ) t;
        ";
    var groupMedian = await connection.QuerySingleAsync<decimal?>(groupMedianSql, new { RoundIds = roundIds.ToArray() });

        const string potSql = @"
            SELECT COALESCE(SUM(total_pot),0)::numeric AS TotalPotSum, COALESCE(MAX(total_pot),0)::numeric AS MaxPot
            FROM rounds
            WHERE id = ANY(@RoundIds);
        ";
        var pot = await connection.QuerySingleAsync(potSql, new { RoundIds = roundIds.ToArray() });
        decimal totalPot = pot.totalpotsum is decimal d1 ? Math.Round(d1, 2) : 0m;
        decimal maxPot = pot.maxpot is decimal d2 ? Math.Round(d2, 2) : 0m;

    var playersList = players.Values.OrderByDescending(p => p.TimesPlayed).ToList();

    // Closest-to-the-hole (CTH) counts per golfer
    const string cthSql = @"
        SELECT cth_winner_golfer_id AS GolferId, COUNT(*)::int AS CthCount
        FROM rounds
        WHERE id = ANY(@RoundIds) AND cth_winner_golfer_id IS NOT NULL
        GROUP BY cth_winner_golfer_id;
    ";
    var cthCounts = (await connection.QueryAsync(cthSql, new { RoundIds = roundIds.ToArray() }))
        .ToDictionary(r => (Guid)r.golferid, r => (int)r.cthcount);

    foreach (var kv in players.ToList())
    {
        if (cthCounts.TryGetValue(kv.Key, out var cth))
        {
            players[kv.Key] = kv.Value with { ClosestToHoleCount = cth };
        }
    }

    // Prefer selecting best players from the computed SQL maps to avoid
    // ordering/assignment timing issues. These maps contain the raw numbers
    // we used to populate the player DTOs above.
    PlayerYearStatsDto? bestPlayer = null;
    PlayerYearStatsDto? bestPlayerByMedian = null;
    PlayerYearStatsDto? bestPlayerByCth = null;

    if (vsPars.Any())
    {
        var bestId = vsPars.OrderBy(kv => kv.Value).First().Key;
        players.TryGetValue(bestId, out bestPlayer);
    }

    if (vsParsMedian.Any(kv => kv.Value.HasValue))
    {
        var bestMedianId = vsParsMedian.Where(kv => kv.Value.HasValue).OrderBy(kv => kv.Value!.Value).First().Key;
        players.TryGetValue(bestMedianId, out bestPlayerByMedian);
    }

    if (cthCounts.Any())
    {
        var bestCthId = cthCounts.OrderByDescending(kv => kv.Value).First().Key;
        players.TryGetValue(bestCthId, out bestPlayerByCth);
    }

    // Team-level stats: compute per-team average and best single-round score
    const string bestTeamByAvgSql = @"
        SELECT rt.id AS TeamId, rt.team_name_or_number AS TeamName, AVG(t.team_round_score_total)::numeric AS AvgScorePerRound, MIN(t.team_round_score_total)::numeric AS BestRoundScore
        FROM (
            SELECT rs.round_id, rs.round_team_id, SUM(rs.score) AS team_round_score_total
            FROM round_scores rs
            WHERE rs.round_id = ANY(@RoundIds)
            GROUP BY rs.round_id, rs.round_team_id
        ) t
        JOIN round_teams rt ON rt.id = t.round_team_id
        GROUP BY rt.id, rt.team_name_or_number
        ORDER BY AvgScorePerRound ASC
        LIMIT 1;";
    var bestTeamByAvgRow = await connection.QueryFirstOrDefaultAsync(bestTeamByAvgSql, new { RoundIds = roundIds.ToArray() });

    const string bestTeamBestRoundSql = @"
        SELECT rt.id AS TeamId, rt.team_name_or_number AS TeamName, AVG(t.team_round_score_total)::numeric AS AvgScorePerRound, MIN(t.team_round_score_total)::numeric AS BestRoundScore
        FROM (
            SELECT rs.round_id, rs.round_team_id, SUM(rs.score) AS team_round_score_total
            FROM round_scores rs
            WHERE rs.round_id = ANY(@RoundIds)
            GROUP BY rs.round_id, rs.round_team_id
        ) t
        JOIN round_teams rt ON rt.id = t.round_team_id
        GROUP BY rt.id, rt.team_name_or_number
        ORDER BY BestRoundScore ASC
        LIMIT 1;";
    var bestTeamBestRoundRow = await connection.QueryFirstOrDefaultAsync(bestTeamBestRoundSql, new { RoundIds = roundIds.ToArray() });

    TeamYearStatsDto? bestTeamByAvg = null;
    TeamYearStatsDto? bestTeamBestRound = null;
    if (bestTeamByAvgRow != null)
    {
        var teamId = (Guid)bestTeamByAvgRow.teamid;
        var teamName = (string)bestTeamByAvgRow.teamname;
    decimal? avgScore = (bestTeamByAvgRow.avgscoreperround is decimal da) ? (decimal?)Math.Round(da, 2) : null;
    decimal? bestRound = (bestTeamByAvgRow.bestroundscore is decimal db) ? (decimal?)Math.Round(db, 2) : null;

        const string teamMembersSql = @"
            SELECT DISTINCT rp.golfer_id AS GolferId, g.full_name AS FullName
            FROM round_participants rp
            JOIN golfers g ON rp.golfer_id = g.id
            WHERE rp.round_team_id = @TeamId AND rp.round_id = ANY(@RoundIds)
            ORDER BY g.full_name;
        ";
        var members = (await connection.QueryAsync(teamMembersSql, new { TeamId = teamId, RoundIds = roundIds.ToArray() }))
            .Select(r => new TeamMemberDto((Guid)r.golferid, (string)r.fullname)).ToList();

        const string teamRoundsSql = @"
            SELECT COUNT(DISTINCT rp.round_id)::int FROM round_participants rp WHERE rp.round_team_id = @TeamId AND rp.round_id = ANY(@RoundIds);
        ";
        var roundsCount = await connection.QuerySingleAsync<int>(teamRoundsSql, new { TeamId = teamId, RoundIds = roundIds.ToArray() });

        bestTeamByAvg = new TeamYearStatsDto(teamId, teamName, avgScore, bestRound, members, roundsCount);
    }
    if (bestTeamBestRoundRow != null)
    {
        var teamId = (Guid)bestTeamBestRoundRow.teamid;
        var teamName = (string)bestTeamBestRoundRow.teamname;
    decimal? avgScore = (bestTeamBestRoundRow.avgscoreperround is decimal dc) ? (decimal?)Math.Round(dc, 2) : null;
    decimal? bestRound = (bestTeamBestRoundRow.bestroundscore is decimal dd) ? (decimal?)Math.Round(dd, 2) : null;

        const string teamMembersSql = @"
            SELECT DISTINCT rp.golfer_id AS GolferId, g.full_name AS FullName
            FROM round_participants rp
            JOIN golfers g ON rp.golfer_id = g.id
            WHERE rp.round_team_id = @TeamId AND rp.round_id = ANY(@RoundIds)
            ORDER BY g.full_name;
        ";
        var members = (await connection.QueryAsync(teamMembersSql, new { TeamId = teamId, RoundIds = roundIds.ToArray() }))
            .Select(r => new TeamMemberDto((Guid)r.golferid, (string)r.fullname)).ToList();

        const string teamRoundsSql = @"
            SELECT COUNT(DISTINCT rp.round_id)::int FROM round_participants rp WHERE rp.round_team_id = @TeamId AND rp.round_id = ANY(@RoundIds);
        ";
        var roundsCount = await connection.QuerySingleAsync<int>(teamRoundsSql, new { TeamId = teamId, RoundIds = roundIds.ToArray() });

        bestTeamBestRound = new TeamYearStatsDto(teamId, teamName, avgScore, bestRound, members, roundsCount);
    }

    // Compute most-played-together teams (by rounds count). If tied, include all.
    const string teamsByRoundsSql = @"
        SELECT rt.id AS TeamId, rt.team_name_or_number AS TeamName, COUNT(DISTINCT rp.round_id)::int AS RoundsCount
        FROM round_participants rp
        JOIN round_teams rt ON rp.round_team_id = rt.id
        WHERE rp.round_id = ANY(@RoundIds)
        GROUP BY rt.id, rt.team_name_or_number
        ORDER BY RoundsCount DESC;
    ";
    var teamsByRounds = (await connection.QueryAsync(teamsByRoundsSql, new { RoundIds = roundIds.ToArray() }))
        .Select(r => new { TeamId = (Guid)r.teamid, TeamName = (string)r.teamname, RoundsCount = (int)r.roundscount })
        .ToList();

    List<MostPlayedTeamDto>? mostPlayedTeams = null;
    if (teamsByRounds.Any())
    {
        var topCount = teamsByRounds.Max(t => t.RoundsCount);
        var topTeams = teamsByRounds.Where(t => t.RoundsCount == topCount).ToList();
        mostPlayedTeams = new List<MostPlayedTeamDto>();
        foreach (var t in topTeams)
        {
            const string teamMembersSql2 = @"
                SELECT DISTINCT rp.golfer_id AS GolferId, g.full_name AS FullName
                FROM round_participants rp
                JOIN golfers g ON rp.golfer_id = g.id
                WHERE rp.round_team_id = @TeamId AND rp.round_id = ANY(@RoundIds)
                ORDER BY g.full_name;
            ";
            var members = (await connection.QueryAsync(teamMembersSql2, new { TeamId = t.TeamId, RoundIds = roundIds.ToArray() }))
                .Select(r => new TeamMemberDto((Guid)r.golferid, (string)r.fullname)).ToList();

            mostPlayedTeams.Add(new MostPlayedTeamDto(members, t.RoundsCount));
        }
    }

    decimal? roundedGroupAvg = groupAvg.HasValue ? Math.Round(groupAvg.Value, 2) : null;
    decimal? roundedGroupMedian = groupMedian.HasValue ? Math.Round(groupMedian.Value, 2) : null;

    var summary = new GroupYearSummaryDto(groupId, roundIds.Count, roundedGroupAvg, roundedGroupMedian, totalPot, maxPot);
        return new GroupYearEndReportDto(groupId, year, playersList, bestPlayer, bestPlayerByMedian, bestPlayerByCth, bestTeamByAvg, bestTeamBestRound, mostPlayedTeams, summary);
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
