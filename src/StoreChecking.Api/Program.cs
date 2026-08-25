using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StoreChecking.Api.Auth;
using StoreChecking.Api.Data;
using StoreChecking.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// ---------- Cấu hình ----------
// SupabaseUrl chỉ dùng để LẤY KHOÁ CÔNG KHAI xác thực token — không lưu gì trên Supabase nữa.
// Sau này tự làm đăng nhập thì đổi Issuer/JwksUrl sang chỗ khác, phần còn lại giữ nguyên.
var supabaseUrl = (builder.Configuration["Auth:SupabaseUrl"] ?? "").TrimEnd('/');
var connString = builder.Configuration.GetConnectionString("Postgres")
                 ?? throw new InvalidOperationException("Thiếu ConnectionStrings__Postgres.");
var allowedOrigins = (builder.Configuration["Cors:Origins"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// ---------- Dịch vụ ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connString));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Supabase công bố khoá công khai (ES256) ở /auth/v1/.well-known/jwks.json.
        // JwtBearer tự tải và tự làm mới khi Supabase xoay khoá.
        o.Authority = $"{supabaseUrl}/auth/v1";
        o.MetadataAddress = $"{supabaseUrl}/auth/v1/.well-known/openid-configuration";
        o.RequireHttpsMetadata = true;

        // Giữ nguyên tên claim gốc, không để ASP.NET đổi `sub` thành NameIdentifier.
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

            // Cho phép lệch đồng hồ 60 giây. Chính là lỗi "JWT issued at future" đang
            // gặp bên Supabase: token vừa phát ra mà đồng hồ bên xác thực chậm hơn
            // một chút là bị từ chối. Ở đây ta chủ động dung sai.
            ClockSkew = TimeSpan.FromSeconds(60),
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length > 0) p.WithOrigins(allowedOrigins);
    else p.AllowAnyOrigin();          // chỉ nên xảy ra lúc chạy máy nhà
    p.AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ---------- Endpoint ----------

/// Kiểm tra sống: dùng cho healthcheck của Docker và để biết DB có nối được không.
app.MapGet("/health", async (AppDbContext db) =>
{
    var dbOk = await db.Database.CanConnectAsync();
    return Results.Ok(new { ok = true, db = dbOk });
});

/// Soi nhanh token đang gửi lên có hợp lệ không, và user id là ai.
app.MapGet("/api/me", (CurrentUser me, ClaimsPrincipal user) => Results.Ok(new
{
    userId = me.Id,
    email = user.FindFirst("email")?.Value,
})).RequireAuthorization();

app.MapWorkCalendar();

app.Run();
