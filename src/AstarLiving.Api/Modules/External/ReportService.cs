using AstarLiving.Api.Common;
using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Enums;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Infrastructure.Queries;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.External;

/// <summary>ตัวกรองของรายงาน — ตรงกับ ReportDto ของเดิม</summary>
public class ReportFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "type")] public ReportType? Type { get; set; }
    [FromQuery(Name = "resident_id")] public int? ResidentId { get; set; }
    [FromQuery(Name = "house_no")] public string? HouseNo { get; set; }
    [FromQuery(Name = "receipt_no")] public string? ReceiptNo { get; set; }
    [FromQuery(Name = "payment_method_name")] public string? PaymentMethodName { get; set; }
    [FromQuery(Name = "payment_method_id")] public int? PaymentMethodId { get; set; }
    [FromQuery(Name = "month")] public int? Month { get; set; }
    /// <summary>ปี พ.ศ.</summary>
    [FromQuery(Name = "year")] public int? Year { get; set; }
    [FromQuery(Name = "payment_start_date")] public DateOnly? PaymentStartDate { get; set; }
    [FromQuery(Name = "payment_end_date")] public DateOnly? PaymentEndDate { get; set; }
    [FromQuery(Name = "user_id")] public int? UserId { get; set; }
    [FromQuery(Name = "user_full_name")] public string? UserFullName { get; set; }
    [FromQuery(Name = "year_start")] public int? YearStart { get; set; }
    [FromQuery(Name = "year_end")] public int? YearEnd { get; set; }
}

/// <summary>
/// รายงานทั้ง 5 ชนิด — พอร์ตจาก src/modules/external/report/report.service.ts
///
/// ระบบเดิมบางชนิดเรียงและตัดหน้าในหน่วยความจำ เพราะแถวถูกประกอบขึ้นหลังดึงข้อมูล
/// (เช่นการจัดกลุ่มของรายงานใบแจ้งค่าใช้จ่าย) ตรงนั้นคงวิธีเดิมไว้เพื่อให้ผลตรงกัน
/// </summary>
public class ReportService(AstarLivingDbContext db)
{
    private readonly AstarLivingDbContext _db = db;

    public async Task<ApiResponse<IReadOnlyList<object>>> GetPaymentReportAsync(
        int projectId, ReportFilterQuery filter, CancellationToken ct)
    {
        return filter.Type switch
        {
            ReportType.REPORT_OUTSTANDING => await OutstandingAsync(projectId, filter, ct),
            ReportType.REPORT_PAYMENT => await PaymentAsync(projectId, filter, ct),
            ReportType.REPORT_INVOICE => await InvoiceAsync(projectId, filter, ct),
            ReportType.REPORT_FINE => await FineAsync(projectId, filter, ct),
            ReportType.REPORT_REVENUE => await RevenueAsync(projectId, filter, ct),

            // ระบบเดิมคืน undefined เมื่อไม่ระบุชนิด ซึ่งกลายเป็น body ว่าง
            _ => throw AppException.BadRequest("กรุณาระบุชนิดของรายงาน"),
        };
    }

    /// <summary>รายงานค้างชำระ — กรองและตัดหน้าที่ฐานข้อมูล</summary>
    private async Task<ApiResponse<IReadOnlyList<object>>> OutstandingAsync(
        int projectId, ReportFilterQuery filter, CancellationToken ct)
    {
        var sort = new SortMap<Domain.Entities.Payment>("id")
            .Add("id", p => p.Id)
            .Add("year", p => p.Year)
            .Add("month", p => p.Month)
            .Add("amount_due", p => p.AmountDue)
            .Add("total_due", p => p.TotalDue)
            .Add("house_no", p => p.Resident.HouseNo)
            .Add("full_name", p => p.Resident.FullName);

        var query = _db.Payments.AsNoTracking()
            .Include(p => p.Resident)
            .Where(p => p.Resident.ProjectId == projectId && p.Status == PaymentStatus.NOT_PAY);

        if (filter.ResidentId is { } residentId) query = query.Where(p => p.ResidentId == residentId);
        if (filter.Year is { } year) query = query.Where(p => p.Year == year);
        if (filter.Month is { } month) query = query.Where(p => p.Month == month);

        // ระบบเดิมเทียบบ้านเลขที่แบบตรงตัวในรายงานนี้ ไม่ใช่ ILIKE
        if (filter.HouseNo is { Length: > 0 } houseNo)
        {
            query = query.Where(p => p.Resident.HouseNo == houseNo);
        }

        var page = await query.ToPagedListAsync(sort, filter.ToFilter(), ct);

        var rows = page.Items.Select(p => (object)new OutstandingReportRow
        {
            Id = p.Id,
            ResidentId = p.ResidentId,
            InvNo = p.InvNo,
            Year = p.Year,
            Month = p.Month,
            MonthTh = ThaiCalendar.FormatMonthThai(p.Month, p.Year),
            AmountDue = p.AmountDue,
            Fine = p.Fine,
            CollectionFee = p.CollectionFee,
            TotalDue = p.TotalDue,
            Status = p.Status,
            HouseNo = p.Resident.HouseNo,
            FullName = p.Resident.FullName,
            Address = p.Resident.Address,
            CreatedAt = p.CreatedAt,
        }).ToList();

        return ApiResponseFactory.CreateFind(HttpStatusCatalog.Ok, (IReadOnlyList<object>)rows, page.PageInformation);
    }

