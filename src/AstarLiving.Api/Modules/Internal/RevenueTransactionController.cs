using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AstarLiving.Api.Modules.Internal;

/// <summary>
/// พอร์ตจาก src/modules/transactions/transactions.controller.ts
/// controller นี้ใช้ prefix "transactions" ตรง ๆ ไม่มี v1 ต่างจากโมดูลอื่น — คงไว้ตามเดิม
/// </summary>
[ApiController]
[Route("transactions")]
[Authorize(Policy = AuthorizationPolicies.Operations)]
[Tags("Revenue (Transaction)")]
public class RevenueTransactionController(
    RevenueTransactionService service, ICurrentUserAccessor currentUser) : ControllerBase
{
    private readonly RevenueTransactionService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    /// <summary>ออกใบเสร็จรายรับอื่น ๆ (ยอดรวมคำนวณใหม่ฝั่งเซิร์ฟเวอร์เสมอ)</summary>
    [HttpPost]
    public Task<ApiResponse<RevenueTransactionResponse>> Create(
        [FromBody] CreateRevenueTransactionRequest request, CancellationToken ct) =>
        _service.CreateAsync(request, _currentUser.Require(), ct);

    /// <summary>
    /// รายการใบเสร็จรายรับของโครงการ
    /// ระบบเดิมสั่งห้าม cache ที่ส่วนหัวของเส้นทางนี้ จึงทำแบบเดียวกัน
    /// </summary>
    [HttpGet("projects/{projectId:int}/revenues")]
    public async Task<ApiResponse<IReadOnlyList<RevenueTransactionResponse>>> FindAllByProject(
        int projectId, [FromQuery] ReceiptFilterQuery filter, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";

        return await _service.FindAllByProjectAsync(projectId, filter, ct);
    }

    [HttpPatch("rejected/{id:int}")]
    public Task<ApiResponse<RevenueTransactionResponse?>> Reject(
        int id, [FromBody] RejectRevenueTransactionRequest request, CancellationToken ct) =>
        _service.RejectAsync(id, request, _currentUser.Require(), ct);

    [HttpGet("{id:int}")]
    public Task<ApiResponse<RevenueTransactionResponse?>> FindOne(int id, CancellationToken ct) =>
        _service.FindOneAsync(id, ct);

    [HttpDelete("{id:int}")]
    public Task<ApiResponse<DeleteRevenueResult>> Remove(int id, CancellationToken ct) =>
        _service.RemoveAsync(id, _currentUser.Require(), ct);
}
