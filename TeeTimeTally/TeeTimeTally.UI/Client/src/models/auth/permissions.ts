export class Permissions {
  // --- Group Management Permissions ---
  public static readonly CreateGroups: string = 'create:groups';
  public static readonly ReadGroups: string = 'read:groups';
  public static readonly ManageGroupMembers: string = 'manage:group_members';
  public static readonly ManageGroupScorers: string = 'manage:group_scorers';
  public static readonly ManageGroupFinances: string = 'manage:group_finances';
  public static readonly ManageGroupSettings: string = 'manage:group_settings';

  // --- Round Management Permissions ---
  public static readonly CreateRounds: string = 'create:rounds';
  public static readonly ManageRoundSetup: string = 'manage:round_setup';
  public static readonly ManageRoundScores: string = 'manage:round_scores';
  public static readonly ManageRoundCth: string = 'manage:round_cth';
  public static readonly ManageRoundWinner: string = 'manage:round_winner';
  public static readonly FinalizeRounds: string = 'finalize:rounds';
  public static readonly ReadRoundSummary: string = 'read:round_summary';
  public static readonly ReadGroupRounds: string = 'read:group_rounds';

  // --- User Management Permissions ---
  public static readonly CreateGolfers: string = 'create:golfers';
  public static readonly ReadGolfers: string = 'read:golfers';
  public static readonly ManageScorers: string = 'manage:scorers';
  public static readonly ReadSelf: string = 'read:self';

  // --- Course Management Permissions ---
  public static readonly CreateCourses: string = 'create:courses';
  public static readonly ManageCourses: string = 'manage:courses';
  public static readonly ReadCourses: string = 'read:courses';

  // --- System Permissions (Admin) ---
  public static readonly ManageAllGroups: string = 'manage:all_groups';

  /**
   * Returns an array containing all defined application permissions.
   */
  public static all(): string[] {
    return [
      // Group Management
      this.CreateGroups,
      this.ReadGroups,
      this.ManageGroupMembers,
      this.ManageGroupScorers,
      this.ManageGroupFinances,
      this.ManageGroupSettings,

      // Round Management
      this.CreateRounds,
      this.ManageRoundSetup,
      this.ManageRoundScores,
      this.ManageRoundCth,
      this.ManageRoundWinner,
      this.FinalizeRounds,
      this.ReadRoundSummary,
      this.ReadGroupRounds,

      // User Management
      this.CreateGolfers,
      this.ReadGolfers,
      this.ManageScorers,
      this.ReadSelf,

      // Course Management
      this.CreateCourses,
      this.ManageCourses,
      this.ReadCourses,

      // System
      this.ManageAllGroups,
    ];
  }
}
