export interface OpenRound {
  roundId: string;
  roundDate: string; // Store as string, format in component
  status: string;
  groupId: string;
  groupName: string;
  courseId: string;
  courseName: string;
  numPlayers: number | null; // API DTO is int, but DB might allow null for num_players before setup
}
