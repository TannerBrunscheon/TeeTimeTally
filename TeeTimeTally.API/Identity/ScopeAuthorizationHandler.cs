// TeeTimeTally.API/Identity/ScopeAuthorizationHandler.cs
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims; // Needed for ClaimTypes

namespace TeeTimeTally.API.Identity;

public class ScopeAuthorizationHandler : AuthorizationHandler<ScopeAuthorizationRequirement>
{
	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeAuthorizationRequirement requirement)
	{
		ILogger<ScopeAuthorizationHandler>? logger = null;
		if (context.Resource is HttpContext httpContext)
		{
			logger = httpContext.RequestServices.GetRequiredService<ILogger<ScopeAuthorizationHandler>>();
		}
		else if (context.Resource is Endpoint endpoint)
		{
			logger = endpoint.Metadata.GetMetadata<ILogger<ScopeAuthorizationHandler>>();
		}

		logger?.LogInformation("AuthZ: Handling requirement '{RequirementScope}' for user '{UserName}'.", requirement.Scope, context.User.Identity?.Name ?? "Unknown");

		logger?.LogInformation("AuthZ: Claims for user '{UserName}':", context.User.Identity?.Name ?? "Unknown");
		foreach (var claim in context.User.Claims)
		{
			logger?.LogInformation("  Claim: Type='{ClaimType}', Value='{ClaimValue}'", claim.Type, claim.Value);
		}

		// --- THE CRITICAL CHANGE IS HERE ---
		// Auth0 typically sends permissions as multiple claims of type "permissions"
		// or as a single "scope" claim with space-separated values.
		// We need to gather all of them.

		var userPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Use HashSet for efficient lookup and case-insensitivity

		// 1. Check for individual "permissions" claims (most common for Auth0 custom scopes)
		foreach (var permissionClaim in context.User.FindAll("permissions"))
		{
			// If the claim value itself is a space-separated string (less common for 'permissions' but possible)
			if (permissionClaim.Value.Contains(' '))
			{
				foreach (var p in permissionClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				{
					userPermissions.Add(p);
				}
			}
			else // It's a single permission value
			{
				userPermissions.Add(permissionClaim.Value.Trim());
			}
		}

		// 2. Fallback: Check the "scope" claim if "permissions" claims weren't found or if it's the primary source
		// This is usually for OIDC scopes, but can contain custom API scopes too.
		if (userPermissions.Count == 0) // Only check 'scope' if 'permissions' claims didn't yield anything
		{
			var scopeClaim = context.User.FindFirst("scope");
			if (scopeClaim != null)
			{
				foreach (var p in scopeClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				{
					userPermissions.Add(p);
				}
			}
		}

		logger?.LogInformation("AuthZ: User has PARSED permissions: [{UserPermissions}]", string.Join(", ", userPermissions));

		if (userPermissions.Contains(requirement.Scope))
		{
			logger?.LogInformation("AuthZ: User has required permission '{RequiredScope}'. Succeeded.", requirement.Scope);
			context.Succeed(requirement);
		}
		else
		{
			logger?.LogWarning("AuthZ: User DOES NOT have required permission '{RequiredScope}'. Failing. User permissions were: [{UserPermissions}]", requirement.Scope, string.Join(", ", userPermissions));
			context.Fail();
		}

		return Task.CompletedTask;
	}
}