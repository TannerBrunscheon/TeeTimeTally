export interface PlayerYearStatsDto {
  golferId: string;
  fullName: string;
  timesPlayed: number;
  netWinnings: number;
  avgVsParPerRound?: number | null;
  medianVsParPerRound?: number | null;
}

export interface GroupYearSummaryDto {
  groupId: string;
  roundsCount: number;
  avgGroupVsPar?: number | null;
  medianGroupVsPar?: number | null;
  totalPotSum: number;
  maxPot: number;
}

export interface TeamYearStatsDto {
  teamId: string;
  teamName: string;
  avgScorePerRound?: number | null;
  bestRoundScore?: number | null;
  members?: TeamMemberDto[];
  roundsPlayedTogether?: number;
}

export interface TeamMemberDto {
  golferId: string;
  fullName: string;
}

export interface MostPlayedTeamDto {
  members: TeamMemberDto[];
  count: number;
}

export interface GroupYearEndReportDto {
  groupId: string;
  year: number;
  players: PlayerYearStatsDto[];
  bestPlayerByAvgVsPar?: PlayerYearStatsDto;
  bestPlayerByMedian?: PlayerYearStatsDto;
  bestTeamByAvg?: TeamYearStatsDto;
  bestTeamBestRound?: TeamYearStatsDto;
  mostPlayedTeams?: MostPlayedTeamDto[];
  groupSummary: GroupYearSummaryDto;
}
