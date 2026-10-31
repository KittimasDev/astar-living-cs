using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AstarLiving.Api.Modules.Internal;

/// <summary>
/// พอร์ตจาก src/modules/internal/collection/collection.controller.ts
/// เส้นทางกลุ่ม reduce คือหน้า "ลดหนี้" ส่วนกลุ่ม collections คือหน้า "ใบทวงถาม"
/// </summary>
[ApiController]
[Route("v1")]
[Authorize(Policy = AuthorizationPolicies.Operations)]
[Tags("Collection (Transaction)")]
public class CollectionController(CollectionService service, ICurrentUserAccessor currentUser)
    : ControllerBase
{
    private readonly CollectionService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    /// <summary>รายการลดหนี้ — หนึ่งแถวต่อหนึ่งงวด</summary>
    [HttpGet("reduce")]
    public Task<ApiResponse<IReadOnlyList<ReduceNoticeResponse>>> FindAllForReduce(
        [FromQuery] CollectionFilterQuery filter, CancellationToken ct) =>
        _service.FindAllForReduceAsync(filter, _currentUser.Require(), ct);

    /// <summary>ตรวจก่อนกดลดหนี้ว่างวดเดือนก่อนหน้ายังค้างอยู่หรือไม่</summary>
    [HttpGet("reduce-check/{id:int}")]
    public Task<ApiResponse<ReduceCheckResponse?>> ReduceCheck(int id, CancellationToken ct) =>
        _service.ReduceCheckAsync(id, _currentUser.Require(), ct);

    [HttpGet("reduce/{id:int}")]
    public Task<ApiResponse<ReduceNoticeResponse?>> FindOneForReduce(int id, CancellationToken ct) =>
        _service.FindOneForReduceAsync(id, _currentUser.Require(), ct);

    /// <summary>ลดหนี้ — ยกเลิกค่าเบี้ยปรับและค่าทวงถามของงวดนั้นทั้งหมด</summary>
    [HttpPatch("reduce/{id:int}")]
    public Task<ApiResponse<CancelReduceResponse?>> CancelReduce(
        int id, [FromBody] CancelReduceRequest request, CancellationToken ct) =>
        _service.CancelReduceAsync(id, request, _currentUser.Require(), ct);

    [HttpPost("collections")]
    public Task<ApiResponse<CollectionNoticeResponse>> Create(
        [FromBody] CreateCollectionRequest request, CancellationToken ct) =>
        _service.CreateAsync(request, _currentUser.Require(), ct);

    /// <summary>ใบทวงถามของโครงการ (เฉพาะชนิด COLLECTION ตามระบบเดิม)</summary>
    [HttpGet("projects/{projectId:int}/collections")]
    public Task<ApiResponse<IReadOnlyList<CollectionNoticeResponse>>> FindAllByProject(
        int projectId, [FromQuery] CollectionFilterQuery filter, CancellationToken ct) =>
        _service.FindAllByProjectAsync(projectId, filter, ct);

    /// <summary>ใบทวงถามที่ยังไม่ได้ชำระ</summary>
    [HttpGet("projects/{projectId:int}/collections/outstanding")]
    public Task<ApiResponse<IReadOnlyList<CollectionNoticeResponse>>> FindOutstanding(
        int projectId, CancellationToken ct) =>
        _service.FindOutstandingAsync(projectId, ct);

    [HttpGet("collections/{id:int}")]
    public Task<ApiResponse<CollectionNoticeResponse?>> FindOne(int id, CancellationToken ct) =>
        _service.FindOneAsync(id, ct);

    [HttpPatch("collections/{id:int}")]
    public Task<ApiResponse<CollectionNoticeResponse?>> Update(
        int id, [FromBody] UpdateCollectionRequest request, CancellationToken ct) =>
        _service.UpdateAsync(id, request, _currentUser.Require(), ct);

    [HttpDelete("collections/{id:int}")]
    public Task<ApiResponse<object>> Remove(int id, CancellationToken ct) =>
        _service.RemoveAsync(id, _currentUser.Require(), ct);
}
