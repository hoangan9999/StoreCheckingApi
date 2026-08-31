using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StoreChecking.Api;
using StoreChecking.Api.Auth;
using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Common;
using StoreChecking.Infrastructure;
using StoreChecking.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration ----------
// Validate everything here and fail LOUDLY at startup. Letting a missing setting through
// produces a confusing crash on the first request ("MetadataAddress or Authority must use
// HTTPS") that points nowhere near the real cause.
//
// This runs BEFORE builder.Build(), which matters for the contract tests: configuration
// has to reach the process as environment variables, because anything added at Build()
// time arrives after these checks have already thrown.
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
// ScopeUser thay cho HttpCurrentUser: vẫn đọc chủ sở hữu từ token như cũ, nhưng cho phép
// việc chạy nền — vốn không có request nào phía sau — tự đặt chủ sở hữu cho đúng phạm vi
// của nó. Controller không bao giờ gọi RunAs, nên một request không thể chọn xem dữ liệu
// của người khác.
builder.Services.AddScoped<ScopeUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<ScopeUser>());

// Tự dựng video mỗi ngày. Đặt Media:DailyVideos = false để tắt.
var dailyVideos = builder.Configuration.GetValue<bool?>("Media:DailyVideos") ?? true;

// Khung giờ dựng video, rải đều trong ngày để có cái mà đăng vài tiếng một lần thay vì
// dồn cả mẻ lúc rạng sáng. Đặt Media:Slots = "7,11,14,17,20" để đổi.
var slots = (builder.Configuration["Media:Slots"] ?? "7,11,14,17,20")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(x => int.TryParse(x, out var h) ? h : -1)
    .Where(h => h is >= 0 and <= 23)
    .Distinct()
    .OrderBy(h => h)
    .ToArray();

// Giữ video bao nhiêu ngày rồi tự xoá. Ngày nào cũng có năm cái mới nên video cũ hết giá
// trị nhanh, mà không dọn thì mỗi tháng thêm khoảng một GB nằm lại.
var keepVideoDays = builder.Configuration.GetValue<int?>("Media:KeepVideoDays") ?? 5;

builder.Services.AddSingleton<VideoJobQueue>();
builder.Services.AddHostedService(sp => new DailyVideoService(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<VideoJobQueue>(),
    sp.GetRequiredService<ILogger<DailyVideoService>>(),
    slots, Math.Max(keepVideoDays, 1), dailyVideos));

// Repositories, unit of work and the application services all come from one place.
// Khoảng cách giữa hai lần hâm nóng database. Đặt Warmup:IntervalSeconds = 0 để tắt.
// Bốn phút: ngắn hơn mọi ngưỡng nghỉ đã biết có thể làm nguội đường đi, và nhẹ tới mức
// không đáng kể — mỗi lượt là một câu đếm trên bảng vài dòng.
var warmupSeconds = builder.Configuration.GetValue<int?>("Warmup:IntervalSeconds") ?? 240;

// Nơi chứa ảnh tải lên và video tự sinh. Mặc định hợp với volume khai trong compose.
var mediaRoot = builder.Configuration["Media:Root"] ?? "/data/media";

// Khoá AI và địa chỉ giọng đọc. Thiếu thì API vẫn chạy bình thường — chỉ việc dựng video
// hằng đêm là không làm được, và nó sẽ ghi rõ lý do vào log thay vì chặn cả máy chủ khởi động.
var geminiKey = builder.Configuration["Media:GeminiApiKey"] ?? "";
var ttsUrl = builder.Configuration["Media:TtsUrl"] ?? "http://host.docker.internal:5050/";

// Đăng video lên Fanpage. Thiếu khoá thì bỏ qua phần đăng, video vẫn dựng như thường.
var facebook = new StoreChecking.Infrastructure.Media.FacebookOptions
{
    PageId = builder.Configuration["Facebook:PageId"],
    AccessToken = builder.Configuration["Facebook:AccessToken"],
    OrderLink = builder.Configuration["Facebook:OrderLink"],
    Hashtags = builder.Configuration["Facebook:Hashtags"],
    ApiVersion = builder.Configuration["Facebook:ApiVersion"] ?? "v23.0",
};

