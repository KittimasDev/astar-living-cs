using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Enums;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Infrastructure.Queries;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.Master;

public class ActivityLogFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "user_id")]
    public int? UserId { get; set; }

    [FromQuery(Name = "action")]
    public string? Action { get; set; }

    [FromQuery(Name = "target_table")]
    public string? TargetTable { get; set; }
}

/// <summary>
/// บันทึกการทำงานของระบบ — พอร์ตจาก src/modules/log/log.service.ts
///
/// ต่างจากของเดิมสามข้อ
/// 1. ของเดิมคืน 100 แถวล่าสุดรวดเดียว ไม่มีการแบ่งหน้า พอใช้ไปสักพักหน้าจอจะช้ามาก
/// 2. ของเดิมคืนแค่รหัสผู้ใช้ ระบบใหม่ต่อชื่อผู้ใช้มาให้ด้วย ไม่งั้นหน้าจออ่านไม่รู้เรื่อง
/// 3. ของเดิมไม่กรองโครงการ ระบบใหม่จำกัดให้เห็นเฉพาะโครงการของตัวเอง
/// </summary>
public class ActivityLogService(AstarLivingDbContext db)
{
    private static readonly SortMap<ActivityLogRow> Sort = new SortMap<ActivityLogRow>("timestamp")
        .Add("id", r => r.Id)
        .Add("timestamp", r => r.Timestamp)
        .Add("action", r => r.Action)
        .Add("target_table", r => r.TargetTable)
        .Add("user_id", r => r.UserId);

    private readonly AstarLivingDbContext _db = db;

    public async Task<ApiResponse<IReadOnlyList<ActivityLogResponse>>> FindAllAsync(
        ActivityLogFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        // ต่อชื่อผู้ใช้เข้ามาตั้งแต่ในฐานข้อมูล เพื่อไม่ให้ยิงคำถามซ้ำทีละแถว
        var query =
            from log in _db.ActivityLogs.AsNoTracking()
            join user in _db.Users.AsNoTracking() on log.UserId equals user.Id into matched
            from user in matched.DefaultIfEmpty()
            select new ActivityLogRow
            {
                Id = log.Id,
                UserId = log.UserId,
                Username = user == null ? null : user.Username,
                FullName = user == null ? null : user.FullName,
                ProjectId = log.ProjectId,
                Action = log.Action,
                TargetTable = log.TargetTable,
                TargetId = log.TargetId,
                Timestamp = log.Timestamp,
            };

        if (current.Role != UserRole.SUPERADMIN)
        {
            query = query.Where(r => r.ProjectId == null || r.ProjectId == current.ProjectId);
        }

        if (filter.UserId is { } userId) query = query.Where(r => r.UserId == userId);

        query = query
            .WhereContains(filter.Action, r => r.Action)
            .WhereContains(filter.TargetTable, r => r.TargetTable);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok,
            (IReadOnlyList<ActivityLogResponse>)page.Items.Select(ToResponse).ToList(),
            page.PageInformation);
    }

    private static ActivityLogResponse ToResponse(ActivityLogRow r) => new()
    {
        Id = r.Id,
        UserId = r.UserId,
        Username = r.Username,
        FullName = r.FullName,
        ProjectId = r.ProjectId,
        Action = r.Action,
        TargetTable = r.TargetTable,
        TargetId = r.TargetId,
        Timestamp = r.Timestamp,
    };

    /// <summary>รูปร่างกลางที่ใช้ทั้งเรียงลำดับและแปลงเป็นผลลัพธ์</summary>
    public sealed class ActivityLogRow
    {
        public int Id { get; init; }
        public int UserId { get; init; }
        public string? Username { get; init; }
        public string? FullName { get; init; }
        public int? ProjectId { get; init; }
        public string Action { get; init; } = string.Empty;
        public string? TargetTable { get; init; }
        public int? TargetId { get; init; }
        public DateTime Timestamp { get; init; }
    }
}
