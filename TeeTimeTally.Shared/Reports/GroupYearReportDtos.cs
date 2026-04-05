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
    decimal? BestRoundScore,
    List<TeamMemberDto> Members,
    int RoundsPlayedTogether
);

public record TeamMemberDto(
    Guid GolferId,
    string FullName
);

public record MostPlayedTeamDto(
    List<TeamMemberDto> Members,
    int Count
);

public record GroupYearEndReportDto(
    Guid GroupId,
    int Year,
    List<PlayerYearStatsDto> Players,
    PlayerYearStatsDto? BestPlayerByAvgVsPar,
    PlayerYearStatsDto? BestPlayerByMedian,
    TeamYearStatsDto? BestTeamByAvg,
    TeamYearStatsDto? BestTeamBestRound,
    List<MostPlayedTeamDto>? MostPlayedTeams,
    GroupYearSummaryDto GroupSummary
);
