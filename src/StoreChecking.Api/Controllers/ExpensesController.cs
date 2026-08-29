using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreChecking.Application.Expenses;

namespace StoreChecking.Api.Controllers;

/// <summary>Chi tiêu — danh mục, giao dịch, thu nhập tháng và hai bảng tổng hợp.</summary>
[ApiController]
[Authorize]
[Route("api/expenses")]
[Tags("Chi tiêu")]
[Produces("application/json")]
public sealed class ExpensesController(ExpensesService expenses) : ControllerBase
{
    // ---------- Danh mục ----------

    /// <summary>Danh mục chi tiêu, theo thứ tự đã sắp.</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> ListCategories(CancellationToken ct) =>
        Ok(await expenses.ListCategoriesAsync(ct));

    /// <summary>Thêm một danh mục.</summary>
    /// <remarks>Không truyền `sortOrder` thì danh mục mới xếp cuối.</remarks>
    [HttpPost("categories")]
    public async Task<IActionResult> AddCategory([FromBody] SaveCategoryRequest body, CancellationToken ct)
    {
        var created = await expenses.AddCategoryAsync(body, ct);
        return Created($"/api/expenses/categories/{created.Id}", created);
    }

    /// <summary>Sửa một danh mục.</summary>
    /// <remarks>Thay thế toàn phần: trường nào không gửi sẽ bị xoá trắng.</remarks>
    [HttpPut("categories/{id:guid}")]
    public async Task<IActionResult> UpdateCategory(
        Guid id, [FromBody] SaveCategoryRequest body, CancellationToken ct)
    {
        var updated = await expenses.UpdateCategoryAsync(id, body, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Xoá một danh mục.</summary>
    /// <remarks>Danh mục còn giao dịch thì trả 400 — xoá được sẽ mất dấu tiền đã đi đâu.</remarks>
    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct) =>
        await expenses.DeleteCategoryAsync(id, ct) ? NoContent() : NotFound();

    // ---------- Giao dịch ----------

    /// <summary>Một trang giao dịch trong tháng, mới nhất trước.</summary>
    /// <remarks>
    /// Mặc định 20 giao dịch, tối đa 500. `categoryId` lọc theo danh mục, `on` lọc đúng
    /// một ngày (ô "chỉ hôm nay"). Trả kèm tổng số giao dịch và tổng tiền của CẢ tháng đã
    /// lọc, không phải của riêng trang này.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> List(
        int year, int month, int? limit, int? offset, Guid? categoryId, DateOnly? on,
        CancellationToken ct) =>
        Ok(await expenses.ListAsync(year, month, limit, offset, categoryId, on, ct));

    /// <summary>Thêm một giao dịch.</summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] SaveExpenseRequest body, CancellationToken ct)
    {
        var created = await expenses.AddAsync(body, ct);
        return Created($"/api/expenses/{created.Id}", created);
    }

    /// <summary>Sửa một giao dịch.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveExpenseRequest body, CancellationToken ct)
    {
        var updated = await expenses.UpdateAsync(id, body, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Xoá một giao dịch.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await expenses.DeleteAsync(id, ct) ? NoContent() : NotFound();

    // ---------- Thu nhập ----------

    /// <summary>Thu nhập từng tháng trong một năm.</summary>
    [HttpGet("income")]
    public async Task<IActionResult> ListIncome(int year, CancellationToken ct) =>
        Ok(await expenses.ListIncomeAsync(year, ct));

    /// <summary>Đặt thu nhập cho một tháng.</summary>
    /// <remarks>Tháng chưa có dòng thì tạo mới; đã có thì ghi đè. Mỗi tháng đúng một dòng.</remarks>
    [HttpPut("income")]
    public async Task<IActionResult> SetIncome([FromBody] SetIncomeRequest body, CancellationToken ct) =>
        Ok(await expenses.SetIncomeAsync(body, ct));

    // ---------- Tổng hợp ----------

    /// <summary>Chi theo từng danh mục trong một tháng.</summary>
    /// <remarks>Đọc từ view `v_expense_month_category` — Postgres gộp, không gộp lại ở đây.</remarks>
    [HttpGet("summary/categories")]
    public async Task<IActionResult> SpendByCategory(int year, int month, CancellationToken ct) =>
        Ok(await expenses.SpendByCategoryAsync(year, month, ct));

    /// <summary>Chi theo từng danh mục theo TỪNG NGÀY trong một tháng.</summary>
    /// <remarks>
    /// Dùng cho cảnh báo vượt hạn mức ngày. Trước đây trình duyệt tự cộng từ danh sách cả
    /// tháng; nay danh sách đã phân trang nên phải cộng ở nơi còn đủ dữ liệu.
    /// </remarks>
    [HttpGet("summary/days")]
    public async Task<IActionResult> SpendByDay(int year, int month, CancellationToken ct) =>
        Ok(await expenses.DayTotalsAsync(year, month, ct));

    /// <summary>Tổng chi từng tháng trong một năm.</summary>
    /// <remarks>Đọc từ view `v_expense_month_total`.</remarks>
    [HttpGet("summary/months")]
    public async Task<IActionResult> TotalsByMonth(int year, CancellationToken ct) =>
        Ok(await expenses.TotalsByMonthAsync(year, ct));
}
