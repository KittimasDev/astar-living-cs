using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Entities;
using AstarLiving.Infrastructure.Auditing;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Infrastructure.Queries;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.Master;

/// <summary>พอร์ตจาก src/modules/master/notice_template/notice_template.service.ts</summary>
public class NoticeTemplateService(AstarLivingDbContext db, AuditStamp audit)
{
    private static readonly SortMap<NoticeTemplate> Sort = new SortMap<NoticeTemplate>("id")
        .Add("id", t => t.Id)
        .Add("topic_name", t => t.TopicName)
        .Add("project_id", t => t.ProjectId)
        .Add("active", t => t.Active)
        .Add("created_at", t => t.CreatedAt)
        .Add("updated_at", t => t.UpdatedAt);

    private readonly AstarLivingDbContext _db = db;
    private readonly AuditStamp _audit = audit;

    public async Task<ApiResponse<NoticeTemplateResponse>> CreateAsync(
        UpsertNoticeTemplateRequest request, CurrentUser current, CancellationToken ct)
    {
        var projectId = request.ProjectId ?? current.ProjectId
            ?? throw AppException.BadRequest("กรุณาระบุโครงการ");

        if (!await _db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw AppException.BadRequest("รหัสโครงการที่ระบุไม่ถูกต้อง");
        }

        var template = new NoticeTemplate
        {
            TopicName = request.TopicName,
            IntroductionDetail = request.IntroductionDetail,
            AdditionalDetail = request.AdditionalDetail,
            ClosureText = request.ClosureText,
            ClosingWord = request.ClosingWord ?? string.Empty,
            Remark = request.Remark,
            ProjectId = projectId,
            Active = request.Active ?? true,
        };
        _audit.StampCreate(template, current.UserId);

        _db.NoticeTemplates.Add(template);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Ok(ToResponse(template));
    }

    public async Task<ApiResponse<IReadOnlyList<NoticeTemplateResponse>>> FindAllAsync(
        NoticeTemplateFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        var query = _db.NoticeTemplates.AsNoTracking()
            .Include(t => t.Project)
            .WhereContains(filter.TopicName, t => t.TopicName);

        if (!current.IsSuperAdmin && current.ProjectId is { } scoped)
        {
            query = query.Where(t => t.ProjectId == scoped);
        }

        if (filter.ProjectId is { } projectId) query = query.Where(t => t.ProjectId == projectId);
        if (filter.Active is { } active) query = query.Where(t => t.Active == active);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok,
            (IReadOnlyList<NoticeTemplateResponse>)page.Items.Select(ToResponse).ToList(),
            page.PageInformation);
    }

    public async Task<ApiResponse<NoticeTemplateResponse?>> FindOneAsync(int id, CancellationToken ct)
    {
        var template = await _db.NoticeTemplates.AsNoTracking()
            .Include(t => t.Project)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        return template is null
            ? ApiResponseFactory.NotFound<NoticeTemplateResponse>()
            : ApiResponseFactory.Create<NoticeTemplateResponse?>(HttpStatusCatalog.Ok, ToResponse(template));
    }

    public async Task<ApiResponse<NoticeTemplateResponse?>> UpdateAsync(
        int id, UpsertNoticeTemplateRequest request, CurrentUser current, CancellationToken ct)
    {
        var template = await _db.NoticeTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return ApiResponseFactory.NotFound<NoticeTemplateResponse>();

        if (request.TopicName is not null) template.TopicName = request.TopicName;
        if (request.IntroductionDetail is not null) template.IntroductionDetail = request.IntroductionDetail;
        if (request.AdditionalDetail is not null) template.AdditionalDetail = request.AdditionalDetail;
        if (request.ClosureText is not null) template.ClosureText = request.ClosureText;
        if (request.ClosingWord is not null) template.ClosingWord = request.ClosingWord;
        if (request.Remark is not null) template.Remark = request.Remark;
        if (request.ProjectId is { } projectId) template.ProjectId = projectId;
        if (request.Active is { } active) template.Active = active;

        _audit.StampUpdate(template, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Create<NoticeTemplateResponse?>(HttpStatusCatalog.Ok, ToResponse(template));
    }

    public async Task<ApiResponse<object>> RemoveAsync(int id, CurrentUser current, CancellationToken ct)
    {
        var template = await _db.NoticeTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is not null)
        {
            _audit.StampDelete(template, current.UserId);
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponseFactory.Ok<object>(new { });
    }

    private static NoticeTemplateResponse ToResponse(NoticeTemplate t) => new()
    {
        Id = t.Id,
        TopicName = t.TopicName,
        IntroductionDetail = t.IntroductionDetail,
        AdditionalDetail = t.AdditionalDetail,
        ClosureText = t.ClosureText,
        ClosingWord = t.ClosingWord,
        Remark = t.Remark,
        ProjectId = t.ProjectId,
        Active = t.Active,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        Project = t.Project is null ? null : new ProjectEmbedded
        {
            CreatedAt = t.Project.CreatedAt,
            CreatedBy = t.Project.CreatedBy,
            UpdatedAt = t.Project.UpdatedAt,
            UpdatedBy = t.Project.UpdatedBy,
            DeletedAt = t.Project.DeletedAt,
            DeletedBy = t.Project.DeletedBy,
            Id = t.Project.Id,
            Name = t.Project.Name,
            Address = t.Project.Address,
            ContactName = t.Project.ContactName,
            ContactPhone = t.Project.ContactPhone,
            Active = t.Project.Active,
            ContactFaxPhone = t.Project.ContactFaxPhone,
            ProjectLogo = t.Project.ProjectLogo,
        },
    };
}
