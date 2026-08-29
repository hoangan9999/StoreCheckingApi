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
public sealed class InventoryController(InventoryService inventory) : ControllerBase
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

    /// <summary>Lịch sử bán toàn bộ, kèm tên sản phẩm và tên lô. limit mặc định 300.</summary>
    [HttpGet("sales")]
    public async Task<IActionResult> ListSales(int? limit, CancellationToken ct) =>
        Ok(await inventory.ListSalesAsync(limit, ct));

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
}
