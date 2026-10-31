using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Entities;
using AstarLiving.Domain.Enums;
using AstarLiving.Infrastructure.Auditing;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Infrastructure.Queries;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.Master;

public class UserFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "username")]
    public string? Username { get; set; }

    [FromQuery(Name = "full_name")]
    public string? FullName { get; set; }
}

/// <summary>
/// พอร์ตจาก src/modules/internal/user/user.service.ts
///
/// สองจุดที่ต่างจากของเดิมโดยตั้งใจ
/// 1. ของเดิมเปิดให้ผู้ใช้ที่ล็อกอินคนไหนก็สร้างหรือแก้ผู้ใช้ได้ ระบบใหม่จำกัดด้วย policy
/// 2. ของเดิมไม่กรองโครงการ ผู้จัดการโครงการหนึ่งจึงเห็นและแก้ผู้ใช้ของอีกโครงการได้
///    ระบบใหม่จำกัดให้เห็นเฉพาะโครงการของตัวเอง ยกเว้น SUPERADMIN ที่ดูแลทุกโครงการ
/// </summary>
public class UserService(AstarLivingDbContext db, AuditStamp audit)
{
    private static readonly SortMap<User> Sort = new SortMap<User>("id")
        .Add("id", u => u.Id)
        .Add("username", u => u.Username)
        .Add("full_name", u => u.FullName)
        .Add("email", u => u.Email)
        .Add("role", u => u.Role)
        .Add("active", u => u.Active)
        .Add("created_at", u => u.CreatedAt)
        .Add("updated_at", u => u.UpdatedAt);

    /// <summary>cost เดียวกับระบบเดิม เพื่อให้ hash ที่สร้างใหม่ใช้ร่วมกันได้ทั้งสองระบบ</summary>
    private const int BcryptWorkFactor = 12;

    private readonly AstarLivingDbContext _db = db;
    private readonly AuditStamp _audit = audit;

