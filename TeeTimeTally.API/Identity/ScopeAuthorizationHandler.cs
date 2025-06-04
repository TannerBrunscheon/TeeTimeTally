// TeeTimeTally.API/Identity/ScopeAuthorizationHandler.cs
using Microsoft.AspNetCore.Authorization;

namespace TeeTimeTally.API.Identity;

public class ScopeAuthorizationHandler : AuthorizationHandler<ScopeAuthorizationRequirement>
{
	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeAuthorizationRequirement requirement)
	{
		// Access logger via HttpContext if available, or inject if possible
		ILogger<ScopeAuthorizationHandler>? logger;
		if (context.Resource is HttpContext httpContext)
		{
			logger = httpContext.RequestServices.GetRequiredService<ILogger<ScopeAuthorizationHandler>>();
		}
		else if (context.Resource is Endpoint endpoint) // For Minimal APIs
		{
			logger = endpoint.Metadata.GetMetadata<ILogger<ScopeAuthorizationHandler>>();
		}
		else
		{
			return Task.CompletedTask;
		}
		// Fallback if logger cannot be resolved this way (should be resolved via DI usually)

		logger?.LogInformation("AuthZ: Handling requirement '{RequirementScope}' for user '{UserName}'.", requirement.Scope, context.User.Identity?.Name ?? "Unknown");

		// Log all claims present in the Principal
		logger?.LogInformation("AuthZ: Claims for user '{UserName}':", context.User.Identity?.Name ?? "Unknown");
		foreach (var claim in context.User.Claims)
		{
			logger?.LogInformation("  Claim: Type='{ClaimType}', Value='{ClaimValue}'", claim.Type, claim.Value);
		}

		// Auth0 typically puts permissions in a 'permissions' claim (custom claim type)
		// Or sometimes in the 'scope' claim (space-separated string)
		var permissionsClaim = context.User.FindFirst("permissions"); // Check for the 'permissions' claim
		if (permissionsClaim == null)
		{
			// Fallback to 'scope' claim if 'permissions' is not found
			permissionsClaim = context.User.FindFirst("scope");
			if (permissionsClaim == null)
			{
				logger?.LogWarning("AuthZ: Neither 'permissions' nor 'scope' claim found in JWT for user '{UserName}'.", context.User.Identity?.Name ?? "Unknown");
				context.Fail();
				return Task.CompletedTask;
			}
		}

		var userPermissions = permissionsClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		logger?.LogInformation("AuthZ: User has permissions: {UserPermissions}", string.Join(", ", userPermissions));

		if (userPermissions.Contains(requirement.Scope))
		{
			logger?.LogInformation("AuthZ: User has required permission '{RequiredScope}'. Succeeded.", requirement.Scope);
			context.Succeed(requirement);
		}
		else
		{
			logger?.LogWarning("AuthZ: User DOES NOT have required permission '{RequiredScope}'. Failing.", requirement.Scope);
			context.Fail();
		}

		return Task.CompletedTask;
	}
}