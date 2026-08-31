using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProposalStudio.Data;
using ProposalStudio.Serialization;
using ProposalStudio.Services;

EnvFile.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);
var hosted = builder.Environment.IsEnvironment("Host");

var connection = builder.Configuration.GetConnectionString("DefaultConnection");
var jwtKey = builder.Configuration["JwtSettings:SecretKey"];
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "ProposalStudio";
var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? "ProposalStudio";
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("Set ConnectionStrings__DefaultConnection in the environment or .env.");
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Set JwtSettings__SecretKey (at least 32 characters) in the environment or .env.");
if (jwtKey == "YourSuperSecretKeyThatIsAtLeast32CharactersLong")
    throw new InvalidOperationException("JwtSettings__SecretKey is still the public sample value. Put a new key in .env.");

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

// -------------------- SERVICES --------------------

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connection));

builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = ProductImageStore.MaxUploadBytes;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = ProductImageStore.MaxUploadBytes;
});

builder.Services.AddControllers().AddNewtonsoftJson(options =>
    options.SerializerSettings.Converters.Add(new JsonDocumentConverter()));
builder.Services.AddScoped<PricingGovernance>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ProposalExpiryService>();
builder.Services.AddScoped<BrandStyleService>();
builder.Services.AddSingleton<ObjectMediaStore>();
builder.Services.AddSingleton<ProductImageStore>();
builder.Services.AddSingleton<ProposalPdfService>();

#if DEBUG
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Enter your JWT token"
        });

        c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Id = "Bearer",
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme
                    }
                },
                new List<string>()
            }
        });
    });
}
#endif

if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy
                .WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !hosted;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = JwtSessionEvents.RejectInactiveUsers
    };
});

// Require auth by default; opt out with [AllowAnonymous]
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// -------------------- BUILD APP --------------------

var app = builder.Build();

// Bring the schema up to date, then fill in the content the application needs.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbStartup");

    // A half-migrated schema breaks everything downstream, so this one is fatal.
    await db.Database.MigrateAsync();

    try
    {
        var seedOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<SeedOptions>>().Value;
        await DbSeeder.SeedAsync(db, seedOptions, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database seed failed — API will still start");
    }

    try
    {
        var archived = await scope.ServiceProvider
            .GetRequiredService<AuditService>()
            .ArchiveNotificationsAsync();
        if (archived > 0)
            logger.LogInformation("Moved {Count} leftover notifications into the audit log.", archived);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Notification archive failed — API will still start");
    }

    try
    {
        var expired = await scope.ServiceProvider
            .GetRequiredService<ProposalExpiryService>()
            .ExpireOverdueAsync();
        if (expired > 0)
            logger.LogInformation("Marked {Count} overdue proposal(s) expired.", expired);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Proposal expiry sweep failed — API will still start");
    }
}

// -------------------- PIPELINE --------------------

#if DEBUG
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
#endif

if (!hosted)
{
    app.UseHttpsRedirection();
}

// Serve wwwroot (product images at /images/products/{id}.png) without auth —
// middleware runs before UseAuthentication so catalog images load for login prefetch.
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "images", "products"));
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "pdfs"));
var webRoot = app.Environment.WebRootPath
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
await app.Services.GetRequiredService<ObjectMediaStore>()
    .RestoreAsync(webRoot, "images/products", "brand");
app.Services.GetRequiredService<ProductImageStore>().AdoptExisting();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var path = context.File.PhysicalPath ?? "";
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            // Long cache — frontend also prefetches these right after login
            context.Context.Response.Headers.Append("Cache-Control", "public, max-age=86400");
            context.Context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
        }
    }
});

// Missing static files must not fall through to the JWT FallbackPolicy
// (browser <img> / @font-face never send Authorization → would become 401 instead of 404).
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (path.StartsWithSegments("/images") ||
        path.StartsWithSegments("/pdfs") ||
        path.StartsWithSegments("/fonts") ||
        path.StartsWithSegments("/brand") ||
        path.StartsWithSegments("/assets"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

app.UseRouting();
if (corsOrigins.Length > 0)
{
    app.UseCors("AllowFrontend");
}
app.UseAuthentication();
app.Use(async (context, next) =>
{
    Guid? picked = null;
    if (context.Request.Headers.TryGetValue("X-Business-Id", out var header) &&
        Guid.TryParse(header.ToString(), out var fromHeader))
        picked = fromHeader;
    else if (context.Request.Query.TryGetValue("businessId", out var query) &&
             Guid.TryParse(query.ToString(), out var fromQuery))
        picked = fromQuery;

    BusinessScope.SetRequestBusinessId(picked);
    try
    {
        await next();
    }
    finally
    {
        BusinessScope.SetRequestBusinessId(null);
    }
});
app.UseAuthorization();
app.MapControllers();

if (hosted)
{
    app.MapFallbackToFile("index.html").AllowAnonymous();
}

app.Run();
