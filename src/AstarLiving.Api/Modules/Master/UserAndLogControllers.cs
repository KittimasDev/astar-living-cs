using AstarLiving.Api.Auth;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AstarLiving.Api.Modules.Master;

/// <summary>
/// พอร์ตจาก src/modules/internal/user/user.controller.ts — คง path เดิม v1/user
/// ต่างจากเดิมตรงที่บังคับสิทธิ์จริง ของเดิมผู้ใช้ที่ล็อกอินคนไหนก็เรียกได้
/// </summary>
[ApiController]
[Route("v1/user")]
[Authorize(Policy = AuthorizationPolicies.UserManagement)]
[Tags("User (Master)")]
public class UserController(UserService service, ICurrentUserAccessor currentUser) : ControllerBase
{
    private readonly UserService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    [HttpPost]
    public Task<ApiResponse<UserResponse>> Create(
        [FromBody] CreateUserRequest request, CancellationToken ct) =>
        _service.CreateAsync(request, _currentUser.Require(), ct);

    [HttpGet]
    public Task<ApiResponse<IReadOnlyList<UserResponse>>> FindAll(
        [FromQuery] UserFilterQuery filter, CancellationToken ct) =>
        _service.FindAllAsync(filter, _currentUser.Require(), ct);

    [HttpGet("{id:int}")]
    public Task<ApiResponse<UserResponse?>> FindOne(int id, CancellationToken ct) =>
        _service.FindOneAsync(id, _currentUser.Require(), ct);

    [HttpPatch("{id:int}")]
    public Task<ApiResponse<UserResponse?>> Update(
        int id, [FromBody] UpdateUserRequest request, CancellationToken ct) =>
        _service.UpdateAsync(id, request, _currentUser.Require(), ct);

    [HttpDelete("{id:int}")]
    public Task<ApiResponse<object>> Remove(int id, CancellationToken ct) =>
        _service.RemoveAsync(id, _currentUser.Require(), ct);
}

/// <summary>
/// พอร์ตจาก src/modules/log/log.controller.ts
/// เส้นทาง logs/{user_id} และ projects/{project_id}/logs ของเดิมถูกยุบมาเป็นตัวกรองของเส้นทางเดียว
/// เพราะทั้งสามเส้นทางคืนข้อมูลชุดเดียวกันต่างกันแค่เงื่อนไข และของเดิมไม่มีการแบ่งหน้าเลย
/// </summary>
[ApiController]
[Route("v1")]
[Authorize(Policy = AuthorizationPolicies.SystemLogs)]
[Tags("Log (Internal)")]
public class ActivityLogController(ActivityLogService service, ICurrentUserAccessor currentUser) : ControllerBase
{
    private readonly ActivityLogService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    [HttpGet("logs")]
    public Task<ApiResponse<IReadOnlyList<ActivityLogResponse>>> FindAll(
        [FromQuery] ActivityLogFilterQuery filter, CancellationToken ct) =>
        _service.FindAllAsync(filter, _currentUser.Require(), ct);

    [HttpGet("logs/{userId:int}")]
    public Task<ApiResponse<IReadOnlyList<ActivityLogResponse>>> FindByUser(
        int userId, [FromQuery] ActivityLogFilterQuery filter, CancellationToken ct)
    {
        filter.UserId = userId;
        return _service.FindAllAsync(filter, _currentUser.Require(), ct);
    }
}
