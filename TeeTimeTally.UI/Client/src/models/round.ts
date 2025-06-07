// --- DTO for listing open rounds ---
export interface OpenRound {
  roundId: string
  roundDate: string // Store as string, format in component
  status: string
  groupId: string
  groupName: string
  courseId: string
  courseName: string
  numPlayers: number | null
}

// --- DTOs for starting a new round ---

export interface TeamDefinitionRequest {
  teamNameOrNumber: string
  golferIdsInTeam: string[]
}

export interface StartRoundRequest {
  courseId: string
  allParticipatingGolferIds: string[]
  teams: TeamDefinitionRequest[]
  roundDate?: string | null
}

export interface GolferBasicResponse {
  golferId: string;
  fullName: string;
}

export interface RoundTeamResponse {
  teamId: string;
  teamNameOrNumber: string;
  members: GolferBasicResponse[];
}


export interface StartRoundResponse {
  roundId: string
  groupId: string
  courseId: string
  financialConfigurationIdUsed: string
  roundDate: string
  status: string
  numPlayers: number
  totalPot: number
  calculatedSkinValuePerHole: number
  calculatedCthPayout: number
  teamsInRound: RoundTeamResponse[]
  createdAt: string
}

// --- DTOs for Getting a Round by ID & Finalization ---

export interface GolferParticipantResponse {
  golferId: string;
  fullName: string;
}

export interface TeamInRoundResponse {
  teamId: string;
  teamNameOrNumber: string;
  members: GolferParticipantResponse[];
  isOverallWinner: boolean;
}

export interface ScoreDetailResponse {
  teamId: string;
  holeNumber: number;
  score: number;
  isSkinWinner: boolean;
  skinValueWon: number;
}

export interface RoundFinancialsResponse {
  financialConfigurationIdUsed: string;
  originalBuyInAmount: number;
  originalSkinValueFormula: string;
  originalCthPayoutFormula: string;
  perRoundCalculatedSkinValuePerHole: number;
  perRoundCalculatedCthPayout: number;
}

export interface PlayerPayoutBreakdown {
  skinsWinnings: number;
  cthWinnings: number;
  overallWinnings: number;
}

export interface PlayerPayoutSummaryResponse {
  golferId: string;
  fullName: string;
  teamId: string;
  teamName: string;
  totalWinnings: number;
  breakdown: PlayerPayoutBreakdown;
}


export interface GetRoundByIdResponse {
  roundId: string;
  roundDate: string;
  status: string;
  groupId: string;
  groupName: string;
  courseId: string;
  courseName: string;
  courseCthHoleNumber: number;
  numPlayers: number;
  totalPot: number;
  financials: RoundFinancialsResponse;
  teams: TeamInRoundResponse[];
  scores: ScoreDetailResponse[];
  cthWinnerGolferId?: string;
  cthWinnerGolferName?: string;
  finalSkinRolloverAmount?: number;
  finalTotalSkinsPayout?: number;
  finalOverallWinnerPayoutAmount?: number;
  finalizedAt?: string;
  createdAt: string;
  updatedAt: string;
  playerPayouts?: PlayerPayoutSummaryResponse[];
  payoutVerificationMessage?: string;
}


// --- DTOs for Submitting Scores ---

export interface TeamHoleScoreRequest {
  teamId: string;
  holeNumber: number;
  score: number;
}

export interface SubmitScoresRequest {
  scoresToSubmit: TeamHoleScoreRequest[];
}

export interface SubmitScoresResponse {
  message: string;
  roundId: string;
  scoresProcessedSuccessfully: number;
  roundStatusAfterSubmit?: string;
}

// --- DTOs for Completing a Round ---

export interface CompleteRoundRequest {
  roundId: string;
  cthWinnerGolferId: string;
  overallWinnerTeamIdOverride?: string;
}

export interface SkinPayoutDetailResponse {
  teamId: string;
  teamName: string;
  holeNumber: number;
  amountWon: number;
  isCarryOverWin: boolean;
}

export interface CthPayoutDetailResponse {
  winningGolferId: string;
  winningGolferName: string;
  winningTeamId: string;
  winningTeamName: string;
  amount: number;
}

export interface OverallWinnerPayoutResponse {
  teamId: string;
  teamName: string;
  amount: number;
}

export interface CompleteRoundResponse {
  roundId: string;
  finalStatus: string;
  totalPot: number;
  skinPayouts: SkinPayoutDetailResponse[];
  totalSkinsPaidOut: number;
  finalSkinRolloverAmount: number;
  cthPayout?: CthPayoutDetailResponse;
  overallWinnerPayouts: OverallWinnerPayoutResponse[];
  totalOverallWinnerPayout: number;
  playerPayouts: PlayerPayoutSummaryResponse[];
  payoutVerificationMessage: string;
}

export interface RoundHistoryItem {
  roundId: string;
  roundDate: string;
  courseName: string;
  numPlayers: number;
  totalPot: number;
  status: string;
}

export interface GetGroupRoundHistoryResponse {
  rounds: RoundHistoryItem[];
}
