using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using StoreChecking.Application.Abstractions;
using StoreChecking.Application.Backup;
using StoreChecking.Application.English;
using StoreChecking.Application.Expenses;
using StoreChecking.Application.Inventory;
using StoreChecking.Application.Notes;
using StoreChecking.Application.Packing;
using StoreChecking.Application.WorkCalendar;
using StoreChecking.Infrastructure.Persistence;
using StoreChecking.Infrastructure.Persistence.Repositories;

namespace StoreChecking.Infrastructure;

/// <summary>
/// One place that knows how the layers are wired, so Program.cs does not.
/// <para>Every new module adds its repositories and its service here. Keeping that in
/// Infrastructure rather than in the API means the API project never has to name a
/// concrete repository — it only ever sees the interfaces from Application.</para>
/// </summary>
public static class DependencyInjection
{
    /// <param name="warmupInterval">
    /// How often to touch the database so nobody's first request has to. Zero turns it off.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString, TimeSpan warmupInterval,
        string mediaRoot, string geminiApiKey, string ttsUrl,
        StoreChecking.Infrastructure.Media.FacebookOptions facebook)
    {
        // Pool settings applied before anything else sees the string, so the migrator and
        // the DbContext agree on how connections are kept.
        connectionString = ConnectionTuning.WithWarmPool(connectionString);

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

        // Applies db/*.sql at startup. Registered here so Program.cs only has to ask for it.
        services.AddSingleton(sp => new SchemaMigrator(
            connectionString, sp.GetRequiredService<ILogger<SchemaMigrator>>()));

        services.AddHostedService(sp => new DatabaseWarmupService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<DatabaseWarmupService>>(),
            warmupInterval));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDatabaseHealth, DatabaseHealth>();

        // Kho ảnh/video trên đĩa. Singleton vì nó chỉ giữ hai đường dẫn và tạo sẵn thư
        // mục một lần lúc khởi động, không giữ trạng thái nào theo từng request.
        services.AddSingleton<IMediaStorage>(sp => new StoreChecking.Infrastructure.Media.DiskMediaStorage(
            mediaRoot, sp.GetRequiredService<ILogger<StoreChecking.Infrastructure.Media.DiskMediaStorage>>()));

        // Dây chuyền dựng video: AI viết -> giọng đọc -> ffmpeg ghép.
        services.AddHttpClient();
        services.AddSingleton<IVideoRenderer>(sp => new StoreChecking.Infrastructure.Media.FfmpegVideoRenderer(
            sp.GetRequiredService<ILogger<StoreChecking.Infrastructure.Media.FfmpegVideoRenderer>>()));
        services.AddSingleton<IVoiceSynthesizer>(sp => new StoreChecking.Infrastructure.Media.AdamVoice(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<StoreChecking.Infrastructure.Media.AdamVoice>>(),
            ttsUrl));
        // Đăng video lên Fanpage. Thiếu khoá thì vẫn đăng ký bình thường — bên trong tự
        // báo Configured = false và MediaService bỏ qua, thay vì làm hỏng lúc khởi động.
        services.AddSingleton<IFanpagePublisher>(sp => new StoreChecking.Infrastructure.Media.FacebookPublisher(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<StoreChecking.Infrastructure.Media.FacebookPublisher>>(),
            facebook));

        services.AddSingleton<IScriptWriter>(sp => new StoreChecking.Infrastructure.Media.GeminiScriptWriter(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IVideoRenderer>(),
            sp.GetRequiredService<ILogger<StoreChecking.Infrastructure.Media.GeminiScriptWriter>>(),
            geminiApiKey));

        // ---------- Repositories ----------
        services.AddScoped<IWorkDayRepository, WorkDayRepository>();
        services.AddScoped<IWorkMonthNoteRepository, WorkMonthNoteRepository>();
        services.AddScoped<IEnglishWordRepository, EnglishWordRepository>();
        services.AddScoped<ISavedSentenceRepository, SavedSentenceRepository>();
        services.AddScoped<INoteRepository, NoteRepository>();
        services.AddScoped<IPackingVideoRepository, PackingVideoRepository>();
        services.AddScoped<IMediaImageRepository, MediaImageRepository>();
        services.AddScoped<IGeneratedVideoRepository, GeneratedVideoRepository>();
        services.AddScoped<IAppSettingRepository, AppSettingRepository>();
        services.AddScoped<IExpenseCategoryRepository, ExpenseCategoryRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IMonthlyIncomeRepository, MonthlyIncomeRepository>();
        services.AddScoped<IExpenseSummaryRepository, ExpenseSummaryRepository>();
        services.AddScoped<IBatchRepository, BatchRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IProductDamageRepository, ProductDamageRepository>();
        services.AddScoped<IBackupRepository, BackupRepository>();

        // ---------- Application services ----------
        // Registered here rather than in a separate AddApplication(): they have no
        // configuration of their own, and one list is easier to keep complete than two.
        services.AddScoped<WorkCalendarService>();
        services.AddScoped<StoreChecking.Application.Media.MediaService>();
        services.AddScoped<EnglishService>();
        services.AddScoped<NotesService>();
        services.AddScoped<PackingService>();
        services.AddScoped<ExpensesService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<BackupService>();

        return services;
    }
}
