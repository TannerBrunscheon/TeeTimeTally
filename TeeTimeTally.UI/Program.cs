using Auth0.AspNetCore.Authentication;
using Auth0.AspNetCore.Authentication.BackchannelLogout;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides; // Required for ForwardedHeadersOptions
using System.Security.Claims;
using TeeTimeTally.Shared.Auth;
using TeeTimeTally.UI.Identity;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure ForwardedHeadersOptions to trust headers from non-localhost proxies
// This is crucial for platforms like Railway/Cloudflare.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
	options.ForwardedHeaders =
		ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
	// By default, these headers are only trusted from localhost.
	// Clearing KnownProxies and KnownNetworks tells the middleware to trust these headers
	// from any immediate upstream proxy. This is generally safe for managed hosting platforms.
	options.KnownProxies.Clear();
	options.KnownNetworks.Clear();
});

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddReverseProxy()
	.LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
	.AddTransforms(context =>
	{
		context.AddRequestTransform(async request =>
		{
			var accessToken = await request.HttpContext.GetTokenAsync("access_token");

			if (!string.IsNullOrEmpty(accessToken))
			{
				request.ProxyRequest.Headers.Remove("Authorization");
				request.ProxyRequest.Headers.Add("Authorization", $"Bearer {accessToken}");
			}
		});
	});

builder.Services.AddAuth0WebAppAuthentication(options =>
{
	options.Domain = builder.Configuration["Auth0:Domain"]!;
	options.ClientId = builder.Configuration["Auth0:ClientId"]!;
	options.ClientSecret = builder.Configuration["Auth0:ClientSecret"]!;

	options.Scope = string.Join(" ", Auth0Scopes.All);
	options.AccessDeniedPath = "/Home/AccessDenied";

	// 2. Explicitly set the CallbackPath to match your observed redirect and Auth0 config
	// Your logs indicated the callback was happening at "/callback"
	options.CallbackPath = new PathString("/callback");

	options.OpenIdConnectEvents = new OpenIdConnectEvents
	{
		OnTokenValidated = async context =>
		{
			var permissions = context.TokenEndpointResponse?.Scope?.Split(' ');
			if (permissions != null)
			{
				var claims = permissions.Select(permission => new Claim(IdentityClaimTypes.Permissions, permission));
				var identity = context.Principal?.Identities.FirstOrDefault();
				identity?.AddClaims(claims);
			}
		}
	};
}).WithAccessToken(options =>
{
	options.Audience = builder.Configuration["Auth0:Audience"];
	options.UseRefreshTokens = true;
	options.Events = new Auth0WebAppWithAccessTokenEvents
	{
		OnMissingRefreshToken = async (context) =>
		{
			await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			var authenticationProperties = new LogoutAuthenticationPropertiesBuilder().WithRedirectUri("/").Build();
			await context.ChallengeAsync(Auth0Constants.AuthenticationScheme, authenticationProperties);
		},
	};
});

builder.Services.AddHttpClient("NoSslVerificationClient")
	.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
	{
		// Disabling SSL certificate validation - BE CAREFUL WITH THIS IN PRODUCTION
		// This is generally not recommended for production environments.
		// Ensure you understand the security implications.
		ServerCertificateCustomValidationCallback =
			(httpRequestMessage, cert, cetChain, policyErrors) => true
	});

builder.Services.AddAuthorizationBuilder()
	.AddPolicy("ApiPolicy", policy =>
		policy.RequireAuthenticatedUser());

builder.Services.AddSingleton<IAuthorizationPolicyProvider, ApplicationAuthorizationPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

builder.Services.AddHttpClient();

var app = builder.Build();

// 3. Ensure UseForwardedHeaders is called VERY EARLY in the pipeline.
// It needs to run before other middleware that depends on the request scheme (like HttpsRedirection and Authentication).
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
	// Potentially add app.UseDeveloperExceptionPage();
}
else
{
	// app.UseExceptionHandler("/Error"); // Example error handling
	// app.UseHsts(); // Consider HSTS for production
}

// This should now correctly redirect to HTTPS if the X-Forwarded-Proto is https
// and the app is accessed via HTTP internally by the proxy.
app.UseHttpsRedirection();

// Add UseStaticFiles if you serve static files from wwwroot
// app.UseStaticFiles();

// Add UseRouting if you use endpoint routing with more complex scenarios before auth
// app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseBackchannelLogout();


app.MapGet("/api/authentication/register", async (HttpContext httpContext, string returnUrl = "/") =>
{
	var authenticationProperties = new LoginAuthenticationPropertiesBuilder()
		.WithParameter("screen_hint", "signup")
		.WithRedirectUri(returnUrl)
		.Build();

	await httpContext.ChallengeAsync(Auth0Constants.AuthenticationScheme, authenticationProperties);
});

app.MapGet("/api/authentication/login", async (HttpContext httpContext, string returnUrl = "/") =>
{
	var authenticationProperties = new LoginAuthenticationPropertiesBuilder()
		.WithRedirectUri(returnUrl)
		.Build();

	await httpContext.ChallengeAsync(Auth0Constants.AuthenticationScheme, authenticationProperties);
});

app.MapGet("/api/authentication/logout", async (HttpContext httpContext) =>
{
	var authenticationProperties = new LogoutAuthenticationPropertiesBuilder()
			.WithRedirectUri("/")
			.Build();

	await httpContext.SignOutAsync(Auth0Constants.AuthenticationScheme, authenticationProperties);
	await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
});

app.MapGet("/api/authentication/user-info", async (HttpContext httpContext) =>
{
	// Added null checks for safety
	var name = httpContext.User.Identity?.Name;
	var email = httpContext.User.FindFirst(c => c.Type == ClaimTypes.Email)?.Value;
	var picture = httpContext.User.FindFirst(c => c.Type == Auth0Scopes.Picture)?.Value;
	var roles = httpContext.User.FindAll(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray();
	var permissions = httpContext.User.FindAll(c => c.Type == IdentityClaimTypes.Permissions).Select(c => c.Value).ToArray();

	return TypedResults.Ok(new UserInfoResponse(name!, email!, picture!, roles, permissions));
}).RequireAuthorization();

app.MapReverseProxy();

app.MapForwarder("/{**catch-all}", app.Configuration["VueAppEndpoint"]!);

await app.RunAsync();