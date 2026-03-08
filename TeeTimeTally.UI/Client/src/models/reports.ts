export interface PlayerYearStats {
  golferId: string;
  fullName: string;
  timesPlayed: number;
  netWinnings: number;
  avgVsParPerRound: number;
  medianVsParPerRound: number;
}

export interface GroupYearSummary {
  groupId: string;
  roundsCount: number;
  avgGroupVsPar: number;
  medianGroupVsPar: number;
  totalPotSum: number;
  maxPot: number;
}

export interface GroupYearEndReportResponse {
  groupId: string;
  year: number;
  players: PlayerYearStats[];
  bestPlayerByAvgVsPar?: PlayerYearStats;
  groupSummary: GroupYearSummary;
}