builder.Services.AddInfrastructure(
    connString, TimeSpan.FromSeconds(Math.Max(warmupSeconds, 0)), mediaRoot, geminiKey, ttsUrl,
    facebook);
builder.Services.AddSingleton<LastRequestClock>();

builder.Services.AddControllers();

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

        // Endpoint summaries come from the XML doc comments on the controller actions, so
        // the Vietnamese labels in Swagger live next to the code they describe.
        var xml = Path.Combine(AppContext.BaseDirectory, "StoreChecking.Api.xml");
        if (File.Exists(xml)) c.IncludeXmlComments(xml);

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

// Bring the database schema up to date before serving anything.
//
// The alternative was doing it by hand: upload a .sql file to the NAS through File
// Station, open a terminal into the database container, run psql — four steps per module,
// and only possible from home. Now deploying the code deploys the schema.
//
// Set Schema__AutoMigrate=false to skip it, for the rare case of wanting the API up while
// the database is being worked on by hand.
if (builder.Configuration.GetValue("Schema:AutoMigrate", true))
{
    await app.Services.GetRequiredService<SchemaMigrator>().ApplyAsync();
}

// Fetch Supabase's public key now instead of during someone's first request.
//
// JwtBearer loads the OIDC document and the JWKS lazily, the first time it has to validate
// a token — and it does that INSIDE that request. /health needs no token so it stays fast,
// which is why the delay only ever showed up on the first real call: measured at tens of
// seconds from the NAS.
//
// Deliberately NOT awaited. Blocking startup on an outbound HTTPS call would recreate the
// failure this file already learned about once: anything that can hang before app.Run()
// leaves a container that is Up with nothing listening. Worst case here is that the warm-up
// loses the race and the first request pays what it used to.
_ = Task.Run(async () =>
{
    var clock = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        var jwt = app.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        if (jwt.ConfigurationManager is not null)
        {
            await jwt.ConfigurationManager.GetConfigurationAsync(CancellationToken.None);
            app.Logger.LogInformation("Đã nạp sẵn khoá công khai Supabase trong {Ms} ms.", clock.ElapsedMilliseconds);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex,
            "Không nạp sẵn được khoá công khai Supabase sau {Ms} ms. Lời gọi có token đầu " +
            "tiên sẽ phải tự đi lấy, nên sẽ chậm.", clock.ElapsedMilliseconds);
    }

    // Second half of the same problem: EF builds its model on the first query, and this
    // model carries a query filter generated by reflection for every IOwnedByUser entity.
    // One throwaway query pays that here rather than in whoever calls first. The schema
    // migrator does not cover it — that uses a raw Npgsql connection, never the DbContext.
    clock.Restart();
    try
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDatabaseHealth>().CanConnectAsync();
        app.Logger.LogInformation("Đã hâm nóng EF trong {Ms} ms.", clock.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Hâm nóng EF không thành công sau {Ms} ms.", clock.ElapsedMilliseconds);
    }
});

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

// Ghi lại thời điểm có request, để /health nói được nó đã nghỉ bao lâu trước lượt này.
// Đặt sớm nhất có thể, trước cả CORS, để đếm cả những request bị chặn giữa chừng.
app.Use(async (ctx, next) =>
{
    ctx.Items[LastRequestClock.ItemKey] = ctx.RequestServices
        .GetRequiredService<LastRequestClock>().Mark();
    await next();
});

app.UseCors();

// Turns the two kinds of failure into the two responses the client expects.
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
    catch (ValidationException ex)
    {
        // A use case refusing its input. Not a fault: the message is meant for the user,
        // and the shape { error } is what the Angular app reads.
        if (ctx.Response.HasStarted) throw;

        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
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

app.MapControllers();

app.Run();

// Exposes the implicit Program class generated from these top-level statements so the
// contract tests can boot the real application through WebApplicationFactory<Program>.
// Testing the assembled app is the point: a test that wired services by hand would not
// notice a route, a filter or a JSON setting going missing.
public partial class Program;
