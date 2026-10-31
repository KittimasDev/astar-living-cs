using AstarLiving.Api.Auth;
using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Enums;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.External;

/// <summary>ตัวกรองของหน้าแดชบอร์ด — ตรงกับ GetDashboardDto ของเดิม</summary>
public class DashboardFilterQuery
{
    [FromQuery(Name = "project_id")]
    public int? ProjectId { get; set; }

    /// <summary>ปี พ.ศ.</summary>
    [FromQuery(Name = "year")]
    public int? Year { get; set; }

    /// <summary>
    /// ระบบเดิมใช้เฉพาะ "ส่วนเดือน" ของวันที่ที่ส่งมา ไม่ได้ใช้ทั้งวัน
    /// (ตัดสตริงด้วย '-' แล้วหยิบชิ้นที่สอง) จึงเป็นการกรองช่วงเดือน ไม่ใช่ช่วงวันที่จริง
    /// </summary>
    [FromQuery(Name = "start_date")]
    public string? StartDate { get; set; }

    [FromQuery(Name = "end_date")]
    public string? EndDate { get; set; }

    /// <summary>คืนเลขเดือนจากสตริงวันที่ เช่น "2026-05-01" ได้ 5</summary>
    public int? StartMonth => ExtractMonth(StartDate);

    public int? EndMonth => ExtractMonth(EndDate);

    private static int? ExtractMonth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var parts = value.Split('-');
        return parts.Length >= 2 && int.TryParse(parts[1], out var month) ? month : null;
    }
}

/// <summary>พอร์ตจาก src/modules/external/dashboard/dashboard.service.ts</summary>
public class DashboardService(AstarLivingDbContext db)
{
    private readonly AstarLivingDbContext _db = db;

    public async Task<ApiResponse<DashboardResponse>> GetDashboardAsync(
        DashboardFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        // ผู้ใช้ระดับโครงการดูได้เฉพาะโครงการตัวเอง ส่วน Astar เลือกโครงการได้
        var projectId = current.IsSuperAdmin
            ? filter.ProjectId
            : current.ProjectId ?? filter.ProjectId;

        var totalResident = await _db.Residents.AsNoTracking()
            .CountAsync(r => r.Active && (projectId == null || r.ProjectId == projectId), ct);

        var paymentQuery = _db.Payments.AsNoTracking()
            .Include(p => p.Resident)
            .Include(p => p.Receipt)
            .Where(p => projectId == null || p.Resident.ProjectId == projectId);

        if (filter.Year is { } year) paymentQuery = paymentQuery.Where(p => p.Year == year);

        // ระบบเดิมใส่ช่วงเดือนลง BETWEEN เสมอ แม้ค่าจะเป็น null ซึ่งทำให้ไม่เจอแถวใดเลย
        // ระบบใหม่ถือว่า "ไม่ระบุ" คือไม่กรอง ซึ่งเป็นสิ่งที่หน้าจอคาดหวัง
        if (filter.StartMonth is { } startMonth) paymentQuery = paymentQuery.Where(p => p.Month >= startMonth);
        if (filter.EndMonth is { } endMonth) paymentQuery = paymentQuery.Where(p => p.Month <= endMonth);

        var payments = await paymentQuery.ToListAsync(ct);

        var noticeQuery = _db.CollectionNotices.AsNoTracking()
            .Where(n => n.ReceiptId != null
                        && (projectId == null || n.Resident.ProjectId == projectId));

        if (filter.Year is { } noticeYear) noticeQuery = noticeQuery.Where(n => n.Year == noticeYear);
        if (filter.StartMonth is { } ns) noticeQuery = noticeQuery.Where(n => n.Month >= ns);
        if (filter.EndMonth is { } ne) noticeQuery = noticeQuery.Where(n => n.Month <= ne);

        var noticeReceiptIds = await noticeQuery.Select(n => n.ReceiptId!.Value).ToListAsync(ct);

        // กราฟวิธีชำระเงินนับ "จำนวนใบเสร็จ" ที่ผูกกับงวดหรือใบทวงถามในช่วงที่เลือก
        var receiptIds = payments
            .Where(p => p.ReceiptsId is not null)
            .Select(p => p.ReceiptsId!.Value)
            .Concat(noticeReceiptIds)
            .Distinct()
            .ToList();

        var receipts = await _db.Receipts.AsNoTracking()
            .Include(r => r.PaymentMethod)
            .Where(r => receiptIds.Contains(r.Id)
                        && (projectId == null || r.Resident.ProjectId == projectId))
            .Select(r => new { r.PaymentMethodId, MethodName = r.PaymentMethod!.Name, MethodCode = r.PaymentMethod.Code })
            .ToListAsync(ct);

        var dashboardPayments = payments.Select(p => new DashboardPayment(
            p.ResidentId,
            p.Resident?.HouseNo ?? string.Empty,
            p.Resident?.FullName ?? string.Empty,
            p.Month,
            p.AmountDue,
            p.Fine ?? 0m,
            p.CollectionFee ?? 0m,
            p.Status)).ToList();

        var unpaid = dashboardPayments.Where(p => p.Status == PaymentStatus.NOT_PAY).ToList();

        var summary = DashboardCalculator.ComputeSummary(dashboardPayments, totalResident);
        var trend = DashboardCalculator.ComputeMonthlyTrend(dashboardPayments);
        var outstandings = DashboardCalculator.ComputeOutstandings(unpaid);

        var paymentMethod = receipts
            .Where(r => r.PaymentMethodId is not null)
            .GroupBy(r => r.PaymentMethodId!.Value)
            .Select(g => new DashboardPointResponse
            {
                Label = g.First().MethodName,
                Key = g.First().MethodCode,
                Value = g.Count(),
            })
            .ToList();

        return ApiResponseFactory.Ok(new DashboardResponse
        {
            Summary = new DashboardSummaryResponse
            {
                TotalResident = summary.TotalResident,
                TotalBilled = summary.TotalBilled,
                TotalPaid = summary.TotalPaid,
                TotalUnpaid = summary.TotalUnpaid,
                PaymentRate = summary.PaymentRate,
            },
            MonthlyTrend = trend.Select(t => new DashboardPointResponse
            {
                Label = t.Label,
                Key = t.Key,
                Value = t.Value,
            }).ToList(),
            PaymentMethod = paymentMethod,
            Outstandings = outstandings.Select(o => new DashboardOutstandingResponse
            {
                ResidentId = o.ResidentId,
                HouseNo = o.HouseNo,
                FullName = o.FullName,
                Count = o.Count,
                AmountDue = o.AmountDue,
                Fine = o.Fine,
                CollectionFee = o.CollectionFee,
                TotalDue = o.TotalDue,
                Status = o.Status,
            }).ToList(),
        });
    }
}
