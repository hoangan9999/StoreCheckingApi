using System.Text.Json;
using StoreChecking.Application.Abstractions;

namespace StoreChecking.Application.Backup;

/// <summary>
/// Builds the backup file's contents.
/// </summary>
public sealed class BackupService(IBackupRepository backup)
{
    /// <summary>
    /// The tables that go into a backup, in the order the previous implementation read them.
    /// <para>A FIXED list, not something a request can influence: it is interpolated into
    /// SQL, so anything else would be an injection hole.</para>
    /// <para>Deliberately the same eight as before and no more. notes, work_days,
    /// work_month_notes, english_words and speaking_saved were never in a backup and adding
    /// them here would quietly change what a backup file contains.</para>
    /// </summary>
    /// <summary>
    /// Bảng đã có từ đầu. KHÔNG phải danh sách để sao lưu — danh sách đó hỏi database.
    /// <para>Giữ lại làm mức sàn: nếu một trong những bảng này biến mất khỏi kết quả dò, thì
    /// hoặc truy vấn dò hỏng, hoặc bảng bị xoá thật. Cả hai đều phải nổ ra ngay, chứ không
    /// phải âm thầm cho ra một bản sao lưu thiếu — thứ chỉ lộ vào đúng ngày cần dùng.</para>
    /// </summary>
    private static readonly string[] MustInclude =
    [
        "batches",
        "products",
        "sales",
        "product_damages",
        "expense_categories",
        "expenses",
        "monthly_income",
        "packing_videos",
    ];

    public async Task<BackupDto> DumpAsync(CancellationToken ct = default)
    {
        var names = await backup.TablesAsync(ct);

        var missing = MustInclude.Except(names).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Sao lưu thiếu bảng đáng lẽ phải có: " + string.Join(", ", missing) +
                ". Dừng lại thay vì tạo ra một bản sao lưu khuyết.");
        }

        var tables = new Dictionary<string, JsonElement>(names.Count);
        var counts = new Dictionary<string, int>(names.Count);

        foreach (var name in names)
        {
            var rows = await backup.DumpAsync(name, ct);
            tables[name] = rows;
            counts[name] = rows.GetArrayLength();
        }

        return new BackupDto(counts, tables);
    }
}
