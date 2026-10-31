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

/// <summary>
/// พอร์ตจาก src/modules/master/settings/settings.service.ts
///
/// หมายเหตุ: หนึ่งโครงการมีได้หลายแถวใน gv_settings แต่ทุกจุดในระบบเดิมอ่านแค่แถวแรก
/// (project.settings[0] และ findOne({ where: { project_id } })) จึงคงพฤติกรรมนี้ไว้
/// </summary>
public class SettingService(AstarLivingDbContext db, AuditStamp audit)
{
    private static readonly SortMap<Setting> Sort = new SortMap<Setting>("id")
        .Add("id", s => s.Id)
        .Add("project_id", s => s.ProjectId)
        .Add("maintenance_fee_rate", s => s.MaintenanceFeeRate)
        .Add("fine_rate", s => s.FineRate)
        .Add("collection_fee", s => s.CollectionFee)
        .Add("discount_rate", s => s.DiscountRate)
        .Add("effective_date", s => s.EffectiveDate)
        .Add("active", s => s.Active)
        .Add("created_at", s => s.CreatedAt)
        .Add("updated_at", s => s.UpdatedAt);

    private readonly AstarLivingDbContext _db = db;
    private readonly AuditStamp _audit = audit;

    public async Task<ApiResponse<SettingResponse>> CreateAsync(
        UpsertSettingRequest request, int? projectIdFromRoute, CurrentUser current, CancellationToken ct)
    {
        var projectId = projectIdFromRoute ?? request.ProjectId
            ?? throw AppException.BadRequest("กรุณาระบุโครงการ");

        if (!await _db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw AppException.BadRequest("รหัสโครงการที่ระบุไม่ถูกต้อง");
        }

        var setting = new Setting
        {
            ProjectId = projectId,
            MaintenanceFeeRate = request.MaintenanceFeeRate,
            FineRate = request.FineRate,
            CollectionFee = request.CollectionFee,
            DiscountRate = request.DiscountRate,
            EffectiveDate = request.EffectiveDate,
            InvoicePrefix = request.InvoicePrefix,
            ReceiptPrefix = request.ReceiptPrefix,
            InvoiceNote = request.InvoiceNote,
            ReceiptNote = request.ReceiptNote,
            Active = request.Active ?? true,
        };
        _audit.StampCreate(setting, current.UserId);

        _db.Settings.Add(setting);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Ok(ToResponse(setting));
    }

    public async Task<ApiResponse<IReadOnlyList<SettingResponse>>> FindAllAsync(
        SettingFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        var query = _db.Settings.AsNoTracking();

        // ระบบเดิมไม่กรองโครงการเลยในเส้นทางนี้ ระบบใหม่จำกัดให้ผู้ใช้ระดับโครงการ
        // เห็นได้แค่โครงการตัวเอง ส่วน Astar เห็นทั้งหมด
        if (!current.IsSuperAdmin && current.ProjectId is { } scoped)
        {
            query = query.Where(s => s.ProjectId == scoped);
        }

        if (filter.ProjectId is { } projectId) query = query.Where(s => s.ProjectId == projectId);
        if (filter.Active is { } active) query = query.Where(s => s.Active == active);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok,
            (IReadOnlyList<SettingResponse>)page.Items.Select(ToResponse).ToList(),
            page.PageInformation);
    }

    public async Task<ApiResponse<SettingResponse?>> FindOneAsync(int id, CancellationToken ct)
    {
        var setting = await _db.Settings.AsNoTracking()
            
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        return setting is null
            ? ApiResponseFactory.NotFound<SettingResponse>()
            : ApiResponseFactory.Create<SettingResponse?>(HttpStatusCatalog.Ok, ToResponse(setting));
    }

    /// <summary>อัตราค่าบริการของโครงการ — คืนแถวแรกเหมือนระบบเดิม</summary>
    public async Task<ApiResponse<SettingResponse?>> FindByProjectAsync(int projectId, CancellationToken ct)
    {
        var setting = await _db.Settings.AsNoTracking()
            
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct);

        return setting is null
            ? ApiResponseFactory.NotFound<SettingResponse>()
            : ApiResponseFactory.Create<SettingResponse?>(HttpStatusCatalog.Ok, ToResponse(setting));
    }

    public async Task<ApiResponse<SettingResponse?>> UpdateByProjectAsync(
        int projectId, UpsertSettingRequest request, CurrentUser current, CancellationToken ct)
    {
        var setting = await _db.Settings
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (setting is null) return ApiResponseFactory.NotFound<SettingResponse>();

        Apply(setting, request);
        _audit.StampUpdate(setting, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Create<SettingResponse?>(HttpStatusCatalog.Ok, ToResponse(setting));
    }

    public async Task<ApiResponse<SettingResponse?>> UpdateAsync(
        int id, UpsertSettingRequest request, CurrentUser current, CancellationToken ct)
    {
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (setting is null) return ApiResponseFactory.NotFound<SettingResponse>();

        Apply(setting, request);
        _audit.StampUpdate(setting, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Create<SettingResponse?>(HttpStatusCatalog.Ok, ToResponse(setting));
    }

    public async Task<ApiResponse<object>> RemoveAsync(int id, CurrentUser current, CancellationToken ct)
    {
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (setting is not null)
        {
            _audit.StampDelete(setting, current.UserId);
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponseFactory.Ok<object>(new { });
    }

    /// <summary>แก้เฉพาะฟิลด์ที่ส่งมา (PartialType ของเดิม)</summary>
    private static void Apply(Setting setting, UpsertSettingRequest request)
    {
        if (request.MaintenanceFeeRate is not null) setting.MaintenanceFeeRate = request.MaintenanceFeeRate;
        if (request.FineRate is not null) setting.FineRate = request.FineRate;
        if (request.CollectionFee is not null) setting.CollectionFee = request.CollectionFee;
        if (request.DiscountRate is not null) setting.DiscountRate = request.DiscountRate;
        if (request.EffectiveDate is not null) setting.EffectiveDate = request.EffectiveDate;
        if (request.InvoicePrefix is not null) setting.InvoicePrefix = request.InvoicePrefix;
        if (request.ReceiptPrefix is not null) setting.ReceiptPrefix = request.ReceiptPrefix;
        if (request.InvoiceNote is not null) setting.InvoiceNote = request.InvoiceNote;
        if (request.ReceiptNote is not null) setting.ReceiptNote = request.ReceiptNote;
        if (request.Active is { } active) setting.Active = active;
    }

    private static SettingResponse ToResponse(Setting s) => new()
    {
        Id = s.Id,
        ProjectId = s.ProjectId,
        MaintenanceFeeRate = s.MaintenanceFeeRate,
        FineRate = s.FineRate,
        CollectionFee = s.CollectionFee,
        EffectiveDate = s.EffectiveDate,
        InvoicePrefix = s.InvoicePrefix,
        ReceiptPrefix = s.ReceiptPrefix,
        InvoiceNote = s.InvoiceNote,
        ReceiptNote = s.ReceiptNote,
        DiscountRate = s.DiscountRate,
        Active = s.Active,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt,
    };
}
