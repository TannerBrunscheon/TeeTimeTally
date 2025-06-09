using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Globalization;
using System.Security.Claims;
using TeeTimeTally.API.Identity;


var builder = WebApplication.CreateBuilder(args);

var cultureInfo = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
// Add FastEndpoints services
builder.Services.AddFastEndpoints();

builder.Services.AddSwaggerGen(options =>
{
	// adds a security definition named "Bearer" to Swagger, 
	// specifying that the API uses JWT Bearer tokens for authentication.
	options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
	{
		Description = "JWT Authorization: Bearer {token}",
		Name = "Authorization",
		In = ParameterLocation.Header,
		Type = SecuritySchemeType.Http,
		Scheme = "Bearer"
	});

	options.AddSecurityRequirement(new OpenApiSecurityRequirement()
	{
		{
			new OpenApiSecurityScheme
			{
				Reference = new OpenApiReference
				{
					Type = ReferenceType.SecurityScheme,
					Id = "Bearer"
				},
			},
			new List<string>()
		}
	});
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
	{
		options.Authority = builder.Configuration["Auth0:Domain"];
		options.Audience = builder.Configuration["Auth0:Audience"]; // Audience can also be set directly here
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidIssuer = builder.Configuration["Auth0:Domain"],
			NameClaimType = ClaimTypes.NameIdentifier,
			// IMPORTANT: Change RoleClaimType to "permissions"
			// This is crucial for Auth0 JWTs where permissions are in a 'permissions' array claim.
			RoleClaimType = "permissions", // <--- THIS IS THE KEY CHANGE
			ValidateLifetime = true,
			// IssuerSigningKeyResolver is usually not needed unless you have custom key retrieval logic
			// IssuerSigningKeyResolver = (token, securityToken, kid, parameters) => {
			//     return new List<SecurityKey>();
			// },
			// AuthenticationType is generally not set here; it's handled by the scheme name.
			// Removing it to avoid potential conflicts or redundancy.
			// AuthenticationType = JwtBearerDefaults.AuthenticationScheme // Removed this line
		};
		options.Events = new JwtBearerEvents
		{
			OnAuthenticationFailed = context =>
			{
				// Log detailed error
				Console.WriteLine("API Auth Failed: " + context.Exception.ToString());
				return Task.CompletedTask;
			},
			OnTokenValidated = context =>
			{
				Console.WriteLine("API Token Validated for: " + context.Principal!.Identity!.Name);
				// You can add more logging here to see the claims after JwtBearer processing
				Console.WriteLine("Claims after JwtBearer validation:");
				foreach (var claim in context.Principal.Claims)
				{
					Console.WriteLine($"  Type: {claim.Type}, Value: {claim.Value}");
				}
				return Task.CompletedTask;
			},
			OnChallenge = context =>
			{
				Console.WriteLine("API OnChallenge: " + context.Error + " - " + context.ErrorDescription);
				return Task.CompletedTask;
			},
			OnMessageReceived = context =>
			{
				Console.WriteLine("API Message Received. Token: " + (string.IsNullOrEmpty(context.Token) ? "NOT FOUND" : "FOUND"));
				return Task.CompletedTask;
			}
		};
	});

builder.Services.AddAuthorization();

builder.Services.AddNpgsqlDataSource(builder.Configuration["DBConnectionString"]!);


builder.Services.AddSingleton<IAuthorizationPolicyProvider, ApplicationAuthorizationPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

builder.Services.AddCors(options =>
{
	options.AddPolicy("AllowSpecificOrigin",
		builder =>
		{
			builder
			.WithOrigins(["https://localhost:7248", "https://localhost:5054"])
			.AllowAnyMethod()
			.AllowAnyHeader()
			.AllowCredentials();
		});
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowSpecificOrigin");


app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints(config =>
{
	config.Endpoints.RoutePrefix = "api"; // Optional: if you want all FastEndpoints to be under /api
});



app.UseCors("AllowSpecificOrigin");

app.Run();
