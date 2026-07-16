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

// Register SignalR and its service mapping
builder.Services.AddSignalR();
builder.Services.AddSingleton<CoreLearningSystem.Application.Interfaces.ISignalRService, CoreLearningSystem.API.Hubs.SignalRService>();

builder.Services.AddHealthChecks()
    .AddCheck<CoreLearningSystem.API.Health.MySqlHealthCheck>("mysql")
    .AddCheck<CoreLearningSystem.API.Health.RedisHealthCheck>("redis");

builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
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
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
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


app.MapGet("/", async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(@"
        <!DOCTYPE html>
        <html>
        <head>
            <title>Group 05 - API Gateway</title>
            <style>
                body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #0F172A; color: #F8FAFC; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; }
                .card { background-color: #1E293B; padding: 40px; border-radius: 12px; box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.3); text-align: center; border: 1px solid #334155; max-width: 400px; width: 100%; }
                h1 { color: #38BDF8; margin-bottom: 8px; font-size: 24px; font-weight: 700; }
                p { color: #94A3B8; margin-bottom: 28px; font-size: 14px; }
                .btn { display: block; background-color: #0EA5E9; color: white; padding: 14px 20px; border-radius: 8px; text-decoration: none; font-weight: bold; transition: all 0.2s; margin: 12px 0; font-size: 15px; }
                .btn:hover { background-color: #0284C7; transform: translateY(-1px); }
                .btn-secondary { background-color: #475569; }
                .btn-secondary:hover { background-color: #334155; }
            </style>
        </head>
        <body>
            <div class='card'>
                <h1>Group 05 - API Gateway</h1>
                <p>AI-Assisted Adaptive English Learning System</p>
                <a href='/swagger' class='btn'>🚀 Go to Swagger UI</a>
                <a href='/hangfire' class='btn btn-secondary'>⏰ Go to Hangfire Dashboard</a>
            </div>
        </body>
        </html>
    ");
>>>>>>> feature/huy-backend-adaptive
});

app.MapControllers();
app.MapHub<CoreLearningSystem.API.Hubs.AppHub>("/hubs/app");
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
