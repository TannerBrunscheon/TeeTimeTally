using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TeeTimeTally.Shared.Reports;

namespace TeeTimeTally.API.Services;

public class ReportService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<ReportService> _logger;

    public ReportService(NpgsqlDataSource dataSource, ILogger<ReportService> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    // Safely parse numeric-like DB output (which may be returned as dynamic) into decimal?
    private static decimal? ParseDecimal(object? raw)
    {
        if (raw == null) return null;
        // Convert using invariant culture to avoid locale issues
        var s = Convert.ToString(raw, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(s)) return null;
        // Allow exponent notation as well
        if (decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var v)) return v;
        return null;
    }

    // For QuerySingleOrDefaultAsync that returns a row with a single column, extract that first column value
    private static object? FirstColumn(object? row)
    {
        if (row == null) return null;
        if (row is IDictionary<string, object> dict) return dict.Values.FirstOrDefault();
        return row;
    }

    // Create a stable Guid from a string (MD5 hash -> Guid)
    private static Guid GuidFromString(string s)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(s);
        var hash = md5.ComputeHash(bytes);
        return new Guid(hash);
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
            return new GroupYearEndReportDto(groupId, year, new List<PlayerYearStatsDto>(), null, null, null, null, null, null, null, null, emptySummary);
        }
        const string playersSql = @"
            SELECT p.golfer_id AS GolferId, g.full_name AS FullName, COUNT(DISTINCT p.round_id) AS TimesPlayed
            FROM round_participants p
            JOIN golfers g ON p.golfer_id = g.id
            WHERE p.round_id = ANY(@RoundIds)
            GROUP BY p.golfer_id, g.full_name;
        ";
        var players = (await connection.QueryAsync(playersSql, new { RoundIds = roundIds.ToArray() }))
            .Select(r => new PlayerYearStatsDto((Guid)r.golferid, (string)r.fullname, (int)r.timesplayed, 0m, 0m, null, null, 0))
            .ToDictionary(p => p.GolferId);

        // Use round_payout_summary which contains per-player breakdown JSON to compute
        // both TotalWinnings and SkinsWinnings. This avoids double-counting skins when
        // a team has multiple members (round_scores.skin_value_won stores the team amount).
        const string payoutsSql = @"
            -- cast numeric to text to avoid Npgsql numeric->Decimal conversion during mapping
            SELECT golfer_id AS GolferId,
                   SUM(total_winnings)::text AS TotalWinnings,
                   SUM((breakdown::jsonb ->> 'SkinsWinnings')::numeric)::text AS SkinsWinnings
            FROM round_payout_summary
            WHERE round_id = ANY(@RoundIds)
            GROUP BY golfer_id;
        ";
        var payoutsRaw = (await connection.QueryAsync(payoutsSql, new { RoundIds = roundIds.ToArray() })).ToList();
        var payouts = new Dictionary<Guid, (decimal Total, decimal Skins)>();
        foreach (var r in payoutsRaw)
        {
            try
            {
                var gid = (Guid)r.golferid;
                // Parse numeric results defensively to avoid Npgsql decimal overflow
                var totalRaw = r.totalwinnings;
                var skinsRaw = r.skinswinnings;
                decimal total = 0m;
                decimal skins = 0m;
                var parsedTotal = ParseDecimal((object)totalRaw);
                if (parsedTotal.HasValue) total = parsedTotal.Value;
                else if (totalRaw != null)
                {
                    _logger.LogWarning("ReportService: Failed to parse total winnings for golfer {GolferId}. Raw: {Raw}", (object)gid, (object)Convert.ToString(totalRaw, CultureInfo.InvariantCulture));
                }
                var parsedSkins = ParseDecimal((object)skinsRaw);
                if (parsedSkins.HasValue) skins = parsedSkins.Value;
                else if (skinsRaw != null)
                {
                    _logger.LogWarning("ReportService: Failed to parse skins winnings for golfer {GolferId}. Raw: {Raw}", (object)gid, (object)Convert.ToString(skinsRaw, CultureInfo.InvariantCulture));
                }
                payouts[gid] = (Total: total, Skins: skins);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ReportService: Unexpected error parsing payout row: {Row}", (object)r);
            }
        }

        foreach (var kv in players.ToList())
        {
            if (payouts.TryGetValue(kv.Key, out var tot))
            {
                players[kv.Key] = kv.Value with { TotalWinnings = Math.Round(tot.Total, 2), SkinsWinnings = Math.Round(tot.Skins, 2) };
            }
        }

        // We don't currently collect hole-by-hole par data, so compute averages/medians
        // over total round scores (lower is better). These populate the same DTO
        // fields previously named 'vs par' so UI continues to work without DB schema changes.
        const string vsParSql = @"
            -- return as text for defensive parsing
            SELECT t.golfer_id AS GolferId, AVG(player_round_score_total)::text AS AvgVsParPerRound
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
        var vsParsRaw = (await connection.QueryAsync(vsParSql, new { RoundIds = roundIds.ToArray() })).ToList();
        var vsPars = new Dictionary<Guid, decimal>();
        foreach (var r in vsParsRaw)
        {
            try
            {
                var gid = (Guid)r.golferid;
                decimal val = 0m;
                if (r.avgvsparperround != null)
                {
                    var parsed = ParseDecimal((object)r.avgvsparperround);
                    if (parsed.HasValue) val = parsed.Value;
                    else _logger.LogWarning("ReportService: Failed to parse AvgVsParPerRound for golfer {GolferId}. Raw: {Raw}", (object)gid, (object)Convert.ToString(r.avgvsparperround, CultureInfo.InvariantCulture));
                }
                vsPars[gid] = val;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ReportService: Failed to parse AvgVsParPerRound row: {Row}", (object)r);
            }
        }

        foreach (var kv in players.ToList())
        {
            if (vsPars.TryGetValue(kv.Key, out var avg))
            {
                players[kv.Key] = kv.Value with { AvgVsParPerRound = Math.Round(avg, 2) };
            }
        }

     const string vsParMedianSql = @"
         -- return as text for defensive parsing
         SELECT golfer_id AS GolferId,
             percentile_cont(0.5) WITHIN GROUP (ORDER BY player_round_score_total)::text AS MedianVsParPerRound
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
        var vsParsMedianRaw = (await connection.QueryAsync(vsParMedianSql, new { RoundIds = roundIds.ToArray() })).ToList();
        var vsParsMedian = new Dictionary<Guid, decimal?>();
        foreach (var r in vsParsMedianRaw)
        {
            try
            {
                var gid = (Guid)r.golferid;
                decimal? val = null;
                if (r.medianvsparperround != null)
                {
                    var parsed = ParseDecimal((object)r.medianvsparperround);
                    if (parsed.HasValue) val = parsed.Value;
                    else _logger.LogWarning("ReportService: Failed to parse MedianVsParPerRound for golfer {GolferId}. Raw: {Raw}", (object)gid, (object)Convert.ToString(r.medianvsparperround, CultureInfo.InvariantCulture));
                }
                vsParsMedian[gid] = val;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ReportService: Failed to parse MedianVsParPerRound row: {Row}", (object)r);
            }
        }

        foreach (var kv in players.ToList())
        {
            if (vsParsMedian.TryGetValue(kv.Key, out var med) && med.HasValue)
            {
                players[kv.Key] = kv.Value with { MedianVsParPerRound = Math.Round(med.Value, 2) };
            }
        }

        const string groupAvgSql = @"
            -- compute per-player average across all team-rounds (team total divided by team size)
            WITH per_round AS (
                SELECT rp.round_id,
                       SUM(rs.score) AS team_round_score_total,
                       COUNT(DISTINCT rp.golfer_id) AS member_count
                FROM round_participants rp
                JOIN round_scores rs ON rp.round_id = rs.round_id AND rp.round_team_id = rs.round_team_id
                WHERE rp.round_id = ANY(@RoundIds)
                GROUP BY rp.round_id, rp.round_team_id
            )
            SELECT AVG((team_round_score_total::numeric / NULLIF(member_count,0)))::text AS AvgGroupVsPar
            FROM per_round;
        ";
    var groupAvgRaw = await connection.QuerySingleOrDefaultAsync(groupAvgSql, new { RoundIds = roundIds.ToArray() });
    decimal? groupAvg = null;
    if (groupAvgRaw != null)
    {
        try
        {
            var first = FirstColumn(groupAvgRaw);
            var parsed = ParseDecimal((object)first);
            if (parsed.HasValue) groupAvg = parsed.Value;
            else _logger.LogWarning("ReportService: Failed to parse groupAvg: {Raw}", (object)Convert.ToString(first, CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "ReportService: Failed to parse groupAvg: {Raw}", (object)groupAvgRaw); }
    }

        const string groupMedianSql = @"
            -- compute per-player median across all team-rounds
            WITH per_round AS (
                SELECT rp.round_id,
                       SUM(rs.score) AS team_round_score_total,
                       COUNT(DISTINCT rp.golfer_id) AS member_count
                FROM round_participants rp
                JOIN round_scores rs ON rp.round_id = rs.round_id AND rp.round_team_id = rs.round_team_id
                WHERE rp.round_id = ANY(@RoundIds)
                GROUP BY rp.round_id, rp.round_team_id
            )
            SELECT percentile_cont(0.5) WITHIN GROUP (ORDER BY (team_round_score_total::numeric / NULLIF(member_count,0)))::text AS MedianGroupVsPar
            FROM per_round;
        ";
    var groupMedianRaw = await connection.QuerySingleOrDefaultAsync(groupMedianSql, new { RoundIds = roundIds.ToArray() });
    decimal? groupMedian = null;
    if (groupMedianRaw != null)
    {
        try
        {
            var first = FirstColumn(groupMedianRaw);
            var parsed = ParseDecimal((object)first);
            if (parsed.HasValue) groupMedian = parsed.Value;
            else _logger.LogWarning("ReportService: Failed to parse groupMedian: {Raw}", (object)Convert.ToString(first, CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "ReportService: Failed to parse groupMedian: {Raw}", (object)groupMedianRaw); }
    }

        const string potSql = @"
            -- return pot values as text for defensive parsing
            SELECT COALESCE(SUM(total_pot),0)::text AS TotalPotSum, COALESCE(MAX(total_pot),0)::text AS MaxPot
            FROM rounds
            WHERE id = ANY(@RoundIds);
        ";
        var pot = await connection.QuerySingleAsync(potSql, new { RoundIds = roundIds.ToArray() });
        decimal totalPot = 0m;
        decimal maxPot = 0m;
        try
        {
            var parsedTp = ParseDecimal((object)pot.totalpotsum);
            if (parsedTp.HasValue) totalPot = Math.Round(parsedTp.Value, 2);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportService: Failed to parse totalPot from pot query: {Pot}", (object)pot);
        }
        try
        {
            var parsedMp = ParseDecimal((object)pot.maxpot);
            if (parsedMp.HasValue) maxPot = Math.Round(parsedMp.Value, 2);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportService: Failed to parse maxPot from pot query: {Pot}", (object)pot);
        }

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

    // Team-level stats: compute per-composition (by golfer ids) averages and best single-round
    // We group by the canonical composition key (sorted golfer ids) so the same pairing across rounds is treated as one team
    const string teamsScoresSql = @"
        WITH per_round AS (
            SELECT rp.round_id,
                   string_agg(rp.golfer_id::text, ',' ORDER BY rp.golfer_id) AS composition_key,
                   SUM(rs.score) AS team_round_score_total
            FROM round_participants rp
            JOIN round_scores rs ON rp.round_id = rs.round_id AND rp.round_team_id = rs.round_team_id
            WHERE rp.round_id = ANY(@RoundIds)
            GROUP BY rp.round_id, rp.round_team_id
        )
        SELECT composition_key,
               AVG(team_round_score_total)::text AS AvgScorePerRound,
               MIN(team_round_score_total)::text AS BestRoundScore,
               COUNT(*)::int AS RoundsCount
        FROM per_round
        GROUP BY composition_key
        ORDER BY RoundsCount DESC, AvgScorePerRound ASC;
    ";
    var teamsScoresRaw = (await connection.QueryAsync(teamsScoresSql, new { RoundIds = roundIds.ToArray() })).ToList();
    var teamsScores = new List<(string CompositionKey, decimal? AvgScore, decimal? BestRound, int RoundsCount)>();
    foreach (var r in teamsScoresRaw)
    {
        try
        {
            var key = (string)r.composition_key;
            decimal? avg = null;
            decimal? best = null;
            if (r.avgscoreperround != null)
            {
                var parsed = ParseDecimal((object)r.avgscoreperround);
                if (parsed.HasValue) avg = Math.Round(parsed.Value, 2);
                else _logger.LogWarning("ReportService: Failed to parse AvgScorePerRound for composition {Comp}. Raw: {Raw}", (object)key, (object)Convert.ToString(r.avgscoreperround, CultureInfo.InvariantCulture));
            }
            if (r.bestroundscore != null)
            {
                var parsed = ParseDecimal((object)r.bestroundscore);
                if (parsed.HasValue) best = Math.Round(parsed.Value, 2);
                else _logger.LogWarning("ReportService: Failed to parse BestRoundScore for composition {Comp}. Raw: {Raw}", (object)key, (object)Convert.ToString(r.bestroundscore, CultureInfo.InvariantCulture));
            }
            var roundsCount = (int)r.roundscount;
            teamsScores.Add((CompositionKey: key, AvgScore: avg, BestRound: best, RoundsCount: roundsCount));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportService: Failed to parse teamsScores row: {Row}", (object)r);
        }
    }

    List<TeamYearStatsDto>? bestTeamsByAvg = null;
    List<TeamYearStatsDto>? bestTeamsByBestRound = null;
    TeamYearStatsDto? bestTeamByAvg = null;
    TeamYearStatsDto? bestTeamBestRound = null;

    if (teamsScores.Any())
    {
        var avgCandidates = teamsScores.Where(t => t.AvgScore.HasValue).ToList();
        if (avgCandidates.Any())
        {
            var minAvg = avgCandidates.Min(t => t.AvgScore!.Value);
            var winners = avgCandidates.Where(t => t.AvgScore == minAvg).ToList();
            bestTeamsByAvg = new List<TeamYearStatsDto>();
            foreach (var w in winners)
            {
                // members by composition key
                const string teamMembersSql = @"
                    SELECT id AS GolferId, full_name AS FullName
                    FROM golfers
                    WHERE id = ANY(string_to_array(@Comp, ',')::uuid[])
                    ORDER BY full_name;
                ";
                var members = (await connection.QueryAsync(teamMembersSql, new { Comp = w.CompositionKey }))
                    .Select(r => new TeamMemberDto((Guid)r.golferid, (string)r.fullname)).ToList();

                var dto = new TeamYearStatsDto(GuidFromString(w.CompositionKey), string.Join(", ", members.Select(m => m.FullName)), w.AvgScore, w.BestRound, members, w.RoundsCount);
                bestTeamsByAvg.Add(dto);
            }
            if (bestTeamsByAvg.Any()) bestTeamByAvg = bestTeamsByAvg.First();
        }

        var bestRoundCandidates = teamsScores.Where(t => t.BestRound.HasValue).ToList();
        if (bestRoundCandidates.Any())
        {
            var minBestRound = bestRoundCandidates.Min(t => t.BestRound!.Value);
            var winners = bestRoundCandidates.Where(t => t.BestRound == minBestRound).ToList();
            bestTeamsByBestRound = new List<TeamYearStatsDto>();
            foreach (var w in winners)
            {
                const string teamMembersSql = @"
                    SELECT id AS GolferId, full_name AS FullName
                    FROM golfers
                    WHERE id = ANY(string_to_array(@Comp, ',')::uuid[])
                    ORDER BY full_name;
                ";
                var members = (await connection.QueryAsync(teamMembersSql, new { Comp = w.CompositionKey }))
                    .Select(r => new TeamMemberDto((Guid)r.golferid, (string)r.fullname)).ToList();

                var dto = new TeamYearStatsDto(GuidFromString(w.CompositionKey), string.Join(", ", members.Select(m => m.FullName)), w.AvgScore, w.BestRound, members, w.RoundsCount);
                bestTeamsByBestRound.Add(dto);
            }
            if (bestTeamsByBestRound.Any()) bestTeamBestRound = bestTeamsByBestRound.First();
        }
    }

    // Compute most-played-together teams (by rounds count) using composition grouping we just produced.
    List<MostPlayedTeamDto>? mostPlayedTeams = null;
    if (teamsScores.Any())
    {
        var topCount = teamsScores.Max(t => t.RoundsCount);
        var topComps = teamsScores.Where(t => t.RoundsCount == topCount).ToList();
        mostPlayedTeams = new List<MostPlayedTeamDto>();
        foreach (var comp in topComps)
        {
            const string teamMembersSql2 = @"
                SELECT id AS GolferId, full_name AS FullName
                FROM golfers
                WHERE id = ANY(string_to_array(@Comp, ',')::uuid[])
                ORDER BY full_name;
            ";
            var members = (await connection.QueryAsync(teamMembersSql2, new { Comp = comp.CompositionKey }))
                .Select(r => new TeamMemberDto((Guid)r.golferid, (string)r.fullname)).ToList();
            mostPlayedTeams.Add(new MostPlayedTeamDto(members, comp.RoundsCount));
        }
    }

    decimal? roundedGroupAvg = groupAvg.HasValue ? Math.Round(groupAvg.Value, 2) : null;
    decimal? roundedGroupMedian = groupMedian.HasValue ? Math.Round(groupMedian.Value, 2) : null;

    var summary = new GroupYearSummaryDto(groupId, roundIds.Count, roundedGroupAvg, roundedGroupMedian, totalPot, maxPot);

    // Re-evaluate the players list after we've applied CTH counts and any other updates
    var playersList = players.Values.OrderByDescending(p => p.TimesPlayed).ToList();

    return new GroupYearEndReportDto(groupId, year, playersList, bestPlayer, bestPlayerByMedian, bestPlayerByCth, bestTeamByAvg, bestTeamBestRound, bestTeamsByAvg, bestTeamsByBestRound, mostPlayedTeams, summary);
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
