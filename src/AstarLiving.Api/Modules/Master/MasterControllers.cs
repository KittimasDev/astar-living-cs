using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.Master;

/// <summary>พอร์ตจาก payment-method.controller.ts — @Controller('v1/payment_method')</summary>
[ApiController]
[Route("v1/payment_method")]
[Authorize(Policy = AuthorizationPolicies.ProjectSettings)]
[Tags("PaymentMethod (Master)")]
public class PaymentMethodController(PaymentMethodService service, ICurrentUserAccessor currentUser)
    : ControllerBase
{
    private readonly PaymentMethodService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    [HttpPost]
    public Task<ApiResponse<PaymentMethodResponse>> Create(
        [FromBody] UpsertPaymentMethodRequest request, CancellationToken ct) =>
        _service.CreateAsync(request, _currentUser.Require(), ct);

    /// <summary>
    /// รายการวิธีการชำระเงิน
    /// เปิดให้ระดับปฏิบัติงานอ่านได้ด้วย เพราะหน้าออกใบเสร็จต้องใช้เติม dropdown
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Operations)]
    public Task<ApiResponse<IReadOnlyList<PaymentMethodResponse>>> FindAll(
        [FromQuery] PaymentMethodFilterQuery filter, CancellationToken ct) =>
        _service.FindAllAsync(filter, ct);

    [HttpGet("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.Operations)]
    public Task<ApiResponse<PaymentMethodResponse?>> FindOne(int id, CancellationToken ct) =>
        _service.FindOneAsync(id, ct);

    [HttpPatch("{id:int}")]
    public Task<ApiResponse<PaymentMethodResponse?>> Update(
        int id, [FromBody] UpsertPaymentMethodRequest request, CancellationToken ct) =>
        _service.UpdateAsync(id, request, _currentUser.Require(), ct);

    [HttpDelete("{id:int}")]
    public Task<ApiResponse<object>> Remove(int id, CancellationToken ct) =>
        _service.RemoveAsync(id, _currentUser.Require(), ct);
}

/// <summary>
/// พอร์ตจาก settings.controller.ts — @Controller('v1') ที่มีเส้นทางทั้งแบบตรงและแบบผูกโครงการ
/// </summary>
[ApiController]
[Route("v1")]
[Authorize(Policy = AuthorizationPolicies.ProjectSettings)]
[Tags("Settings (Master)")]
public class SettingController(SettingService service, ICurrentUserAccessor currentUser) : ControllerBase
{
    private readonly SettingService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    [HttpPost("settings")]
    public Task<ApiResponse<SettingResponse>> Create(
        [FromBody] UpsertSettingRequest request, CancellationToken ct) =>
        _service.CreateAsync(request, null, _currentUser.Require(), ct);

    [HttpPost("projects/{projectId:int}/settings")]
    public Task<ApiResponse<SettingResponse>> CreateByProject(
        int projectId, [FromBody] UpsertSettingRequest request, CancellationToken ct) =>
        _service.CreateAsync(request, projectId, _currentUser.Require(), ct);

    [HttpGet("settings")]
    public Task<ApiResponse<IReadOnlyList<SettingResponse>>> FindAll(
        [FromQuery] SettingFilterQuery filter, CancellationToken ct) =>
        _service.FindAllAsync(filter, _currentUser.Require(), ct);

    [HttpGet("settings/{id:int}")]
    public Task<ApiResponse<SettingResponse?>> FindOne(int id, CancellationToken ct) =>
        _service.FindOneAsync(id, ct);

    /// <summary>
    /// อัตราค่าบริการของโครงการ — ทุกหน้าที่คิดเงินเรียกเส้นทางนี้
    /// จึงเปิดให้ระดับปฏิบัติงานอ่านได้
    /// </summary>
    [HttpGet("projects/{projectId:int}/settings")]
    [Authorize(Policy = AuthorizationPolicies.Operations)]
    public Task<ApiResponse<SettingResponse?>> FindByProject(int projectId, CancellationToken ct) =>
        _service.FindByProjectAsync(projectId, ct);

    [HttpPatch("projects/{projectId:int}/settings")]
    public Task<ApiResponse<SettingResponse?>> UpdateByProject(
        int projectId, [FromBody] UpsertSettingRequest request, CancellationToken ct) =>
        _service.UpdateByProjectAsync(projectId, request, _currentUser.Require(), ct);

    [HttpPatch("settings/{id:int}")]
    public Task<ApiResponse<SettingResponse?>> Update(
        int id, [FromBody] UpsertSettingRequest request, CancellationToken ct) =>
        _service.UpdateAsync(id, request, _currentUser.Require(), ct);

    [HttpDelete("settings/{id:int}")]
    public Task<ApiResponse<object>> Remove(int id, CancellationToken ct) =>
        _service.RemoveAsync(id, _currentUser.Require(), ct);
}

/// <summary>พอร์ตจาก notice_template.controller.ts — @Controller('v1/notice_template')</summary>
[ApiController]
[Route("v1/notice_template")]
[Authorize(Policy = AuthorizationPolicies.ProjectSettings)]
[Tags("NoticeTemplate (Master)")]
public class NoticeTemplateController(NoticeTemplateService service, ICurrentUserAccessor currentUser)
    : ControllerBase
{
    private readonly NoticeTemplateService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    [HttpPost]
    public Task<ApiResponse<NoticeTemplateResponse>> Create(
        [FromBody] UpsertNoticeTemplateRequest request, CancellationToken ct) =>
        _service.CreateAsync(request, _currentUser.Require(), ct);

    [HttpGet]
    public Task<ApiResponse<IReadOnlyList<NoticeTemplateResponse>>> FindAll(
        [FromQuery] NoticeTemplateFilterQuery filter, CancellationToken ct) =>
        _service.FindAllAsync(filter, _currentUser.Require(), ct);

    [HttpGet("{id:int}")]
    public Task<ApiResponse<NoticeTemplateResponse?>> FindOne(int id, CancellationToken ct) =>
        _service.FindOneAsync(id, ct);

    [HttpPatch("{id:int}")]
    public Task<ApiResponse<NoticeTemplateResponse?>> Update(
        int id, [FromBody] UpsertNoticeTemplateRequest request, CancellationToken ct) =>
        _service.UpdateAsync(id, request, _currentUser.Require(), ct);

    [HttpDelete("{id:int}")]
    public Task<ApiResponse<object>> Remove(int id, CancellationToken ct) =>
        _service.RemoveAsync(id, _currentUser.Require(), ct);
}

/// <summary>
/// พอร์ตจาก options.controller.ts — @Controller('v1/options')
/// ทุกเส้นทางคืน "อาร์เรย์ดิบ" ไม่ห่อซอง เพราะ dropdown ฝั่งหน้าจออ่านแบบนั้น
/// </summary>
[ApiController]
[Route("v1/options")]
[Authorize(Policy = AuthorizationPolicies.Operations)]
[Tags("Options (Master)")]
public class OptionsController(AstarLivingDbContext db) : ControllerBase
{
    private readonly AstarLivingDbContext _db = db;

    /// <summary>ชื่อโครงการ</summary>
    [HttpGet("projects/{projectId:int}")]
    public async Task<IReadOnlyList<OptionItem>> Projects(int projectId, CancellationToken ct) =>
        await _db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new OptionItem { Label = p.Name, Value = p.Id })
            .ToListAsync(ct);

    /// <summary>prefix ใบแจ้งหนี้จากอัตราค่าบริการของโครงการ</summary>
    [HttpGet("projects/{projectId:int}/settings")]
    public async Task<IReadOnlyList<OptionItem>> Settings(int projectId, CancellationToken ct) =>
        await _db.Settings.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Id)
            .Select(s => new OptionItem { Label = s.InvoicePrefix ?? string.Empty, Value = s.Id })
            .ToListAsync(ct);

    /// <summary>ผู้ใช้งานที่เปิดใช้งานอยู่ในโครงการ เรียงตามชื่อ</summary>
    [HttpGet("projects/{projectId:int}/users")]
    public async Task<IReadOnlyList<OptionItem>> Users(int projectId, CancellationToken ct) =>
        await _db.Users.AsNoTracking()
            .Where(u => u.ProjectId == projectId && u.Active)
            .OrderBy(u => u.FullName)
            .Select(u => new OptionItem { Label = u.FullName, Value = u.Id })
            .ToListAsync(ct);

    /// <summary>บ้านเลขที่ — ตัวนี้ถูกใช้เติม dropdown เกือบทุกหน้าจอ</summary>
    [HttpGet("projects/{projectId:int}/residents/house_no")]
    public async Task<IReadOnlyList<OptionItem>> ResidentHouseNumbers(int projectId, CancellationToken ct) =>
        await _db.Residents.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.Active)
            .OrderBy(r => r.HouseNo)
            .Select(r => new OptionItem { Label = r.HouseNo, Value = r.Id })
            .ToListAsync(ct);
}
