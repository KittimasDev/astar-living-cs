using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Entities;
using AstarLiving.Infrastructure.Auditing;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Infrastructure.Queries;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.Internal;

/// <summary>พอร์ตจาก src/modules/internal/resident/resident.service.ts</summary>
public class ResidentService(AstarLivingDbContext db, AuditStamp audit)
{
    /// <summary>ระบบเดิมเรียง id ASC เป็นค่าเริ่มต้นในทุกเส้นทางของลูกบ้าน</summary>
    private static readonly SortMap<Resident> Sort = new SortMap<Resident>("id", defaultAscending: true)
        .Add("id", r => r.Id)
        .Add("house_no", r => r.HouseNo)
        .Add("full_name", r => r.FullName)
        .Add("ownership_ratio", r => r.OwnershipRatio)
        .Add("phone", r => r.Phone)
        .Add("email", r => r.Email)
        .Add("active", r => r.Active)
        .Add("created_at", r => r.CreatedAt)
        .Add("updated_at", r => r.UpdatedAt);

    private readonly AstarLivingDbContext _db = db;
    private readonly AuditStamp _audit = audit;

    public async Task<ApiResponse<ResidentResponse>> CreateAsync(
        CreateResidentRequest request, CurrentUser current, CancellationToken ct)
    {
        EnsureProjectAccess(current, request.ProjectId);

        if (!await _db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct))
        {
            throw AppException.BadRequest("รหัสโครงการที่ระบุไม่ถูกต้อง");
        }

        var resident = new Resident
        {
            ProjectId = request.ProjectId,
            HouseNo = request.HouseNo,
            FullName = request.FullName,
            OwnershipRatio = request.OwnershipRatio,
            Phone = request.Phone,
            Email = request.Email,
            Address = request.Address,
            Active = request.Active ?? true,
        };
        _audit.StampCreate(resident, current.UserId);

        _db.Residents.Add(resident);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Ok(ToResponse(resident));
    }

    /// <summary>GET /v1/projects/:project_id/residents — รายการลูกบ้านในโครงการ</summary>
    public async Task<ApiResponse<IReadOnlyList<ResidentResponse>>> FindByProjectAsync(
        int projectId, ResidentFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        EnsureProjectAccess(current, projectId);

        var query = _db.Residents.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .WhereContains(filter.FullName, r => r.FullName)
            .WhereContains(filter.HouseNo, r => r.HouseNo);

        if (filter.Active is { } active) query = query.Where(r => r.Active == active);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok,
            (IReadOnlyList<ResidentResponse>)page.Items.Select(ToResponse).ToList(),
            page.PageInformation);
    }

    /// <summary>
    /// GET /v1/projects/:project_id/residents/all
    ///
    /// ระบบเดิมมีบั๊กตรงนี้: ใช้ where {} จึงคืนลูกบ้าน "ทุกโครงการ" แม้จะระบุ project_id ใน path
    /// (ตารางบั๊กข้อ 5 ในแผน) ระบบใหม่กรองตามโครงการจริงตามที่ชื่อเส้นทางบอกไว้
    /// </summary>
    public async Task<ApiResponse<IReadOnlyList<ResidentResponse>>> FindAllInProjectAsync(
        int projectId, ResidentFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        EnsureProjectAccess(current, projectId);

        var query = _db.Residents.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .WhereContains(filter.FullName, r => r.FullName)
            .WhereContains(filter.HouseNo, r => r.HouseNo);

        if (filter.Active is { } active) query = query.Where(r => r.Active == active);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok,
            (IReadOnlyList<ResidentResponse>)page.Items.Select(ToResponse).ToList(),
            page.PageInformation);
    }

    public async Task<ApiResponse<ResidentResponse?>> FindOneAsync(
        int id, CurrentUser current, CancellationToken ct)
    {
        var resident = await _db.Residents.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (resident is null) return ApiResponseFactory.NotFound<ResidentResponse>();

        EnsureProjectAccess(current, resident.ProjectId);

        return ApiResponseFactory.Create<ResidentResponse?>(HttpStatusCatalog.Ok, ToResponse(resident));
    }

    public async Task<ApiResponse<ResidentResponse?>> UpdateAsync(
        int id, UpdateResidentRequest request, CurrentUser current, CancellationToken ct)
    {
        var resident = await _db.Residents.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (resident is null) return ApiResponseFactory.NotFound<ResidentResponse>();

        EnsureProjectAccess(current, resident.ProjectId);

        if (request.ProjectId is { } projectId)
        {
            EnsureProjectAccess(current, projectId);
            resident.ProjectId = projectId;
        }

        if (request.HouseNo is not null) resident.HouseNo = request.HouseNo;
        if (request.FullName is not null) resident.FullName = request.FullName;
        if (request.OwnershipRatio is not null) resident.OwnershipRatio = request.OwnershipRatio;
        if (request.Phone is not null) resident.Phone = request.Phone;
        if (request.Email is not null) resident.Email = request.Email;
        if (request.Address is not null) resident.Address = request.Address;
        if (request.Active is { } active) resident.Active = active;

        // ระบบเดิมลืมตั้ง updated_by ในเส้นทางนี้ ระบบใหม่บันทึกให้ครบเพื่อให้ตรวจย้อนหลังได้
        _audit.StampUpdate(resident, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Create<ResidentResponse?>(HttpStatusCatalog.Ok, ToResponse(resident));
    }

    public async Task<ApiResponse<object>> RemoveAsync(int id, CurrentUser current, CancellationToken ct)
    {
        var resident = await _db.Residents.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (resident is not null)
        {
            EnsureProjectAccess(current, resident.ProjectId);
            _audit.StampDelete(resident, current.UserId);
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponseFactory.Ok<object>(new { });
    }

    /// <summary>ผู้ใช้ระดับโครงการแตะได้เฉพาะโครงการตัวเอง ส่วน Astar แตะได้ทุกโครงการ</summary>
    private static void EnsureProjectAccess(CurrentUser current, int projectId)
    {
        if (current.IsSuperAdmin) return;
        if (current.ProjectId == projectId) return;

        throw AppException.Forbidden("ไม่มีสิทธิ์เข้าถึงข้อมูลของโครงการอื่น");
    }

    private static ResidentResponse ToResponse(Resident r) => new()
    {
        Id = r.Id,
        ProjectId = r.ProjectId,
        HouseNo = r.HouseNo,
        FullName = r.FullName,
        OwnershipRatio = r.OwnershipRatio,
        Phone = r.Phone,
        Email = r.Email,
        Address = r.Address,
        Active = r.Active,
        CreatedBy = r.CreatedBy,
        UpdatedBy = r.UpdatedBy,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };
}