    /// <summary>รายงานการชำระเงิน — แถวมาจากใบเสร็จ เรียงและตัดหน้าในหน่วยความจำตามของเดิม</summary>
    private async Task<ApiResponse<IReadOnlyList<object>>> PaymentAsync(
        int projectId, ReportFilterQuery filter, CancellationToken ct)
    {
        var query = _db.Receipts.AsNoTracking()
            .Include(r => r.Resident)
            .Include(r => r.PaymentMethod)
            .Include(r => r.CreatedUser)
            .Where(r => r.ProjectId == projectId)
            .WhereContains(filter.HouseNo, r => r.Resident.HouseNo)
            .WhereContains(filter.ReceiptNo, r => r.ReceiptNo);

        if (filter.PaymentMethodId is { } methodId) query = query.Where(r => r.PaymentMethodId == methodId);
        if (filter.ResidentId is { } residentId) query = query.Where(r => r.ResidentId == residentId);
        if (filter.Active is { } active) query = query.Where(r => r.Active == active);
        if (filter.PaymentStartDate is { } start) query = query.Where(r => r.IssueDate >= start);
        if (filter.PaymentEndDate is { } end) query = query.Where(r => r.IssueDate <= end);

        if (filter.PaymentMethodName is { Length: > 0 } methodName)
        {
            var pattern = $"%{methodName.Trim()}%";
            query = query.Where(r => r.PaymentMethod != null
                                     && EF.Functions.ILike(r.PaymentMethod.Name, pattern));
        }

        var receipts = await query.ToListAsync(ct);

        var rows = receipts.Select(r => new PaymentReportRow
        {
            Id = r.Id,
            ResidentId = r.ResidentId,
            HouseNo = r.Resident?.HouseNo ?? string.Empty,
            FullName = r.Resident?.FullName ?? string.Empty,
            Address = r.Resident?.Address ?? string.Empty,
            Active = r.Active,
            Status = r.Active,
            Note = r.Note,
            CreatedAt = r.CreatedAt,
            CreatedBy = r.CreatedBy,
            CreatedByName = r.CreatedUser?.FullName,
            RejectedReason = r.RejectedReason ?? string.Empty,
            ReceiptNo = r.ReceiptNo ?? string.Empty,
            IssueDate = r.IssueDate,
            TotalAmount = r.TotalAmount,
            PaymentDate = r.PaymentDate,
            PaymentMethodCode = r.PaymentMethod?.Code ?? string.Empty,
            PaymentMethodName = r.PaymentMethod?.Name ?? string.Empty,
            UserFullName = r.CreatedUser?.FullName ?? string.Empty,
        }).ToList();

        var sorted = SortInMemory(rows, filter, PaymentSortSelectors, "id");

        return Paginate(sorted, filter);
    }

