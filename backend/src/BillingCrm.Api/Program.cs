using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BillingCrm.Api.Auth;
using BillingCrm.Api.Development;
using BillingCrm.Api.Endpoints;
using BillingCrm.Application;
using BillingCrm.Application.Auth;
using BillingCrm.Domain;
using BillingCrm.Infrastructure;
using BillingCrm.Infrastructure.Auth;
using BillingCrm.Infrastructure.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// A request body has a size limit (spec 012, research R4): 1 MiB is far larger than any legitimate request this API
// accepts (the largest today is an invoice with 100 lines). Kestrel enforces this itself while the body is read (the
// deep backstop, including against a client that lies about Content-Length); the explicit check on the declared
// Content-Length in the pipeline below (before CORS) answers the common case with the same 413 before the body is
// read at all, and is what a test host without a real Kestrel connection (in-memory TestServer) can also exercise.
const long maxRequestBodyBytes = 1_048_576;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBodyBytes);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Who is acting (spec 010): the signed-in user's email and role from the token, for history entries.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// 401 / 403 responses without a body get a ProblemDetails body from UseStatusCodePages below.
builder.Services.AddProblemDetails();

// By default minimal APIs *throw* on a malformed request body in Development, and the developer exception
// page then answers with a stack trace and the request headers (including the Bearer token). Answer with the
// same clean 400 problem details in every environment instead. Genuine bugs still get the developer page.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

// Numbers must be JSON numbers. The web defaults also accept "100" (a string) for a decimal, which would let a
// price arrive in a form the contract calls a wrong type; Strict refuses it with the same clean 400.
// DecimalJsonConverter (spec 012, research R2) additionally refuses exponent notation and more than 28 significant
// digits on every decimal-bound field (product price/VAT, invoice quantity/discount, payment amount) before a
// decimal is ever constructed from the token.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new DecimalJsonConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste the token from POST /api/auth/login.",
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        },
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtOptions>((options, jwt) =>
    {
        options.MapInboundClaims = false;
        // Keep the WWW-Authenticate header free of validation details such as expiry timestamps.
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwt.SigningKeyBytes),
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            // Pin the algorithm: only HS256 tokens signed with our key are accepted.
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            RequireExpirationTime = true,
            // Zero skew: "expired" means expired (spec FR-010), not "expired 5 minutes ago".
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "email",
            RoleClaimType = "role",
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Fail closed: an endpoint nobody annotated still requires a signed-in user.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy(AuthorizationPolicies.AdminOnly, policy => policy.RequireRole(nameof(StaffRole.Admin)));
});

// Login rate limiting (spec 012, research R5): unlimited login attempts were never throttled. One fixed window per
// source address, held in memory (enough for this single-instance application, an assumption the spec itself
// records), applied only to POST /api/auth/login below. Partitioned (not a single AddFixedWindowLimiter) so one
// source's attempts never throttle another's. The middleware's own defaults are not what FR-013 wants (rejection
// is 503, not 429, and a fixed window's rejected lease has no Retry-After header on its own), so both are set
// explicitly below.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        }

        return ValueTask.CompletedTask;
    };
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Any(origin => origin.Trim() == "*"))
{
    // WithOrigins("*") behaves like AllowAnyOrigin; arbitrary origins are never allowed (FR-008).
    throw new InvalidOperationException("Setting 'Cors:AllowedOrigins' must list explicit origins; '*' is not allowed.");
}

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .WithHeaders("Authorization", "Content-Type", "Accept")
        // Only the methods the API actually uses. Adding one is a deliberate act (there is no DELETE for customers).
        .WithMethods("GET", "POST", "PUT")));

// Development-only: the seeder is not even registered in any other environment, so the documented dev
// accounts cannot be created there (spec FR-005). The start-up service also refuses non-LocalDB databases.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<DevelopmentSeeder>();
    builder.Services.AddHostedService<DevelopmentSeedStartupService>();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Unexpected failures become a sanitised ProblemDetails instead of a bare 500.
    app.UseExceptionHandler();
}

app.UseStatusCodePages();

// Anti-framing and no-store (spec 012, research R3): nothing on this site should ever be frame-able by another
// origin, and an authenticated screen (an invoice, its notes) must never be restored from a shared machine's
// back/forward cache. A blanket rule on every response (X-Frame-Options) plus every /api response (Cache-Control)
// is simpler than doing this route by route and cannot develop a gap.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.Append("Cache-Control", "no-store");
    }

    // The declared Content-Length, checked before the body is read (see the comment at maxRequestBodyBytes above).
    if (context.Request.ContentLength is { } contentLength && contentLength > maxRequestBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    await next(context);
});

app.UseCors();

app.UseRateLimiter();

// Development only, and placed ahead of UseAuthentication/UseAuthorization: the global FallbackPolicy above
// requires an authenticated user on every endpoint, but Swagger has to be reachable without a token to be useful.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoint();
app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapCustomerEndpoints();
app.MapProductEndpoints();
app.MapInvoiceEndpoints();

app.Run();

public partial class Program;
