using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StoreChecking.Api.Auth;
using StoreChecking.Api.Data;
using StoreChecking.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration ----------
// Validate everything here and fail LOUDLY at startup. Letting a missing setting through
// produces a confusing crash on the first request ("MetadataAddress or Authority must use
// HTTPS") that points nowhere near the real cause.
var connString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connString))
{
    throw new InvalidOperationException(
        "Thiếu ConnectionStrings__Postgres. Đặt biến môi trường, ví dụ: " +
        "Host=localhost;Port=55432;Database=storechecking;Username=sc;Password=...");
}

var supabaseUrl = (builder.Configuration["Auth:SupabaseUrl"] ?? "").Trim().TrimEnd('/');
if (string.IsNullOrWhiteSpace(supabaseUrl))
{
    throw new InvalidOperationException(
        "Thiếu Auth__SupabaseUrl. Đây là URL project Supabase (chỉ dùng để lấy khoá công " +
        "khai xác thực token), ví dụ: https://xxxxxxxx.supabase.co");
}
if (!supabaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Auth__SupabaseUrl phải bắt đầu bằng https:// — đang là '{supabaseUrl}'. " +
        "JwtBearer bắt buộc HTTPS khi tải khoá công khai.");
}

var allowedOrigins = (builder.Configuration["Cors:Origins"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// Swagger is on by default when developing locally and OFF in production, where it would
// expose the whole endpoint surface. Set Swagger__Enabled=true to turn it on temporarily.
var swaggerEnabled = builder.Configuration.GetValue("Swagger:Enabled", builder.Environment.IsDevelopment());

// ---------- Services ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connString));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Supabase publishes its public key (ES256) at /auth/v1/.well-known/jwks.json.
        // JwtBearer fetches it and refreshes automatically when Supabase rotates keys.
        o.Authority = $"{supabaseUrl}/auth/v1";
        o.MetadataAddress = $"{supabaseUrl}/auth/v1/.well-known/openid-configuration";
        o.RequireHttpsMetadata = true;

        // Keep the original claim names; do not let ASP.NET rename `sub` to NameIdentifier.
        o.MapInboundClaims = false;

        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseUrl}/auth/v1",
            ValidateAudience = true,
            ValidAudiences = ["authenticated"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "sub",

            // Tolerate 60s of clock skew. This is exactly the "JWT issued at future"
            // failure seen on Supabase: a freshly minted token is rejected when the
            // validating side's clock runs slightly behind the issuer's.
            ClockSkew = TimeSpan.FromSeconds(60),
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length > 0) p.WithOrigins(allowedOrigins);
    else p.AllowAnyOrigin();          // should only ever happen while developing locally
    p.AllowAnyHeader().AllowAnyMethod();
}));

if (swaggerEnabled)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "StoreChecking API",
            Version = "v1",
            Description =
                "Bấm **Authorize** rồi dán access token của Supabase (KHÔNG cần gõ chữ 'Bearer'). " +
                "Lấy token: mở app Angular đã đăng nhập, F12 → Console → " +
                "JSON.parse(Object.entries(localStorage).find(([k])=>k.includes('auth-token'))[1].replace(/^base64-/,(m)=>'')).access_token",
        });

        // Authorize button: paste the token once and every later request carries the header.
        // Microsoft.OpenApi v2 (Swashbuckle 10) dropped the Reference property on
        // OpenApiSecurityScheme, so it has to be referenced via OpenApiSecuritySchemeReference.
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Dán access token Supabase vào đây.",
        });
        // Swashbuckle 10 takes a factory over the document so the reference can bind to
        // the document currently being generated.
        c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", doc)] = new List<string>(),
        });
    });
}

var app = builder.Build();

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "StoreChecking API v1");
        c.RoutePrefix = "swagger";       // served at /swagger
        c.DocumentTitle = "StoreChecking API";
    });
}

app.UseCors();

// Turn an unhandled exception into a readable JSON 500 instead of an empty one.
//
// Must sit AFTER UseCors. The built-in handling clears the response before writing the
// 500, which throws away the CORS headers with it — and a 500 without those headers
// reaches the browser as a plain "failed to fetch", so the real cause never leaves the
// server. Writing the response here keeps the headers UseCors already added.
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Lỗi chưa bắt ở {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

        // Response already on the wire — nothing left to change.
        if (ctx.Response.HasStarted) throw;

        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = "Máy chủ gặp lỗi khi xử lý yêu cầu.",
            traceId = ctx.TraceIdentifier,
        });
    }
});

app.UseAuthentication();
app.UseAuthorization();

// ---------- Endpoints ----------

// Liveness probe: used by the Docker healthcheck, and tells us whether the DB is reachable.
app.MapGet("/health", async (AppDbContext db) =>
{
    var dbOk = await db.Database.CanConnectAsync();
    return Results.Ok(new { ok = true, db = dbOk });
})
.WithName("Health")
.WithSummary("Sống chưa, DB nối được chưa (không cần token)")
.WithTags("Hệ thống");

// Quick way to check whether the supplied token is valid and which user it belongs to.
app.MapGet("/api/me", (CurrentUser me, ClaimsPrincipal user) => Results.Ok(new
{
    userId = me.Id,
    email = user.FindFirst("email")?.Value,
}))
.RequireAuthorization()
.WithName("Me")
.WithSummary("Token có hợp lệ không, user id là ai")
.WithTags("Hệ thống");

app.MapWorkCalendar();
app.MapEnglish();

app.Run();