    /// <summary>รายงานใบแจ้งค่าใช้จ่าย — จัดกลุ่มตาม (ผู้ออกใบ, ปี, เดือน)</summary>
    private async Task<ApiResponse<IReadOnlyList<object>>> InvoiceAsync(
        int projectId, ReportFilterQuery filter, CancellationToken ct)
    {
        var query = _db.Payments.AsNoTracking()
            .Include(p => p.CreatedUser)
            .Where(p => p.Resident.ProjectId == projectId);

        if (filter.UserId is { } userId) query = query.Where(p => p.CreatedBy == userId);
        if (filter.ResidentId is { } residentId) query = query.Where(p => p.ResidentId == residentId);
        if (filter.YearStart is { } yearStart) query = query.Where(p => p.Year >= yearStart);
        if (filter.YearEnd is { } yearEnd) query = query.Where(p => p.Year <= yearEnd);

        if (filter.UserFullName is { Length: > 0 } fullName)
        {
            var pattern = $"%{fullName.Trim()}%";
            query = query.Where(p => p.CreatedUser != null
                                     && EF.Functions.ILike(p.CreatedUser.FullName, pattern));
        }

        var payments = await query.ToListAsync(ct);

        var running = 1;
        var rows = payments
            .GroupBy(p => new { p.CreatedBy, p.Year, p.Month })
            .Select(g =>
            {
                decimal SumOf(Func<Domain.Entities.Payment, bool>? condition = null) =>
                    g.Where(p => condition is null || condition(p))
                     .Sum(p => p.AmountDue + (p.Fine ?? 0m) + (p.CollectionFee ?? 0m));

                return new InvoiceReportRow
                {
                    Id = running++,
                    MonthTh = ThaiCalendar.FormatMonthThai(g.Key.Month, g.Key.Year),
                    CountPm = g.Count(),
                    CreatedUser = g.First().CreatedUser?.FullName ?? string.Empty,
                    Total = SumOf(),
                    TotalPaid = SumOf(p => p.Status == PaymentStatus.PAID),
                    TotalNotPaid = SumOf(p => p.Status == PaymentStatus.NOT_PAY),
                };
            })
            .ToList();

        var sorted = SortInMemory(rows, filter, InvoiceSortSelectors, "id");

        return Paginate(sorted, filter);
    }

