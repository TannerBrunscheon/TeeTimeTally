using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using TeeTimeTally.API.Auth;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

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
        options.Authority = $"https://{builder.Configuration["Auth0:Domain"]}";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = builder.Configuration["Auth0:Audience"],
            ValidIssuer = $"{builder.Configuration["Auth0:Domain"]}",
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = ClaimTypes.Role,
            ValidateLifetime = true,
        };
    });

builder.Services.AddAuthorization();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.UseCors("AllowSpecificOrigin");

app.Run();
