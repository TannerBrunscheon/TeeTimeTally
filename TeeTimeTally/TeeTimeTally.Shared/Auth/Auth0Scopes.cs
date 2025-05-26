// scopes (or permissions) that the application might request from Auth0.
namespace TeeTimeTally.Shared.Auth;

public static class Auth0Scopes
{
	// Standard OpenID Connect Scopes
	public const string Openid = "openid";
	public const string Profile = "profile";
	public const string Email = "email";
	public const string Picture = "picture";
	public const string Name = "name";

	// --- Group Management Permissions ---
	public const string CreateGroups = "create:groups";
	public const string ReadGroups = "read:groups";
	public const string ManageGroupMembers = "manage:group_members";
	public const string ManageGroupScorers = "manage:group_scorers";
	public const string ManageGroupFinances = "manage:group_finances";
	public const string ManageGroupSettings = "manage:group_settings";

	// --- Round Management Permissions ---
	public const string CreateRounds = "create:rounds";
	public const string ManageRoundSetup = "manage:round_setup";
	public const string ManageRoundScores = "manage:round_scores";
	public const string ManageRoundCth = "manage:round_cth";
	public const string ManageRoundWinner = "manage:round_winner";
	public const string FinalizeRounds = "finalize:rounds";
	public const string ReadRoundSummary = "read:round_summary";
	public const string ReadGroupRounds = "read:group_rounds";

	// --- User Management Permissions ---
	public const string CreateGolfers = "create:golfers";
	public const string ReadGolfers = "read:golfers";
	public const string ManageScorers = "manage:scorers";
	public const string ReadSelf = "read:self";

	// --- Course Management Permissions ---
	public const string CreateCourses = "create:courses";
	public const string ManageCourses = "manage:courses";
	public const string ReadCourses = "read:courses";

	// --- System Permissions (Admin) ---
	public const string ManageAllGroups = "manage:all_groups";

	/// <summary>
	/// Provides an array containing all defined scopes/permissions.
	/// </summary>
	public static string[] All =>
	[
		// Standard
		Openid,
	Profile,
	Email,
	Picture,
	Name,

        // Group Management
        CreateGroups,
	ReadGroups,
	ManageGroupMembers,
	ManageGroupScorers,
	ManageGroupFinances,
	ManageGroupSettings,

        // Round Management
        CreateRounds,
	ManageRoundSetup,
	ManageRoundScores,
	ManageRoundCth,
	ManageRoundWinner,
	FinalizeRounds,
	ReadRoundSummary,
	ReadGroupRounds,

        // User Management
        CreateGolfers,
	ReadGolfers,
	ManageScorers,
	ReadSelf,

        // Course Management
        CreateCourses,
	ManageCourses,
	ReadCourses,

        // System
        ManageAllGroups
	];
}