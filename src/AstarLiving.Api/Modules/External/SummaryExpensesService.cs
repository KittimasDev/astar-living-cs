using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Entities;
using AstarLiving.Domain.Enums;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.External;

/// <summary>ตัวกรองของรายงานสรุปค่าใช้จ่าย</summary>
public class SummaryExpensesFilterQuery : BaseFilterQuery
{
    [FromQuery(Name = "resident_id")] public int? ResidentId { get; set; }
    [FromQuery(Name = "month")] public int? Month { get; set; }
    /// <summary>ปี พ.ศ.</summary>
    [FromQuery(Name = "year")] public int? Year { get; set; }
}

/// <summary>
/// สรุปค่าใช้จ่ายค้างชำระ — พอร์ตจาก src/modules/external/summary-expenses/
///
/// นับเฉพาะงวดที่ยังเปิดใช้งานและสถานะเป็น NOT_PAY หรือ PARTIAL_PAY
/// งวดที่ชำระบางส่วนจะถูกเฉลี่ยยอดลงตามสัดส่วนที่ยังค้าง (ดู SummaryExpensesCalculator)
/// </summary>
public class SummaryExpensesService(AstarLivingDbContext db)
{
    /// <summary>ชื่อฟิลด์ที่เรียงได้ในมุมมองรายบ้าน</summary>
    private static readonly Dictionary<string, Func<SummaryExpensesHouseResponse, object?>> SortSelectors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["resident_id"] = r => r.ResidentId,
            ["house_no"] = r => r.HouseNo,
            ["full_name"] = r => r.FullName,
            ["common_fee"] = r => r.CommonFee,
            ["fine"] = r => r.Fine,
            ["collection_fee"] = r => r.CollectionFee,
            ["total"] = r => r.Total,
            ["payment_count"] = r => r.PaymentCount,
        };

    private readonly AstarLivingDbContext _db = db;

    /// <summary>GET /v1/summary_expenses — ยอดค้างแยกตามบ้าน</summary>
    public async Task<ApiResponse<SummaryExpensesResponse>> GetSummaryAsync(
        SummaryExpensesFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        var projectId = current.RequireProjectId();

        var payments = await BaseQuery(projectId, filter).ToListAsync(ct);
        var aggregate = SummaryExpensesCalculator.Compute(payments);

        var rows = aggregate.ByHouse.Select(h => new SummaryExpensesHouseResponse
        {
            ResidentId = h.ResidentId,
            HouseNo = h.HouseNo,
            FullName = h.FullName,
            CommonFee = h.CommonFee,
            Fine = h.Fine,
            CollectionFee = h.CollectionFee,
            Total = h.Total,
            PaymentCount = h.PaymentCount,
        }).ToList();

        var sorted = SortHouses(rows, filter);
        var paged = filter.NoLimit
            ? sorted
            : sorted.Skip((filter.Page - 1) * filter.Limit).Take(filter.Limit).ToList();

        var field = filter.OrderField is not null && SortSelectors.ContainsKey(filter.OrderField)
            ? filter.OrderField
            : "resident_id";
        var type = string.Equals(filter.OrderType, "DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

        var pageInfo = filter.NoLimit
            ? Pagination.BuildPageInfo(field, type, sorted.Count, paged.Count, paged.Count, 1, noLimit: true)
            : Pagination.BuildPageInfo(field, type, sorted.Count, paged.Count, filter.Limit, filter.Page);

        return ApiResponseFactory.CreateFind(HttpStatusCatalog.Ok, new SummaryExpensesResponse
        {
            ByHouse = paged,
            Total = new SummaryExpensesTotalResponse
            {
                CommonFee = aggregate.Total.CommonFee,
                Fine = aggregate.Total.Fine,
                CollectionFee = aggregate.Total.CollectionFee,
                Total = aggregate.Total.Total,
                HouseCount = aggregate.Total.HouseCount,
                PaymentCount = aggregate.Total.PaymentCount,
            },
        }, pageInfo);
    }

    /// <summary>
    /// GET /v1/summary_expenses/:resident_id — ยอดค้างของบ้านหลังเดียว แยกตามปี
    /// ปีเรียงจากใหม่ไปเก่า ส่วนเดือนภายในแต่ละปีเรียงจากน้อยไปมาก
    /// </summary>
    public async Task<ApiResponse<SummaryExpensesByResidentResponse?>> GetByResidentAsync(
        int residentId, SummaryExpensesFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        var projectId = current.RequireProjectId();

        var resident = await _db.Residents.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == residentId && r.ProjectId == projectId, ct)
            ?? throw AppException.NotFound("ไม่พบลูกบ้านในโครงการ");

        var scoped = new SummaryExpensesFilterQuery
        {
            ResidentId = residentId,
            Month = filter.Month,
            Year = filter.Year,
        };

        var payments = await BaseQuery(projectId, scoped).ToListAsync(ct);

        var byYear = payments
            .GroupBy(p => p.Year)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var months = g
                    .OrderBy(p => p.Month)
                    .Select(p =>
                    {
                        var amounts = SummaryExpensesCalculator.RowAmounts(p);
                        return new SummaryExpensesMonthRow
                        {
                            PaymentId = p.Id,
                            Month = p.Month,
                            Year = p.Year,
                            InvNo = p.InvNo ?? string.Empty,
                            CommonFee = amounts.CommonFee,
                            Fine = amounts.Fine,
                            CollectionFee = amounts.CollectionFee,
                            Total = amounts.Total,
                            Status = p.Status,
                        };
                    })
                    .ToList();

                return new SummaryExpensesYearGroup
                {
                    Year = g.Key,
                    MonthCount = months.Count,
                    CommonFee = months.Sum(m => m.CommonFee),
                    Fine = months.Sum(m => m.Fine),
                    CollectionFee = months.Sum(m => m.CollectionFee),
                    Total = months.Sum(m => m.Total),
                    Months = months,
                };
            })
            .ToList();

        return ApiResponseFactory.Create<SummaryExpensesByResidentResponse?>(HttpStatusCatalog.Ok,
            new SummaryExpensesByResidentResponse
            {
                ResidentId = resident.Id,
                HouseNo = resident.HouseNo,
                FullName = resident.FullName,
                ByYear = byYear,
                Total = new SummaryExpensesResidentTotal
                {
                    CommonFee = byYear.Sum(y => y.CommonFee),
                    Fine = byYear.Sum(y => y.Fine),
                    CollectionFee = byYear.Sum(y => y.CollectionFee),
                    Total = byYear.Sum(y => y.Total),
                    PaymentCount = byYear.Sum(y => y.MonthCount),
                    YearCount = byYear.Count,
                },
            });
    }

    /// <summary>เงื่อนไขพื้นฐานที่ทุกเส้นทางของรายงานนี้ใช้ร่วมกัน</summary>
    private IQueryable<Payment> BaseQuery(int projectId, SummaryExpensesFilterQuery filter)
    {
        var query = _db.Payments.AsNoTracking()
            .Include(p => p.Resident)
            .Where(p => p.Active
                        && p.Resident.ProjectId == projectId
                        && (p.Status == PaymentStatus.NOT_PAY || p.Status == PaymentStatus.PARTIAL_PAY));

        if (filter.ResidentId is { } residentId) query = query.Where(p => p.ResidentId == residentId);
        if (filter.Month is { } month) query = query.Where(p => p.Month == month);
        if (filter.Year is { } year) query = query.Where(p => p.Year == year);

        return query;
    }

    private static List<SummaryExpensesHouseResponse> SortHouses(
        List<SummaryExpensesHouseResponse> rows, SummaryExpensesFilterQuery filter)
    {
        var field = filter.OrderField is not null && SortSelectors.ContainsKey(filter.OrderField)
            ? filter.OrderField
            : "resident_id";

        // ค่าเริ่มต้นของรายงานนี้คือเรียงจากน้อยไปมาก ต่างจากรายการอื่นในระบบ
        var descending = string.Equals(filter.OrderType, "DESC", StringComparison.OrdinalIgnoreCase);
        var selector = SortSelectors[field];

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

        return descending
            ? rows.OrderByDescending(selector, comparer).ToList()
            : rows.OrderBy(selector, comparer).ToList();
    }
}
