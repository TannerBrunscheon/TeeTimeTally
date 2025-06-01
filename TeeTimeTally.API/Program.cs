using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using TeeTimeTally.API.Identity;


var builder = WebApplication.CreateBuilder(args);

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
			RoleClaimType = ClaimTypes.Role,
			ValidateLifetime = true,
			//IssuerSigningKeyResolver = (token, securityToken, kid, parameters) => {
			//    // If you want to inspect how keys are resolved, though Authority should handle this.
			//    // This is more for advanced scenarios.
			//    return new List<SecurityKey>();
			//},
			AuthenticationType = JwtBearerDefaults.AuthenticationScheme // Added this line
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
