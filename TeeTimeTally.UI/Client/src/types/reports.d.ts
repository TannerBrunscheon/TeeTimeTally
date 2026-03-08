export interface PlayerYearStatsDto {
  golferId: string;
  fullName: string;
  timesPlayed: number;
  netWinnings: number;
  avgVsParPerRound: number;
  medianVsParPerRound: number;
}

export interface GroupYearSummaryDto {
  groupId: string;
  roundsCount: number;
  avgGroupVsPar: number;
  medianGroupVsPar: number;
  totalPotSum: number;
  maxPot: number;
}

export interface GroupYearEndReportDto {
  groupId: string;
  year: number;
  players: PlayerYearStatsDto[];
  bestPlayerByAvgVsPar?: PlayerYearStatsDto;
  groupSummary: GroupYearSummaryDto;
}
