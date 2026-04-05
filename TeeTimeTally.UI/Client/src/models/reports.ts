export interface PlayerYearStats {
  golferId: string;
  fullName: string;
  timesPlayed: number;
  netWinnings: number;
  closestToHoleCount?: number;
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
  members?: TeamMember[];
  roundsPlayedTogether?: number;
}

export interface TeamMember {
  golferId: string;
  fullName: string;
}

export interface MostPlayedTeam {
  members: TeamMember[];
  count: number;
}

export interface GroupYearEndReportResponse {
  groupId: string;
  year: number;
  players: PlayerYearStats[];
  bestPlayerByAvgVsPar?: PlayerYearStats;
  bestPlayerByMedian?: PlayerYearStats;
  bestPlayerByCth?: PlayerYearStats;
  bestTeamByAvg?: TeamYearStats;
  bestTeamBestRound?: TeamYearStats;
  mostPlayedTeams?: MostPlayedTeam[];
  groupSummary: GroupYearSummary;
}