using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Entities;
using AstarLiving.Infrastructure.Auditing;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Infrastructure.Queries;
using AstarLiving.Infrastructure.Storage;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.Master;

/// <summary>พอร์ตจาก src/modules/master/project/project.service.ts</summary>
public class ProjectService(
    AstarLivingDbContext db,
    AuditStamp audit,
    ImageStorage images,
    ThaiCalendar calendar)
{
    /// <summary>ค่าเริ่มต้นของ createOrderByFuc คือ id DESC</summary>
    private static readonly SortMap<Project> Sort = new SortMap<Project>("id")
        .Add("id", p => p.Id)
        .Add("name", p => p.Name)
        .Add("address", p => p.Address)
        .Add("contact_name", p => p.ContactName)
        .Add("contact_phone", p => p.ContactPhone)
        .Add("active", p => p.Active)
        .Add("created_at", p => p.CreatedAt)
        .Add("updated_at", p => p.UpdatedAt);

    private readonly AstarLivingDbContext _db = db;
    private readonly AuditStamp _audit = audit;
    private readonly ImageStorage _images = images;
    private readonly ThaiCalendar _calendar = calendar;

    /// <summary>
    /// สร้างโครงการ
    /// ผลข้างเคียงสำคัญ: ระบบเดิมสร้างแถว gv_settings (อัตราเป็น 0 ทั้งหมด)
    /// และแถว gv_notice_template เปล่าให้โครงการใหม่ด้วยเสมอ
    /// ถ้าไม่สร้าง หน้าตั้งค่าอัตราและหน้าเทมเพลตใบทวงถามจะไม่มีข้อมูลให้แก้
    /// </summary>
    public async Task<ApiResponse<ProjectResponse>> CreateAsync(
        CreateProjectRequest request, CurrentUser current, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var project = new Project
        {
            Name = request.Name,
            Address = request.Address,
            ContactName = request.ContactName,
            ContactPhone = request.ContactPhone,
            ContactFaxPhone = request.ContactFaxPhone,
            Active = request.Active ?? true,
        };

        // ระบบเดิมใช้ createDefaultNonActive() ตอนสร้างโครงการ จึงไม่ได้บันทึก created_by ของโครงการ
        // แต่บันทึกใน settings และ notice_template ที่สร้างตามมา — คงพฤติกรรมนี้ไว้
        project.CreatedAt = _audit.Now;
        project.UpdatedAt = _audit.Now;

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);

        var setting = new Setting
        {
            ProjectId = project.Id,
            MaintenanceFeeRate = 0m,
            FineRate = 0m,
            CollectionFee = 0m,
            DiscountRate = 0m,
        };
        _audit.StampCreate(setting, current.UserId);
        _db.Settings.Add(setting);

        var template = new NoticeTemplate
        {
            ProjectId = project.Id,
            TopicName = string.Empty,
            IntroductionDetail = string.Empty,
            AdditionalDetail = string.Empty,
            ClosureText = string.Empty,
            ClosingWord = string.Empty,
            Remark = string.Empty,
        };
        _audit.StampCreate(template, current.UserId);
        _db.NoticeTemplates.Add(template);

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return ApiResponseFactory.Ok(ToResponse(project, withLogoUrl: false));
    }

    public async Task<ApiResponse<IReadOnlyList<ProjectResponse>>> FindAllAsync(
        ProjectFilterQuery filter, CancellationToken ct)
    {
        var query = _db.Projects.AsNoTracking().WhereContains(filter.Name, p => p.Name);

        if (filter.Active is { } active) query = query.Where(p => p.Active == active);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok,
            (IReadOnlyList<ProjectResponse>)page.Items.Select(p => ToResponse(p)).ToList(),
            page.PageInformation);
    }

    public async Task<ApiResponse<ProjectResponse?>> FindOneAsync(int id, CancellationToken ct)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

        return project is null
            ? ApiResponseFactory.NotFound<ProjectResponse>()
            : ApiResponseFactory.Create<ProjectResponse?>(HttpStatusCatalog.Ok, ToResponse(project));
    }

    public async Task<ApiResponse<ProjectResponse?>> UpdateAsync(
        int id, UpdateProjectRequest request, CurrentUser current, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return ApiResponseFactory.NotFound<ProjectResponse>();

        if (request.Name is not null) project.Name = request.Name;
        if (request.Address is not null) project.Address = request.Address;
        if (request.ContactName is not null) project.ContactName = request.ContactName;
        if (request.ContactPhone is not null) project.ContactPhone = request.ContactPhone;
        if (request.ContactFaxPhone is not null) project.ContactFaxPhone = request.ContactFaxPhone;
        if (request.Active is { } active) project.Active = active;

        _audit.StampUpdate(project, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Create<ProjectResponse?>(
            HttpStatusCatalog.Ok, ToResponse(project, withLogoUrl: false));
    }

    /// <summary>ลบแบบ soft delete — ระบบเดิมคืน body เป็น object เปล่า</summary>
    public async Task<ApiResponse<object>> RemoveAsync(int id, CurrentUser current, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is not null)
        {
            _audit.StampDelete(project, current.UserId);
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponseFactory.Ok<object>(new { });
    }

    /// <summary>
    /// อัปโหลดโลโก้โครงการ
    /// ชื่อไฟล์เป็นเวลาไทยรูปแบบ YYYY_MM_DD_HH-mm-ss ลงท้าย .png
    /// แต่เนื้อไฟล์เป็น WebP ตามที่ระบบเดิมทำไว้ (ตั้งใจคงไว้ให้ path เข้ากันได้)
    /// </summary>
    public async Task<ApiResponse<ProjectLogoResponse>> UpdateImageAsync(
        int id, IFormFile file, CurrentUser current, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw AppException.NotFound("ไม่พบโครงการ");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);

        var compressed = await ImageStorage.CompressAsync(buffer.ToArray(), ct);
        var fileName = $"{_calendar.NowBangkok:yyyy_MM_dd_HH-mm-ss}.png";
        var stored = await _images.SaveAsync(compressed, $"uploads/project/{id}", fileName, ct);

        project.ProjectLogo = stored.RelativePath;
        _audit.StampUpdate(project, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Ok(new ProjectLogoResponse
        {
            Id = project.Id,
            Name = project.Name,
            ProjectLogo = _images.GetFileUrl(project.ProjectLogo),
        });
    }

    private ProjectResponse ToResponse(Project p, bool withLogoUrl = true) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Address = p.Address,
        ContactName = p.ContactName,
        ContactPhone = p.ContactPhone,
        ContactFaxPhone = p.ContactFaxPhone,
        ProjectLogo = withLogoUrl ? _images.GetFileUrl(p.ProjectLogo) : p.ProjectLogo,
        Active = p.Active,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
    };
}
