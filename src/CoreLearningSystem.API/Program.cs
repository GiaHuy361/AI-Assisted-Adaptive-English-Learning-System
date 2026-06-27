using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using CoreLearningSystem.Application;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Infrastructure;
using CoreLearningSystem.API.Middlewares;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using Hangfire.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// 1. Register Application Layer Dependencies (MediatR & FluentValidation)
builder.Services.AddApplicationServices();

// 2. Register Infrastructure Layer Dependencies (DbContext, Repositories, Mock Publishers)
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck<CoreLearningSystem.API.Health.MySqlHealthCheck>("mysql")
    .AddCheck<CoreLearningSystem.API.Health.RedisHealthCheck>("redis");

builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();

// 3. Configure Swagger with JWT Authorization Support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Core Learning System API", Version = "v1" });
    
    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter JWT Bearer token **_only_**",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Id = JwtBearerDefaults.AuthenticationScheme,
            Type = ReferenceType.SecurityScheme
        }
    };
    c.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

// 4. Configure Authentication & Role-Based Authorization Schema
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secret = jwtSettings.GetValue<string>("Secret") ?? "A_DEFAULT_FALLBACK_SECRET_KEY_FOR_LOCAL_DEV";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false, // Relaxed for local dev testing
        ValidateAudience = false, // Relaxed for local dev testing
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.GetValue<string>("Issuer") ?? "AdaptiveEnglishLearningCore",
        ValidAudience = jwtSettings.GetValue<string>("Audience") ?? "AdaptiveEnglishLearningCoreUsers",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var validator = context.HttpContext.RequestServices.GetRequiredService<ITokenRevocationValidator>();
            var claimsPrincipal = context.Principal;
            if (claimsPrincipal == null)
            {
                context.Fail("No claims principal.");
                return;
            }
            var jwtIdClaim = claimsPrincipal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti);
            if (jwtIdClaim == null)
            {
                context.Fail("Token does not contain jti claim.");
                return;
            }
            var jwtId = jwtIdClaim.Value;
            var isRevoked = await validator.IsTokenRevokedAsync(jwtId);
            if (isRevoked)
            {
                context.Fail("Token has been revoked or session has expired/revoked.");
            }
        }
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174", "http://127.0.0.1:5173", "http://127.0.0.1:5174") // Allow both ports safely
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddAuthorization();

var app = builder.Build();

// 5. Wire Global Middlewares
app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Core Learning System API v1"));
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new AllowAllHangfireAuthorizationFilter() }
});

app.MapControllers();
app.MapHealthChecks("/health");

// Root redirect → Swagger (click localhost:5292 trên Docker Desktop nhảy thẳng vào Swagger)
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

// 6. Seed Database Data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<CoreLearningSystem.Infrastructure.Persistence.AppDbContext>();
        await context.Database.MigrateAsync();
        await CoreLearningSystem.Infrastructure.Persistence.DataSeeder.SeedAsync(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or seeding the database.");
        throw;
    }
}

app.Run();

public class AllowAllHangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
