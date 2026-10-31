using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AstarLiving.Api.Modules.Master;

/// <summary>
/// พอร์ตจาก src/modules/master/project/project.controller.ts — คง path เดิมทุกเส้นทาง
///
/// สิทธิ์: หน้าตั้งค่าโครงการเป็นเมนูของ Astar เท่านั้น (ดู AuthorizationPolicies)
/// แต่เส้นทางอ่านรายการยังต้องเปิดให้ระดับโครงการใช้ได้ เพราะ sidebar ใช้ดึงโลโก้โครงการ
/// </summary>
[ApiController]
[Route("v1/project")]
[Authorize]
[Tags("Project (Master)")]
public class ProjectController(ProjectService service, ICurrentUserAccessor currentUser) : ControllerBase
{
    private readonly ProjectService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    /// <summary>สร้างโครงการ พร้อมสร้างอัตราค่าบริการและเทมเพลตใบทวงถามเปล่าให้อัตโนมัติ</summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.ProjectAdministration)]
    public Task<ApiResponse<ProjectResponse>> Create(
        [FromBody] CreateProjectRequest request, CancellationToken ct) =>
        _service.CreateAsync(request, _currentUser.Require(), ct);

    /// <summary>รายการโครงการ</summary>
    [HttpGet]
    public Task<ApiResponse<IReadOnlyList<ProjectResponse>>> FindAll(
        [FromQuery] ProjectFilterQuery filter, CancellationToken ct) =>
        _service.FindAllAsync(filter, ct);

    /// <summary>โครงการรายตัว</summary>
    [HttpGet("{id:int}")]
    public Task<ApiResponse<ProjectResponse?>> FindOne(int id, CancellationToken ct) =>
        _service.FindOneAsync(id, ct);

    [HttpPatch("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.ProjectAdministration)]
    public Task<ApiResponse<ProjectResponse?>> Update(
        int id, [FromBody] UpdateProjectRequest request, CancellationToken ct) =>
        _service.UpdateAsync(id, request, _currentUser.Require(), ct);

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.ProjectAdministration)]
    public Task<ApiResponse<object>> Remove(int id, CancellationToken ct) =>
        _service.RemoveAsync(id, _currentUser.Require(), ct);

    /// <summary>อัปโหลดโลโก้โครงการ (multipart/form-data ฟิลด์ชื่อ file)</summary>
    [HttpPost("{id:int}/upload")]
    [Authorize(Policy = AuthorizationPolicies.ProjectAdministration)]
    public Task<ApiResponse<ProjectLogoResponse>> UploadLogo(
        int id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw AppException.BadRequest("กรุณาเลือกไฟล์รูปภาพ");
        }

        return _service.UpdateImageAsync(id, file, _currentUser.Require(), ct);
    }
}