    public async Task<ApiResponse<UserResponse>> CreateAsync(
        CreateUserRequest request, CurrentUser current, CancellationToken ct)
    {
        var username = request.Username.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Username == username, ct))
        {
            throw AppException.Conflict("ชื่อผู้ใช้งาน (Username) ถูกใช้งานแล้ว");
        }

        var role = ParseRole(request.Role);
        var projectId = ResolveProjectId(request.ProjectId, role, current);

        await EnsureProjectExistsAsync(projectId, ct);

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.PasswordHash, BcryptWorkFactor),
            FullName = request.FullName.Trim(),
            Email = Clean(request.Email),
            Phone = Clean(request.Phone),
            Role = role,
            ProjectId = projectId,
            Active = request.Active ?? true,
        };
        _audit.StampCreate(user, current.UserId);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Ok(ToResponse(user, null));
    }

    public async Task<ApiResponse<IReadOnlyList<UserResponse>>> FindAllAsync(
        UserFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        var query = _db.Users
            .AsNoTracking()
            .Include(u => u.Project)
            .WhereContains(filter.Username, u => u.Username)
            .WhereContains(filter.FullName, u => u.FullName);

        query = ScopeToProject(query, current);

        if (filter.Active is { } active) query = query.Where(u => u.Active == active);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok,
            (IReadOnlyList<UserResponse>)page.Items.Select(u => ToResponse(u, u.Project)).ToList(),
            page.PageInformation);
    }

    public async Task<ApiResponse<UserResponse?>> FindOneAsync(int id, CurrentUser current, CancellationToken ct)
    {
        var user = await ScopeToProject(_db.Users.AsNoTracking().Include(u => u.Project), current)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        return user is null
            ? ApiResponseFactory.NotFound<UserResponse>()
            : ApiResponseFactory.Create<UserResponse?>(HttpStatusCatalog.Ok, ToResponse(user, user.Project));
    }

    public async Task<ApiResponse<UserResponse?>> UpdateAsync(
        int id, UpdateUserRequest request, CurrentUser current, CancellationToken ct)
    {
        var user = await ScopeToProject(_db.Users, current).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return ApiResponseFactory.NotFound<UserResponse>();

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var username = request.Username.Trim().ToLowerInvariant();

            if (username != user.Username && await _db.Users.AnyAsync(u => u.Username == username && u.Id != id, ct))
            {
                throw AppException.Conflict("ชื่อผู้ใช้งาน (Username) ถูกใช้งานแล้ว");
            }

            user.Username = username;
        }

        if (!string.IsNullOrWhiteSpace(request.FullName)) user.FullName = request.FullName.Trim();
        if (request.Email is not null) user.Email = Clean(request.Email);
        if (request.Phone is not null) user.Phone = Clean(request.Phone);
        if (!string.IsNullOrWhiteSpace(request.Role)) user.Role = ParseRole(request.Role);

        if (request.ProjectId is { } projectId)
        {
            await EnsureProjectExistsAsync(projectId, ct);
            user.ProjectId = projectId;
        }

        if (request.Active is { } active) user.Active = active;

        // รหัสผ่านว่างแปลว่าไม่เปลี่ยน ไม่ใช่ตั้งเป็นค่าว่าง
        if (!string.IsNullOrWhiteSpace(request.PasswordHash))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.PasswordHash, BcryptWorkFactor);
        }

        _audit.StampUpdate(user, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Create<UserResponse?>(HttpStatusCatalog.Ok, ToResponse(user, null));
    }

    public async Task<ApiResponse<object>> RemoveAsync(int id, CurrentUser current, CancellationToken ct)
    {
        // กันไม่ให้ผู้ดูแลลบบัญชีตัวเองจนไม่เหลือใครเข้าระบบได้
        if (id == current.UserId)
        {
            throw AppException.BadRequest("ไม่สามารถลบบัญชีที่กำลังใช้งานอยู่ได้");
        }

        var user = await ScopeToProject(_db.Users, current).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is not null)
        {
            _audit.StampDelete(user, current.UserId);
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponseFactory.Ok<object>(new { });
    }

    /// <summary>
    /// ผู้ใช้ทั่วไปเห็นเฉพาะบัญชีในโครงการของตัวเอง ส่วน Astar เห็นทุกโครงการ
    /// ระบบเดิมไม่มีการกรองตรงนี้เลย
    /// </summary>
    private static IQueryable<User> ScopeToProject(IQueryable<User> query, CurrentUser current) =>
        current.Role == UserRole.SUPERADMIN
            ? query
            : query.Where(u => u.ProjectId == current.ProjectId);

    private static int? ResolveProjectId(int? requested, UserRole role, CurrentUser current)
    {
        // บัญชีระดับ Astar ไม่ผูกกับโครงการใดโครงการหนึ่ง
        if (role == UserRole.SUPERADMIN) return requested;

        return current.Role == UserRole.SUPERADMIN ? requested : current.ProjectId;
    }

    private async Task EnsureProjectExistsAsync(int? projectId, CancellationToken ct)
    {
        if (projectId is null) return;

        if (!await _db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw AppException.BadRequest("รหัสโครงการที่ระบุไม่ถูกต้อง หรือไม่มีอยู่ในระบบ");
        }
    }

    private static UserRole ParseRole(string role) =>
        Enum.TryParse<UserRole>(role.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : throw AppException.BadRequest($"สิทธิ์ผู้ใช้งาน \"{role}\" ไม่ถูกต้อง");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static UserResponse ToResponse(User u, Project? project) => new()
    {
        Id = u.Id,
        Username = u.Username,
        FullName = u.FullName,
        Email = u.Email,
        Phone = u.Phone,
        Role = u.Role.ToString(),
        ProjectId = u.ProjectId,
        Active = u.Active,
        CreatedAt = u.CreatedAt,
        UpdatedAt = u.UpdatedAt,
        Project = project is null ? null : new ProjectSummary
        {
            Id = project.Id,
            Name = project.Name,
            Address = project.Address,
            ContactName = project.ContactName,
            ContactPhone = project.ContactPhone,
            Active = project.Active,
        },
    };
}
