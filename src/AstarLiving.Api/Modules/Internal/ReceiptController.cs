using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AstarLiving.Api.Modules.Internal;

/// <summary>พอร์ตจาก src/modules/internal/receipt/receipt.controller.ts</summary>
[ApiController]
[Route("v1")]
[Authorize(Policy = AuthorizationPolicies.Operations)]
[Tags("Receipt (Transaction)")]
public class ReceiptController(ReceiptService service, ICurrentUserAccessor currentUser) : ControllerBase
{
    private readonly ReceiptService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    /// <summary>ออกใบเสร็จรับเงินค่าส่วนกลาง</summary>
    [HttpPost("receipts")]
    public Task<ApiResponse<ReceiptResponse>> Create(
        [FromBody] CreateReceiptRequest request, CancellationToken ct) =>
        _service.CreateAsync(request, _currentUser.Require(), ct);

    /// <summary>งวดที่ยังเลือกออกใบเสร็จได้ พร้อมบอกว่าบ้านนี้เข้าเงื่อนไขส่วนลดรายปีหรือไม่</summary>
    [HttpGet("receipts/residents/{residentId:int}")]
    public Task<ApiResponse<SelectableInvoicesResponse?>> GetSelectableInvoices(
        int residentId, CancellationToken ct) =>
        _service.GetSelectableInvoicesAsync(residentId, ct);

    /// <summary>สรุปยอดก่อนออกใบเสร็จ</summary>
    [HttpPost("receipts/residents/{residentId:int}")]
    public Task<ApiResponse<ReceiptPreviewResponse?>> CalculatePreview(
        int residentId, [FromBody] CalculateReceiptPreviewRequest request, CancellationToken ct) =>
        _service.CalculatePreviewAsync(residentId, request, ct);

    /// <summary>รายการใบเสร็จของโครงการ</summary>
    [HttpGet("projects/{projectId:int}/receipts")]
    public Task<ApiResponse<IReadOnlyList<ReceiptResponse>>> FindAllByProject(
        int projectId, [FromQuery] ReceiptFilterQuery filter, CancellationToken ct) =>
        _service.FindAllByProjectAsync(projectId, filter, ct);

    [HttpGet("receipts/{id:int}")]
    public Task<ApiResponse<ReceiptResponse?>> FindOne(int id, CancellationToken ct) =>
        _service.FindOneAsync(id, ct);

    [HttpPatch("receipts/{id:int}")]
    public Task<ApiResponse<ReceiptResponse?>> Update(
        int id, [FromBody] UpdateReceiptRequest request, CancellationToken ct) =>
        _service.UpdateAsync(id, request, _currentUser.Require(), ct);

    /// <summary>ยกเลิกใบเสร็จ และคืนสถานะงวดกับใบทวงถามที่เกี่ยวข้องทั้งหมด</summary>
    [HttpPatch("receipts/rejected/{id:int}")]
    public Task<ApiResponse<ReceiptResponse?>> Reject(
        int id, [FromBody] RejectReceiptRequest request, CancellationToken ct) =>
        _service.RejectAsync(id, request, _currentUser.Require(), ct);

    [HttpDelete("receipts/{id:int}")]
    public Task<ApiResponse<object>> Remove(int id, CancellationToken ct) =>
        _service.RemoveAsync(id, _currentUser.Require(), ct);
}
