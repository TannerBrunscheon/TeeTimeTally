namespace TeeTimeTally.Shared.Reports;

public record PlayerYearStatsDto(
    Guid GolferId,
    string FullName,
    int TimesPlayed,
    decimal SkinsWinnings,
    decimal TotalWinnings,
    decimal? AvgVsParPerRound,
    decimal? MedianVsParPerRound,
    int ClosestToHoleCount
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
    PlayerYearStatsDto? BestPlayerByCth,
    TeamYearStatsDto? BestTeamByAvg,
    TeamYearStatsDto? BestTeamBestRound,
    List<TeamYearStatsDto>? BestTeamsByAvg,
    List<TeamYearStatsDto>? BestTeamsByBestRound,
    List<MostPlayedTeamDto>? MostPlayedTeams,
    GroupYearSummaryDto GroupSummary
);