    /// <summary>
    /// รายงานค่าปรับและค่าทวงถาม
    ///
    /// รวมงวดที่มีค่าปรับหรือค่าทวงถามไม่เป็นศูนย์ บวกกับงวดที่ใบทวงถามถูกยกเลิกไปหมดแล้ว
    /// (กลุ่มหลังคือรายการที่ถูกลดหนี้ ซึ่งยอดกลายเป็นศูนย์แต่ยังต้องแสดงในรายงาน)
    /// </summary>
    private async Task<ApiResponse<IReadOnlyList<object>>> FineAsync(
        int projectId, ReportFilterQuery filter, CancellationToken ct)
    {
        // งวดที่ใบทวงถามทุกใบถูกยกเลิกแล้ว
        var cancelledOnlyPaymentIds = await _db.CollectionNotices.AsNoTracking()
            .Where(n => n.Resident.ProjectId == projectId && n.PaymentId != null)
            .GroupBy(n => n.PaymentId!.Value)
            .Where(g => g.Count() > 0 && g.All(n => n.Status == NoticeStatus.CANCELLED))
            .Select(g => g.Key)
            .ToListAsync(ct);

        var query = _db.Payments.AsNoTracking()
            .Include(p => p.Resident)
            .Include(p => p.CreatedUser)
            .Where(p => p.Resident.ProjectId == projectId);

        if (filter.HouseNo is { Length: > 0 } houseNo) query = query.Where(p => p.Resident.HouseNo == houseNo);
        if (filter.Year is { } year) query = query.Where(p => p.Year == year);
        if (filter.Month is { } month) query = query.Where(p => p.Month == month);
        if (filter.ResidentId is { } residentId) query = query.Where(p => p.ResidentId == residentId);

        query = query.Where(p => (p.Fine != null && p.Fine != 0m)
                                 || (p.CollectionFee != null && p.CollectionFee != 0m)
                                 || cancelledOnlyPaymentIds.Contains(p.Id));

        var payments = await query.ToListAsync(ct);

        var paymentIds = payments.Select(p => p.Id).Distinct().ToList();
        var notices = paymentIds.Count == 0
            ? []
            : await _db.CollectionNotices.AsNoTracking()
                .Where(n => n.PaymentId != null && paymentIds.Contains(n.PaymentId.Value))
                .Select(n => new { n.Id, PaymentId = n.PaymentId!.Value, n.NoticesType, n.Status, n.Note })
                .ToListAsync(ct);

        var noticesByPayment = notices.GroupBy(n => n.PaymentId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = payments.Select(p =>
        {
            var feesAreZero = (p.Fine ?? 0m) == 0m && (p.CollectionFee ?? 0m) == 0m;
            var candidates = noticesByPayment.GetValueOrDefault(p.Id, []);

            // เมื่อยอดเป็นศูนย์ทั้งคู่ ให้ดูเฉพาะใบที่ถูกยกเลิก เพราะแถวนี้มาจากการลดหนี้
            if (feesAreZero)
            {
                candidates = candidates.Where(n => n.Status == NoticeStatus.CANCELLED).ToList();
            }

            // ลำดับการเลือกตัวแทน: ใบที่งวดอ้างถึง -> COLLECTION -> FINE -> ใบแรก
            var picked = candidates.FirstOrDefault(n => n.Id == p.CollectionNoticeId)
                         ?? candidates.FirstOrDefault(n => n.NoticesType == NoticeType.COLLECTION)
                         ?? candidates.FirstOrDefault(n => n.NoticesType == NoticeType.FINE)
                         ?? candidates.FirstOrDefault();

            return new FineReportRow
            {
                Id = p.Id,
                Month = p.Month,
                Year = p.Year,
                MonthTh = ThaiCalendar.FormatMonthThai(p.Month, p.Year),
                ResidentHouseNo = p.Resident?.HouseNo ?? string.Empty,
                ResidentFullName = p.Resident?.FullName ?? string.Empty,
                ResidentAddress = p.Resident?.Address ?? string.Empty,
                Fine = p.Fine,
                CollectionFee = p.CollectionFee,
                TotalDue = p.AmountDue + (p.Fine ?? 0m) + (p.CollectionFee ?? 0m),
                StatusPayments = p.Status,
                StatusNotices = picked?.Status,
                Note = picked?.Note,
                CreatedAt = p.CreatedAt,
                CreatedBy = p.CreatedBy,
                CreatedByName = p.CreatedUser?.FullName,
            };
        }).ToList();

        var sorted = SortInMemory(rows, filter, FineSortSelectors, "id");

        return Paginate(sorted, filter);
    }

    /// <summary>รายงานรายรับอื่น ๆ — กรองและตัดหน้าที่ฐานข้อมูล</summary>
    private async Task<ApiResponse<IReadOnlyList<object>>> RevenueAsync(
        int projectId, ReportFilterQuery filter, CancellationToken ct)
    {
        var sort = new SortMap<Domain.Entities.RevenueTransaction>("id")
            .Add("id", t => t.Id)
            .Add("receipt_no", t => t.ReceiptNo)
            .Add("issue_date", t => t.IssueDate)
            .Add("total_amount", t => t.TotalAmount)
            .Add("house_no", t => t.Resident.HouseNo);

        var query = _db.RevenueTransactions.AsNoTracking()
            .Include(t => t.Resident)
            .Where(t => t.ProjectId == projectId)
            .WhereContains(filter.HouseNo, t => t.Resident.HouseNo)
            .WhereContains(filter.ReceiptNo, t => t.ReceiptNo);

        if (filter.PaymentMethodId is { } methodId) query = query.Where(t => t.PaymentMethodId == methodId);
        if (filter.ResidentId is { } residentId) query = query.Where(t => t.ResidentId == residentId);
        if (filter.Active is { } active) query = query.Where(t => t.Active == active);
        if (filter.PaymentStartDate is { } start) query = query.Where(t => t.IssueDate >= start);
        if (filter.PaymentEndDate is { } end) query = query.Where(t => t.IssueDate <= end);

        var page = await query.ToPagedListAsync(sort, filter.ToFilter(), ct);

        var rows = page.Items.Select(t => (object)new
        {
            t.Id,
            t.ProjectId,
            t.ReceiptNo,
            t.IssueDate,
            t.ResidentId,
            t.PaymentDetail,

            // คอลัมน์ "รายละเอียด" ของหน้ารายงาน ระบบเดิมประกอบที่เบราว์เซอร์
            // ย้ายมาทำที่นี่เพื่อให้หน้าจอกับไฟล์ Excel ได้ข้อความชุดเดียวกัน
            detail = RevenueDetailText.Build(t.PaymentDetail),
            t.TotalAmount,
            t.PaymentMethodId,
            t.BankName,
            t.ChequeNo,
            t.Note,
            t.PaymentDate,
            t.Active,
            t.RejectedReason,
            house_no = t.Resident?.HouseNo ?? string.Empty,
            full_name = t.Resident?.FullName ?? string.Empty,
        }).ToList();

        return ApiResponseFactory.CreateFind(HttpStatusCatalog.Ok, (IReadOnlyList<object>)rows, page.PageInformation);
    }

    // ─── การเรียงและตัดหน้าในหน่วยความจำ ─────────────────────────────────────

    private static readonly Dictionary<string, Func<PaymentReportRow, object?>> PaymentSortSelectors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = r => r.Id,
            ["resident_id"] = r => r.ResidentId,
            ["house_no"] = r => r.HouseNo,
            ["full_name"] = r => r.FullName,
            ["receipt_no"] = r => r.ReceiptNo,
            ["issue_date"] = r => r.IssueDate,
            ["total_amount"] = r => r.TotalAmount,
            ["payment_date"] = r => r.PaymentDate,
            ["payment_method_name"] = r => r.PaymentMethodName,
            ["created_at"] = r => r.CreatedAt,
            ["created_by_name"] = r => r.CreatedByName,
        };

