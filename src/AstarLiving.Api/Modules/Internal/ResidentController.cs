using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AstarLiving.Api.Modules.Internal;

/// <summary>
/// พอร์ตจาก src/modules/internal/resident/resident.controller.ts
/// controller เดิมใช้ @Controller('v1') แล้วเขียน path เต็มในแต่ละเมธอด
/// </summary>
[ApiController]
[Route("v1")]
[Authorize(Policy = AuthorizationPolicies.Operations)]
[Tags("Resident (Internal)")]
public class ResidentController(ResidentService service, ICurrentUserAccessor currentUser) : ControllerBase
{
    private readonly ResidentService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    [HttpPost("residents")]
    public Task<ApiResponse<ResidentResponse>> Create(
        [FromBody] CreateResidentRequest request, CancellationToken ct) =>
        _service.CreateAsync(request, _currentUser.Require(), ct);

    /// <summary>รายการลูกบ้านของโครงการ (แบ่งหน้า)</summary>
    [HttpGet("projects/{projectId:int}/residents")]
    public Task<ApiResponse<IReadOnlyList<ResidentResponse>>> FindByProject(
        int projectId, [FromQuery] ResidentFilterQuery filter, CancellationToken ct) =>
        _service.FindByProjectAsync(projectId, filter, _currentUser.Require(), ct);

    /// <summary>
    /// ลูกบ้านทั้งหมดของโครงการ
    /// ระบบเดิมเส้นทางนี้ไม่ได้กรองโครงการ (คืนทุกโครงการ) ระบบใหม่กรองให้ถูกต้อง
    /// </summary>
    [HttpGet("projects/{projectId:int}/residents/all")]
    public Task<ApiResponse<IReadOnlyList<ResidentResponse>>> FindAllInProject(
        int projectId, [FromQuery] ResidentFilterQuery filter, CancellationToken ct) =>
        _service.FindAllInProjectAsync(projectId, filter, _currentUser.Require(), ct);

    [HttpGet("residents/{id:int}")]
    public Task<ApiResponse<ResidentResponse?>> FindOne(int id, CancellationToken ct) =>
        _service.FindOneAsync(id, _currentUser.Require(), ct);

    [HttpPatch("residents/{id:int}")]
    public Task<ApiResponse<ResidentResponse?>> Update(
        int id, [FromBody] UpdateResidentRequest request, CancellationToken ct) =>
        _service.UpdateAsync(id, request, _currentUser.Require(), ct);

    [HttpDelete("residents/{id:int}")]
    public Task<ApiResponse<object>> Remove(int id, CancellationToken ct) =>
        _service.RemoveAsync(id, _currentUser.Require(), ct);
}
