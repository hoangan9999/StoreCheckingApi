using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StoreChecking.Api.Auth;
using StoreChecking.Api.Data;
using StoreChecking.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// ---------- Cấu hình ----------
// Kiểm hết ở đây và báo lỗi RÕ RÀNG ngay lúc khởi động. Thiếu cấu hình mà để chạy tiếp
// thì nó sẽ nổ mơ hồ ở request đầu tiên ("MetadataAddress or Authority must use HTTPS"),
// rất khó đoán nguyên nhân.
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

// Swagger: bật sẵn khi chạy máy nhà. Trên NAS thì mặc định TẮT vì nó phơi bày toàn bộ
// danh sách endpoint; cần bật tạm thì đặt Swagger__Enabled=true.
var swaggerEnabled = builder.Configuration.GetValue("Swagger:Enabled", builder.Environment.IsDevelopment());

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

        // Nút Authorize: nhập token một lần, mọi request sau tự kèm header.
        // Microsoft.OpenApi v2 (Swashbuckle 10) bỏ thuộc tính Reference trên chính
        // OpenApiSecurityScheme — phải trỏ tới nó bằng OpenApiSecuritySchemeReference.
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Dán access token Supabase vào đây.",
        });
        // Swashbuckle 10 nhận một hàm dựng theo document, để reference gắn được vào
        // đúng tài liệu đang sinh.
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
        c.RoutePrefix = "swagger";       // mở ở /swagger
        c.DocumentTitle = "StoreChecking API";
    });
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ---------- Endpoint ----------

// Kiểm tra sống: dùng cho healthcheck của Docker và để biết DB có nối được không.
app.MapGet("/health", async (AppDbContext db) =>
{
    var dbOk = await db.Database.CanConnectAsync();
    return Results.Ok(new { ok = true, db = dbOk });
})
.WithName("Health")
.WithSummary("Sống chưa, DB nối được chưa (không cần token)")
.WithTags("Hệ thống");

// Soi nhanh token đang gửi lên có hợp lệ không, và user id là ai.
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

app.Run();