    private static readonly Dictionary<string, Func<InvoiceReportRow, object?>> InvoiceSortSelectors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = r => r.Id,
            ["countPm"] = r => r.CountPm,
            ["createdUser"] = r => r.CreatedUser,
            ["total"] = r => r.Total,
            ["totalPaid"] = r => r.TotalPaid,
            ["totalNotPaid"] = r => r.TotalNotPaid,
            ["monthTH"] = r => r.MonthTh,
        };

    private static readonly Dictionary<string, Func<FineReportRow, object?>> FineSortSelectors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = r => r.Id,
            ["month"] = r => r.Month,
            ["year"] = r => r.Year,
            ["fine"] = r => r.Fine,
            ["collection_fee"] = r => r.CollectionFee,
            ["monthTH"] = r => r.MonthTh,
            ["resident_house_no"] = r => r.ResidentHouseNo,
            ["resident_full_name"] = r => r.ResidentFullName,
            ["total_due"] = r => r.TotalDue,
            ["status_payments"] = r => r.StatusPayments,
            ["status_notices"] = r => r.StatusNotices,
            ["note"] = r => r.Note,
            ["created_at"] = r => r.CreatedAt,
            ["created_by"] = r => r.CreatedBy,
            ["created_by_name"] = r => r.CreatedByName,
        };

    /// <summary>
    /// sortReportData() — เรียงในหน่วยความจำ
    /// ข้อความใช้การเทียบแบบไทย ส่วนค่าว่างถูกจัดไว้ก่อนเมื่อเรียงจากน้อยไปมาก
    /// </summary>
    private static List<T> SortInMemory<T>(
        List<T> rows, ReportFilterQuery filter,
        Dictionary<string, Func<T, object?>> selectors, string defaultField)
    {
        var field = filter.OrderField is not null && selectors.ContainsKey(filter.OrderField)
            ? filter.OrderField
            : defaultField;

        var ascending = string.Equals(filter.OrderType, "ASC", StringComparison.OrdinalIgnoreCase);
        var selector = selectors[field];

        var comparer = Comparer<object?>.Create((a, b) =>
        {
            if (a is null && b is null) return 0;
            if (a is null) return -1;
            if (b is null) return 1;

            if (a is string sa && b is string sb)
            {
                return string.Compare(sa, sb, System.Globalization.CultureInfo.GetCultureInfo("th-TH"),
                    System.Globalization.CompareOptions.None);
            }

            return Comparer<object>.Default.Compare(a, b);
        });

        return ascending
            ? rows.OrderBy(selector, comparer).ToList()
            : rows.OrderByDescending(selector, comparer).ToList();
    }

    private static ApiResponse<IReadOnlyList<object>> Paginate<T>(List<T> rows, ReportFilterQuery filter)
    {
        var field = filter.OrderField ?? "id";
        var type = string.Equals(filter.OrderType, "ASC", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

        if (filter.NoLimit)
        {
            var all = rows.Cast<object>().ToList();
            return ApiResponseFactory.CreateFind(
                HttpStatusCatalog.Ok, (IReadOnlyList<object>)all,
                Pagination.BuildPageInfo(field, type, rows.Count, all.Count, all.Count, 1, noLimit: true));
        }

        var skip = (filter.Page - 1) * filter.Limit;
        var page = rows.Skip(skip).Take(filter.Limit).Cast<object>().ToList();

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok, (IReadOnlyList<object>)page,
            Pagination.BuildPageInfo(field, type, rows.Count, page.Count, filter.Limit, filter.Page));
    }
}
