using Auth0.AspNetCore.Authentication;
using Auth0.AspNetCore.Authentication.BackchannelLogout;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;
using TeeTimeTally.Shared.Auth;
using TeeTimeTally.UI.Identity; // Ensure ComprehensiveUserInfoResponse and ApiGolferProfileResponse are here
using Yarp.ReverseProxy.Transforms;
using System.Net.Http.Headers;
using System.Text.Json; // For JsonSerializer

var builder = WebApplication.CreateBuilder(args);

// 1. Configure ForwardedHeadersOptions
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
	options.ForwardedHeaders =
		ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
	options.KnownProxies.Clear();
	options.KnownNetworks.Clear();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient(); // For IHttpClientFactory

builder.Services.AddReverseProxy()
	.LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
	.AddTransforms(context =>
	{
		context.AddRequestTransform(async request =>
		{
			var logger = context.Services.GetRequiredService<ILogger<Program>>();
			var accessToken = await request.HttpContext.GetTokenAsync("access_token");
			if (!string.IsNullOrEmpty(accessToken))
			{
				logger.LogInformation("BFF: Access Token: {Token}", accessToken);
				logger.LogInformation("BFF: Found access token for forwarding. Length: {TokenLength}", accessToken.Length);
				request.ProxyRequest.Headers.Remove("Authorization");
				request.ProxyRequest.Headers.Add("Authorization", $"Bearer {accessToken}");
			}
			else
			{
				logger.LogWarning("BFF: Access token NOT found in HttpContext for forwarding to API.");
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

			var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
			var httpClientFactory = context.HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>();
			var accessToken = context.TokenEndpointResponse?.AccessToken;

			if (string.IsNullOrEmpty(accessToken))
			{
				logger.LogWarning("Access token not found after OnTokenValidated for user {User}. Cannot ensure golfer profile with API.", context.Principal?.Identity?.Name ?? "Unknown");
				return;
			}

			var apiBaseUrl = builder.Configuration["ReverseProxy:Clusters:backend:Destinations:destination1:Address"];
			if (string.IsNullOrEmpty(apiBaseUrl))
			{
				logger.LogError("API base URL (ReverseProxy:Clusters:backend:Destinations:destination1:Address) is not configured. Cannot call EnsureGolferProfileEndpoint.");
				return;
			}
			var ensureProfileApiUrl = $"{apiBaseUrl.TrimEnd('/')}/api/golfers/me/ensure-profile";

			try
			{
				var apiClient = httpClientFactory.CreateClient("BackendApiClient"); // Or default
				using var requestMessage = new HttpRequestMessage(HttpMethod.Post, ensureProfileApiUrl);
				requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

				logger.LogInformation("Calling EnsureGolferProfileEndpoint at {Url} for user {User}.", ensureProfileApiUrl, context.Principal?.Identity?.Name ?? "Unknown");
				var response = await apiClient.SendAsync(requestMessage, context.HttpContext.RequestAborted);

				if (!response.IsSuccessStatusCode)
				{
					var errorContent = await response.Content.ReadAsStringAsync();
					logger.LogError("Failed to ensure golfer profile via API for user {User}. Status: {StatusCode}. Content: {ErrorContent}",
						context.Principal?.Identity?.Name ?? "Unknown", response.StatusCode, errorContent);
				}
				else
				{
					var responseContent = await response.Content.ReadAsStringAsync();
					logger.LogInformation("Successfully called EnsureGolferProfileEndpoint for user {User}. API Status: {StatusCode}. Profile: {Profile}",
						context.Principal?.Identity?.Name ?? "Unknown", response.StatusCode, responseContent);
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Exception calling EnsureGolferProfileEndpoint for user {User}.", context.Principal?.Identity?.Name ?? "Unknown");
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

builder.Services.AddAuthorizationBuilder()
	.AddPolicy("ApiPolicy", policy =>
		policy.RequireAuthenticatedUser());

builder.Services.AddSingleton<IAuthorizationPolicyProvider, ApplicationAuthorizationPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

builder.Services.AddHttpClient("NoSslVerificationClient")
	.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
	{
		ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
	});

// Named HttpClient for calling the backend API
builder.Services.AddHttpClient("BackendApiClient", client =>
{
	var apiBaseUrl = builder.Configuration["ReverseProxy:Clusters:backend:Destinations:destination1:Address"];
	if (!string.IsNullOrEmpty(apiBaseUrl))
	{
		client.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
	}
	// Add other default headers if necessary, e.g., client.DefaultRequestHeaders.Add("Accept", "application/json");
});


var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
	// app.UseDeveloperExceptionPage();
}
else
{
	// app.UseExceptionHandler("/Error");
	// app.UseHsts();

}

app.UseHttpsRedirection();
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

app.MapGet("/api/authentication/user-info", async (
	HttpContext httpContext,
	IHttpClientFactory httpClientFactory,
	ILogger<Program> logger) =>
{
	var user = httpContext.User;
	if (user.Identity == null || !user.Identity.IsAuthenticated)
	{
		return Results.Unauthorized();
	}

	// 1. Get claims from the current authenticated user session
	var emailFromClaims = user.FindFirst(c => c.Type == ClaimTypes.Email)?.Value;
	var pictureFromClaims = user.FindFirst(c => c.Type == "picture")?.Value; // Auth0 typically uses 'picture'
	var rolesFromClaims = user.FindAll(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray();
	var permissionsFromClaims = user.FindAll(c => c.Type == IdentityClaimTypes.Permissions).Select(c => c.Value).ToArray();
	var auth0UserIdFromClaims = user.FindFirstValue(ClaimTypes.NameIdentifier); // Subject ID

	if (string.IsNullOrEmpty(auth0UserIdFromClaims))
	{
		logger.LogWarning("Auth0 User ID (NameIdentifier) claim is missing for user in /api/authentication/user-info. Cannot fetch DB profile.");
		// Return a partial profile based on claims only, or an error.
		// For consistency, it's better if EnsureGolferProfile and GetMyProfile handle this.
		// Here, we will proceed assuming the API will handle it or we get a 404.
	}

	// 2. Call the TeeTimeTally.API's /api/golfers/me endpoint
	ApiGolferProfileResponse? dbProfile = null;
	var accessToken = await httpContext.GetTokenAsync("access_token");

	if (string.IsNullOrEmpty(accessToken))
	{
		logger.LogWarning("Access token for API call not found in /api/authentication/user-info. Cannot fetch DB profile.");
		// Proceed to return claims-based info, or decide to return an error/partial content
	}
	else
	{
		try
		{
			// Use the named HttpClient "BackendApiClient" which should have BaseAddress configured
			var apiClient = httpClientFactory.CreateClient("BackendApiClient");
			var request = new HttpRequestMessage(HttpMethod.Get, "api/golfers/me"); // Relative path to API
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

			var response = await apiClient.SendAsync(request, httpContext.RequestAborted);

			if (response.IsSuccessStatusCode)
			{
				var jsonResponse = await response.Content.ReadAsStringAsync();
				// Ensure JsonSerializerOptions are compatible with your API's casing
				dbProfile = JsonSerializer.Deserialize<ApiGolferProfileResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			}
			else
			{
				var errorContent = await response.Content.ReadAsStringAsync();
				logger.LogWarning("Failed to fetch golfer profile from API for user {Auth0UserId}. Status: {StatusCode}. API Response: {ErrorContent}", auth0UserIdFromClaims, response.StatusCode, errorContent);
				// If API returns 404, dbProfile will remain null. This is an expected scenario if EnsureProfile didn't run or link.
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Exception calling /api/golfers/me from BFF for user {Auth0UserId}.", auth0UserIdFromClaims);
			// dbProfile will remain null
		}
	}

	// 3. Merge and return
	if (dbProfile != null && !dbProfile.IsDeleted)
	{
		var comprehensiveInfo = new UserInfoResponse(
			Id: dbProfile.Id,
			Auth0UserId: dbProfile.Auth0UserId,
			FullName: dbProfile.FullName,
			Email: dbProfile.Email,
			IsSystemAdmin: dbProfile.IsSystemAdmin,
			CreatedAt: dbProfile.CreatedAt,
			UpdatedAt: dbProfile.UpdatedAt,
			ProfileImage: pictureFromClaims,
			Roles: rolesFromClaims,
			Permissions: permissionsFromClaims
		);
		return TypedResults.Ok(comprehensiveInfo);
	}
	else
	{

		logger.LogWarning("DB profile for user {Auth0UserId} was null or marked as deleted after attempting to fetch from API. Returning based on claims or potential error.", auth0UserIdFromClaims);


		if (dbProfile == null)
		{
			return Results.Problem(
			   title: "User Profile Not Found",
			   detail: "Could not retrieve the detailed user profile from the database.",
			   statusCode: StatusCodes.Status404NotFound);
		}
		else
		{ // dbProfile is not null but IsDeleted is true
			return Results.Problem(
			   title: "User Profile Inactive",
			   detail: "The user profile is currently inactive.",
			   statusCode: StatusCodes.Status403Forbidden); // Or another suitable status
		}
	}
}).RequireAuthorization(); // Ensures only authenticated users can hit this

app.MapReverseProxy();
app.MapForwarder("/{**catch-all}", app.Configuration["VueAppEndpoint"]!);

await app.RunAsync();
