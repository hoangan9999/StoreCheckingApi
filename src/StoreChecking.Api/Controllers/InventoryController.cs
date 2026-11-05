using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreChecking.Application.Inventory;

namespace StoreChecking.Api.Controllers;

/// <summary>Kho hàng — lô nhập, sản phẩm, bán hàng và hàng hư.</summary>
/// <remarks>
/// Stock is checked here AND by triggers in the database. The check here produces a message
/// worth reading; the trigger holds when two sales of the last item arrive at once and both
/// pass their check.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/inventory")]
[Tags("Kho hàng")]
[Produces("application/json")]
public sealed class InventoryController(
    InventoryService inventory,
    LabelStockService labels) : ControllerBase
{
    // ---------- Lô hàng ----------

    /// <summary>Danh sách lô kèm tổng hợp, sắp theo ưu tiên rồi mới nhất trước.</summary>
    /// <remarks>Đã gộp sẵn `priority` — view `batch_summary` không có cột đó, nên trước đây client phải gọi hai lần.</remarks>
    [HttpGet("batches")]
    public async Task<IActionResult> ListBatches(CancellationToken ct) =>
        Ok(await inventory.ListBatchesAsync(ct));

    /// <summary>Tổng hợp của một lô.</summary>
    [HttpGet("batches/{id:guid}")]
    public async Task<IActionResult> GetBatch(Guid id, CancellationToken ct) =>
        await inventory.GetBatchAsync(id, ct) is { } b ? Ok(b) : NotFound();

    /// <summary>Tạo lô mới kèm luôn các sản phẩm trong lô.</summary>
    /// <remarks>Lô và sản phẩm ghi trong cùng một transaction — không có chuyện lô tồn tại nửa vời.</remarks>
    [HttpPost("batches")]
    public async Task<IActionResult> CreateBatch([FromBody] CreateBatchRequest body, CancellationToken ct)
    {
        var created = await inventory.CreateBatchAsync(body, ct);
        return Created($"/api/inventory/batches/{created.Id}", created);
    }

    /// <summary>Sửa thông tin lô, kèm ưu tiên khi bán.</summary>
    [HttpPut("batches/{id:guid}")]
    public async Task<IActionResult> UpdateBatch(Guid id, [FromBody] UpdateBatchRequest body, CancellationToken ct) =>
        await inventory.UpdateBatchAsync(id, body, ct) is { } b ? Ok(b) : NotFound();

    /// <summary>Xoá một lô.</summary>
    /// <remarks>Kéo theo sản phẩm, lịch sử bán và hàng hư của lô đó — `on delete cascade`.</remarks>
    [HttpDelete("batches/{id:guid}")]
    public async Task<IActionResult> DeleteBatch(Guid id, CancellationToken ct) =>
        await inventory.DeleteBatchAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Đặt lại ưu tiên nhiều lô cùng lúc (kéo-thả sắp thứ tự).</summary>
    /// <remarks>Trả về số lô thực sự đổi. Id không phải của mình thì bị bỏ qua, không báo lỗi.</remarks>
    [HttpPut("batches/priorities")]
    public async Task<IActionResult> SetPriorities([FromBody] SetPrioritiesRequest body, CancellationToken ct) =>
        Ok(new { changed = await inventory.SetPrioritiesAsync(body, ct) });

    // ---------- Sản phẩm ----------

    /// <summary>Sản phẩm trong lô, kèm tồn kho.</summary>
    [HttpGet("batches/{batchId:guid}/products")]
    public async Task<IActionResult> ListProducts(Guid batchId, CancellationToken ct) =>
        await inventory.ListProductsAsync(batchId, ct) is { } rows ? Ok(rows) : NotFound();

    /// <summary>Thêm một sản phẩm vào lô.</summary>
    [HttpPost("batches/{batchId:guid}/products")]
    public async Task<IActionResult> AddProduct(
        Guid batchId, [FromBody] SaveProductRequest body, CancellationToken ct)
    {
        var created = await inventory.AddProductAsync(batchId, body, ct);
        return created is null
            ? NotFound()
            : Created($"/api/inventory/batches/{batchId}/products", created);
    }

    /// <summary>Sửa một sản phẩm.</summary>
    /// <remarks>Không hạ được số lượng nhập xuống dưới số đã bán hoặc đã hư.</remarks>
    [HttpPut("products/{id:guid}")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] SaveProductRequest body, CancellationToken ct) =>
        await inventory.UpdateProductAsync(id, body, ct) is { } p ? Ok(p) : NotFound();

    /// <summary>Xoá một sản phẩm, kéo theo lịch sử bán và hàng hư của nó.</summary>
    [HttpDelete("products/{id:guid}")]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken ct) =>
        await inventory.DeleteProductAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Mọi sản phẩm còn hàng ở mọi lô, sắp theo ưu tiên lô.</summary>
    [HttpGet("stock")]
    public async Task<IActionResult> ListStock(CancellationToken ct) =>
        Ok(await inventory.ListStockAsync(ct));

    // ---------- Bán hàng ----------

    /// <summary>Một trang lịch sử bán, kèm tên sản phẩm và tên lô.</summary>
    /// <remarks>
    /// `limit` đếm theo ĐƠN chứ không theo dòng — một đơn nhiều sản phẩm vẫn về đủ.
    /// Mặc định 20 đơn, tối đa 5000. `from`/`to` là mốc thời gian tuyệt đối, `to` không tính.
    /// Trả kèm tổng số đơn và tổng tiền của CẢ khoảng, không phải của riêng trang này.
    /// </remarks>
    [HttpGet("sales")]
    public async Task<IActionResult> ListSales(
        int? limit, int? offset, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
        Ok(await inventory.ListSalesAsync(limit, offset, from, to, ct));

    /// <summary>Lịch sử bán của một lô.</summary>
    [HttpGet("batches/{batchId:guid}/sales")]
    public async Task<IActionResult> ListSalesByBatch(Guid batchId, CancellationToken ct) =>
        await inventory.ListSalesByBatchAsync(batchId, ct) is { } rows ? Ok(rows) : NotFound();

    /// <summary>Ghi một đơn bán, có thể gồm sản phẩm của nhiều lô.</summary>
    /// <remarks>
    /// Phí ship và ghi chú thuộc về ĐƠN nên chỉ ghi vào dòng đầu — lặp lại ở mọi dòng
    /// sẽ tính phí ship nhiều lần và làm doanh thu hụt đi.
    /// </remarks>
    [HttpPost("sales")]
    public async Task<IActionResult> RecordSale([FromBody] RecordSaleRequest body, CancellationToken ct)
    {
        var rows = await inventory.RecordSaleAsync(body, ct);
        return Created("/api/inventory/sales", rows);
    }

    /// <summary>Sửa một dòng bán.</summary>
    [HttpPut("sales/{id:guid}")]
    public async Task<IActionResult> UpdateSale(Guid id, [FromBody] UpdateSaleRequest body, CancellationToken ct) =>
        await inventory.UpdateSaleAsync(id, body, ct) is { } s ? Ok(s) : NotFound();

    /// <summary>Xoá một dòng bán.</summary>
    [HttpDelete("sales/{id:guid}")]
    public async Task<IActionResult> DeleteSale(Guid id, CancellationToken ct) =>
        await inventory.DeleteSaleAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Sửa cả một đơn: mọi dòng, kèm ngày, phí ship và ghi chú của đơn.</summary>
    /// <remarks>
    /// Dòng nào của đơn KHÔNG có trong `lines` sẽ bị xoá — đó là cách một đơn hai món
    /// thành đơn một món. Gửi danh sách rỗng thì trả 400: đơn không còn dòng nào là đơn
    /// đã xoá, mà xoá thì đã có lời gọi riêng nói đúng việc đó.
    /// </remarks>
    [HttpPut("sales/group/{groupId:guid}")]
    public async Task<IActionResult> UpdateSaleGroup(
        Guid groupId, [FromBody] UpdateSaleGroupRequest body, CancellationToken ct) =>
        await inventory.UpdateSaleGroupAsync(groupId, body, ct) is { } rows ? Ok(rows) : NotFound();

    /// <summary>Xoá cả một đơn — mọi dòng cùng `saleGroupId`.</summary>
    [HttpDelete("sales/group/{groupId:guid}")]
    public async Task<IActionResult> DeleteSaleGroup(Guid groupId, CancellationToken ct) =>
        await inventory.DeleteSaleGroupAsync(groupId, ct) > 0 ? NoContent() : NotFound();

    // ---------- Hàng hư ----------

    /// <summary>Hàng hư của một lô.</summary>
    [HttpGet("batches/{batchId:guid}/damages")]
    public async Task<IActionResult> ListDamages(Guid batchId, CancellationToken ct) =>
        await inventory.ListDamagesAsync(batchId, ct) is { } rows ? Ok(rows) : NotFound();

    /// <summary>Ghi nhận hàng hư.</summary>
    /// <remarks>Không ghi vượt quá tồn khả dụng — kiểm ở đây và trigger trong database chặn lần nữa.</remarks>
    [HttpPost("damages")]
    public async Task<IActionResult> AddDamage([FromBody] AddDamageRequest body, CancellationToken ct)
    {
        var created = await inventory.AddDamageAsync(body, ct);
        return Created($"/api/inventory/batches/{created.BatchId}/damages", created);
    }

    /// <summary>Xoá một dòng hàng hư.</summary>
    [HttpDelete("damages/{id:guid}")]
    public async Task<IActionResult> DeleteDamage(Guid id, CancellationToken ct) =>
        await inventory.DeleteDamageAsync(id, ct) ? NoContent() : NotFound();

    // ---------- Trừ tồn theo nhãn ----------
    //
    // Không có endpoint nào tự quyết định. Nhãn nào trừ, trừ vào sản phẩm nào, trừ mấy
    // cái — tất cả đều do người dùng chọn rồi gửi lên. Lý do nằm trong LabelStockService.

    /// <summary>Gợi ý: chữ trên nhãn này lần trước được trừ vào mặt hàng nào, mỗi món mấy cái.</summary>
    /// <remarks>Chỉ để chọn sẵn ô dropdown. Không có gì tự trừ theo danh sách này.</remarks>
    [HttpGet("label-aliases")]
    public async Task<IActionResult> ListAliases(CancellationToken ct) =>
        Ok(await labels.ListAliasesAsync(ct));

    /// <summary>Nhãn nào đã xử lý, kèm 7 ngày gần nhất để hoàn lại được.</summary>
    /// <remarks>
    /// `codes` là TẤT CẢ mã đã xử lý ở mọi thời điểm — client lấy danh sách nhãn trên NAS
    /// trừ đi cái này ra danh sách chờ. `recent` mới là phần bị giới hạn 7 ngày.
    /// </remarks>
    [HttpGet("label-actions")]
    public async Task<IActionResult> ListActions(CancellationToken ct) =>
        Ok(await labels.ListActionsAsync(ct));

    /// <summary>Trừ tồn cho một nhãn, theo đúng sản phẩm người dùng đã chọn.</summary>
    /// <remarks>
    /// Thiếu tồn thì KHÔNG trừ gì cả và trả 400 — để còn trừ lại được sau khi sửa tồn.
    /// Nhãn đã xử lý rồi mà gửi lại cũng trả 400: muốn làm lại thì hoàn lại trước.
    /// </remarks>
    [HttpPost("deduct-from-label")]
    public async Task<IActionResult> DeductFromLabel(
        [FromBody] DeductFromLabelRequest body, CancellationToken ct) =>
        Ok(await labels.DeductAsync(body, ct));

    /// <summary>Đánh dấu nhãn là đã xem và cố ý bỏ qua — hàng không theo dõi tồn.</summary>
    /// <remarks>Nhận cả danh sách: tick cả mẻ rồi bỏ qua một lượt là cách dùng bình thường, không phải ngoại lệ.</remarks>
    [HttpPost("label-actions/skip")]
    public async Task<IActionResult> SkipLabel([FromBody] SkipLabelRequest body, CancellationToken ct) =>
        Ok(await labels.SkipAsync(body, ct));

    /// <summary>Hoàn lại một quyết định: nhãn quay về danh sách chờ, dòng bán đã sinh ra thì xoá.</summary>
    /// <remarks>Chỉ trong 7 ngày. Quá hạn trả 400 chứ không âm thầm sửa sổ của tháng đã chốt.</remarks>
    [HttpDelete("label-actions/{orderCode}")]
    public async Task<IActionResult> UndoAction(string orderCode, CancellationToken ct) =>
        await labels.UndoAsync(orderCode, ct) ? NoContent() : NotFound();
}
