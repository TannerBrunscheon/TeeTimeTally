﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TeeTimeTally.Shared.Auth;

namespace TeeTimeTally.API.Identity;

public class ApplicationAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options, IConfiguration configuration)
	: DefaultAuthorizationPolicyProvider(options)
{
	public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
	{
		var policy = await base.GetPolicyAsync(policyName);

		if (policy is null)
		{
			if (Auth0Scopes.All.Contains(policyName))
			{
				policy = new AuthorizationPolicyBuilder()
					.AddRequirements(
						new ScopeAuthorizationRequirement(policyName, configuration["Auth0:Domain"]!))
					.RequireAuthenticatedUser()
					.Build();
			}
		}

		return policy;
	}
}