using System.Security.Claims;

namespace StoreChecking.Api.Auth;

/// <summary>
/// Người dùng của request hiện tại, lấy từ claim `sub` trong JWT.
/// <para>Đây là MẢNH GHÉP THAY THẾ RLS của Supabase. Bên Supabase, database tự chặn:
/// quên điều kiện lọc thì Postgres vẫn không trả dữ liệu người khác. Ở đây không có
/// lưới an toàn đó, nên Id này được cắm vào global query filter của EF Core
/// (xem AppDbContext) để mọi truy vấn đều tự lọc theo chủ sở hữu.</para>
/// <para>KHÔNG BAO GIỜ lấy user id từ body hay query string của client.</para>
/// </summary>
public sealed class CurrentUser
{
    public Guid Id { get; }

    public CurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;
        // Supabase đặt user id ở claim `sub`. ASP.NET map `sub` sang NameIdentifier,
        // nên phải thử cả hai tuỳ cấu hình MapInboundClaims.
        var raw = principal?.FindFirstValue("sub")
                  ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        Id = Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    /// <summary>Chưa đăng nhập hoặc token không có `sub` hợp lệ.</summary>
    public bool IsAnonymous => Id == Guid.Empty;
}
