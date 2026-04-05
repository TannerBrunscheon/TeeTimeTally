namespace TeeTimeTally.Shared.Reports;

public record PlayerYearStatsDto(
    Guid GolferId,
    string FullName,
    int TimesPlayed,
    decimal NetWinnings,
    decimal? AvgVsParPerRound,
    decimal? MedianVsParPerRound
);

public record GroupYearSummaryDto(
    Guid GroupId,
    int RoundsCount,
    decimal? AvgGroupVsPar,
    decimal? MedianGroupVsPar,
    decimal TotalPotSum,
    decimal MaxPot
);

public record TeamYearStatsDto(
    Guid TeamId,
    string TeamName,
    decimal? AvgScorePerRound,
    decimal? BestRoundScore
);

public record GroupYearEndReportDto(
    Guid GroupId,
    int Year,
    List<PlayerYearStatsDto> Players,
    PlayerYearStatsDto? BestPlayerByAvgVsPar,
    PlayerYearStatsDto? BestPlayerByMedian,
    TeamYearStatsDto? BestTeamByAvg,
    TeamYearStatsDto? BestTeamBestRound,
    GroupYearSummaryDto GroupSummary
);
