using Microsoft.AspNetCore.Authorization;
using TeeTimeTally.Shared.Auth;

namespace TeeTimeTally.API.Identity;

public class ScopeAuthorizationHandler
	: AuthorizationHandler<ScopeAuthorizationRequirement>
{
	protected override Task HandleRequirementAsync(
		AuthorizationHandlerContext context,
		ScopeAuthorizationRequirement requirement)
	{
		if (context.User.HasClaim(c => c.Type == Auth0ClaimTypes.Scope && c.Issuer == requirement.Issuer && c.Value == requirement.Scope))
			context.Succeed(requirement);
			

		return Task.CompletedTask;
	}
}
