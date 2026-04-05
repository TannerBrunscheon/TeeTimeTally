export interface PlayerYearStats {
  golferId: string;
  fullName: string;
  timesPlayed: number;
  netWinnings: number;
  avgVsParPerRound?: number | null;
  medianVsParPerRound?: number | null;
}

export interface GroupYearSummary {
  groupId: string;
  roundsCount: number;
  avgGroupVsPar?: number | null;
  medianGroupVsPar?: number | null;
  totalPotSum: number;
  maxPot: number;
}

export interface TeamYearStats {
  teamId: string;
  teamName: string;
  avgScorePerRound?: number | null;
  bestRoundScore?: number | null;
}

export interface GroupYearEndReportResponse {
  groupId: string;
  year: number;
  players: PlayerYearStats[];
  bestPlayerByAvgVsPar?: PlayerYearStats;
  bestPlayerByMedian?: PlayerYearStats;
  bestTeamByAvg?: TeamYearStats;
  bestTeamBestRound?: TeamYearStats;
  groupSummary: GroupYearSummary;
}