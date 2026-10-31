using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Entities;
using AstarLiving.Domain.Enums;
using AstarLiving.Infrastructure.Auditing;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Infrastructure.Queries;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.Internal;

/// <summary>
/// เส้นทางอ่านข้อมูลและแก้ไขรายตัวของ gv_payments
/// พอร์ตจาก src/modules/internal/payment/payment.service.ts
/// ส่วนการออกใบแจ้งหนี้อยู่ใน InvoiceIssuingService
/// </summary>
public class PaymentService(
    AstarLivingDbContext db,
    PaymentQueries queries,
    AuditStamp audit,
    ThaiCalendar calendar)
{
    private static readonly SortMap<Payment> Sort = new SortMap<Payment>("id")
        .Add("id", p => p.Id)
        .Add("inv_no", p => p.InvNo)
        .Add("year", p => p.Year)
        .Add("month", p => p.Month)
        .Add("amount_due", p => p.AmountDue)
        .Add("total_due", p => p.TotalDue)
        .Add("status", p => p.Status)
        .Add("payment_date", p => p.PaymentDate)
        .Add("house_no", p => p.Resident.HouseNo)
        .Add("full_name", p => p.Resident.FullName)
        .Add("created_at", p => p.CreatedAt)
        .Add("updated_at", p => p.UpdatedAt);

    private readonly AstarLivingDbContext _db = db;
    private readonly PaymentQueries _queries = queries;
    private readonly AuditStamp _audit = audit;
    private readonly ThaiCalendar _calendar = calendar;

    /// <summary>GET /v1/projects/:project_id/payments</summary>
    public async Task<ApiResponse<IReadOnlyList<PaymentResponse>>> FindAllByProjectAsync(
        int projectId, PaymentFilterQuery filter, CancellationToken ct)
    {
        var query = _db.Payments.AsNoTracking()
            .Include(p => p.Resident)
            .Where(p => p.Resident.ProjectId == projectId);

        query = ApplyFilter(query, filter);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        var items = await RecomputeTotalsAsync(projectId, page.Items, ct);

        return ApiResponseFactory.CreateFind(HttpStatusCatalog.Ok, items, page.PageInformation);
    }

    /// <summary>
    /// GET /v1/payments/outstanding
    /// ระบบเดิมไม่สนตัวกรองใด ๆ เลยในเส้นทางนี้ และคืนทุกงวดที่สถานะ NOT_PAY ทั้งฐานข้อมูล
    /// ระบบใหม่จำกัดตามโครงการของผู้ใช้เป็นอย่างน้อย
    /// </summary>
    public async Task<ApiResponse<IReadOnlyList<PaymentResponse>>> GetOutstandingAsync(
        CurrentUser current, CancellationToken ct)
    {
        var query = _db.Payments.AsNoTracking()
            .Include(p => p.Resident)
            .Where(p => p.Status == PaymentStatus.NOT_PAY);

        if (!current.IsSuperAdmin && current.ProjectId is { } projectId)
        {
            query = query.Where(p => p.Resident.ProjectId == projectId);
        }

        var rows = await query.OrderBy(p => p.Id).ToListAsync(ct);

        return ApiResponseFactory.Ok((IReadOnlyList<PaymentResponse>)rows.Select(ToResponse).ToList());
    }

    /// <summary>
    /// GET /v1/projects/:project_id/payments/residents
    /// type = RECEIPT  : บ้านที่ยังมีงวดค้าง (ใช้ในหน้าออกใบเสร็จ)
    /// นอกนั้น          : บ้านที่ไม่มีงวดค้างแบบปกติเหลืออยู่
    /// </summary>
    public async Task<ApiResponse<IReadOnlyList<ResidentBrief>>> GetResidentsAsync(
        int projectId, PaymentFilterQuery filter, CancellationToken ct)
    {
        var unpaid = _db.Payments.AsNoTracking()
            .Where(p => p.Resident.ProjectId == projectId
                        && !p.Advance
                        && p.Status != PaymentStatus.PAID
                        && p.Status != PaymentStatus.CANCELLED)
            .Select(p => p.ResidentId);

        var residents = _db.Residents.AsNoTracking().Where(r => r.ProjectId == projectId);

        residents = filter.Type == PaymentFilterType.RECEIPT
            ? residents.Where(r => unpaid.Contains(r.Id))
            : residents.Where(r => !unpaid.Contains(r.Id));

        residents = residents.WhereContains(filter.HouseNo, r => r.HouseNo)
                             .WhereContains(filter.FullName, r => r.FullName);

        var rows = await residents.OrderBy(r => r.Id).ToListAsync(ct);

        return ApiResponseFactory.Ok((IReadOnlyList<ResidentBrief>)rows.Select(ToBrief).ToList());
    }

    /// <summary>
    /// GET /v1/projects/:project_id/payments/reminders
    /// งวดของ "เดือนก่อนหน้างวดที่จะออก" ที่ยังไม่ชำระ พร้อมยอดค้างรวมของบ้านนั้น
    /// </summary>
    public async Task<ApiResponse<IReadOnlyList<ReminderRow>>> GetRemindersAsync(
        int projectId, PaymentFilterQuery filter, CancellationToken ct)
    {
        var period = await _queries.ResolveIssuePeriodAsync(projectId, ct);
        var previous = ThaiCalendar.PreviousMonth(period.Month, period.Year);

        var query = _db.Payments.AsNoTracking()
            .Include(p => p.Resident)
            .Where(p => p.Resident.ProjectId == projectId
                        && p.Month == previous.Month
                        && p.Year == previous.Year
                        && p.Status == PaymentStatus.NOT_PAY)
            .WhereContains(filter.FullName, p => p.Resident.FullName);

        var rows = await query.OrderBy(p => p.Id).ToListAsync(ct);

        // ยอดค้างรวมต่อบ้าน คิดจากทุกงวดที่ยังไม่ชำระของบ้านนั้นในโครงการ
        var residentIds = rows.Select(r => r.ResidentId).Distinct().ToList();
        var debtTotals = await _db.Payments.AsNoTracking()
            .Where(p => p.Resident.ProjectId == projectId
                        && p.Status == PaymentStatus.NOT_PAY
                        && residentIds.Contains(p.ResidentId))
            .GroupBy(p => p.ResidentId)
            .Select(g => new { ResidentId = g.Key, Total = g.Sum(p => p.AmountDue) })
            .ToDictionaryAsync(x => x.ResidentId, x => x.Total, ct);

        var items = rows.Select(p => new ReminderRow
        {
            Id = p.Id,
            ResidentId = p.ResidentId,
            InvNo = p.InvNo,
            Year = p.Year,
            Month = p.Month,
            AmountDue = p.AmountDue,
            FullName = p.Resident.FullName,
            HouseNo = p.Resident.HouseNo,
            Address = p.Resident.Address,
            DebtTotal = debtTotals.GetValueOrDefault(p.ResidentId),
        }).ToList();

        return ApiResponseFactory.Ok((IReadOnlyList<ReminderRow>)items);
    }

    /// <summary>GET /v1/projects/:project_id/payments/month-range</summary>
    public async Task<ApiResponse<InvoicePeriodResponse>> GetMonthRangeAsync(
        int projectId, CancellationToken ct)
    {
        var period = await _queries.ResolveIssuePeriodAsync(projectId, ct);

        if (!period.IsPayment) throw AppException.BadRequest(period.DuplicateIssueMessage);

        return ApiResponseFactory.Ok(ToResponse(period));
    }

    public async Task<ApiResponse<PaymentResponse?>> FindOneAsync(int id, CancellationToken ct)
    {
        var payment = await _db.Payments.AsNoTracking()
            .Include(p => p.Resident)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return payment is null
            ? ApiResponseFactory.NotFound<PaymentResponse>()
            : ApiResponseFactory.Create<PaymentResponse?>(HttpStatusCatalog.Ok, ToResponse(payment));
    }

    /// <summary>
    /// GET /v1/payments/summary
    /// ระบบเดิมเขียน SUM(p.amount) ซึ่งไม่มีคอลัมน์นั้นอยู่จริง เส้นทางนี้จึงพังมาตลอด
    /// (ตารางบั๊กข้อ 4) ระบบใหม่รวมจาก total_due ซึ่งเป็นยอดที่ตั้งใจจะรวม
    /// </summary>
    public async Task<ApiResponse<IReadOnlyList<object>>> GetSummaryAsync(
        CurrentUser current, CancellationToken ct)
    {
        var query = _db.Payments.AsNoTracking().Where(p => p.PaymentDate != null);

        if (!current.IsSuperAdmin && current.ProjectId is { } projectId)
        {
            query = query.Where(p => p.Resident.ProjectId == projectId);
        }

        var rows = await query
            .GroupBy(p => new { p.Year, p.Month })
            .Select(g => new
            {
                year = g.Key.Year,
                month = g.Key.Month,
                total_amount = g.Sum(p => p.TotalDue),
            })
            .OrderByDescending(x => x.year).ThenByDescending(x => x.month)
            .ToListAsync(ct);

        return ApiResponseFactory.Ok((IReadOnlyList<object>)rows.Cast<object>().ToList());
    }

    public async Task<ApiResponse<PaymentResponse>> CreateAsync(
        CreatePaymentRequest request, CurrentUser current, CancellationToken ct)
    {
        var resident = await _db.Residents.FirstOrDefaultAsync(r => r.Id == request.ResidentId, ct)
                       ?? throw AppException.BadRequest("ไม่พบข้อมูลลูกบ้าน");

        if (!current.IsSuperAdmin && resident.ProjectId != current.ProjectId)
        {
            throw AppException.Forbidden("ไม่มีสิทธิ์เข้าถึงข้อมูลของโครงการอื่น");
        }

        var payment = new Payment
        {
            ResidentId = request.ResidentId,
            Year = request.Year,
            Month = request.Month,
            AmountDue = request.AmountDue,
            PaidAmount = request.PaidAmount,
            Fine = request.Fine,
            CollectionFee = request.CollectionFee,
            TotalDue = request.TotalDue,
            PaymentDate = request.PaymentDate ?? _audit.ThaiDocumentDate,
            PaymentMethodId = request.PaymentMethodId,
            Status = request.Status,
            Note = request.Note,
            CollectionNoticeId = request.CollectionNoticeId,
            HasCollectionNotice = request.HasCollectionNotice ?? false,
            CollectionPaid = request.CollectionPaid ?? false,
            Active = true,
            Advance = false,
        };
        _audit.StampCreate(payment, current.UserId);

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Ok(ToResponse(payment));
    }

    public async Task<ApiResponse<PaymentResponse?>> UpdateAsync(
        int id, UpdatePaymentRequest request, CurrentUser current, CancellationToken ct)
    {
        var payment = await _db.Payments.Include(p => p.Resident).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null) return ApiResponseFactory.NotFound<PaymentResponse>();

        if (request.AmountDue is not null) payment.AmountDue = request.AmountDue.Value;
        if (request.PaidAmount is not null) payment.PaidAmount = request.PaidAmount;
        if (request.Fine is not null) payment.Fine = request.Fine;
        if (request.CollectionFee is not null) payment.CollectionFee = request.CollectionFee;
        if (request.TotalDue is not null) payment.TotalDue = request.TotalDue.Value;
        if (request.PaymentDate is not null) payment.PaymentDate = request.PaymentDate;
        if (request.PaymentMethodId is not null) payment.PaymentMethodId = request.PaymentMethodId;
        if (request.Status is { } status) payment.Status = status;
        if (request.Note is not null) payment.Note = request.Note;
        if (request.Active is { } active) payment.Active = active;

        _audit.StampUpdate(payment, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Create<PaymentResponse?>(HttpStatusCatalog.Ok, ToResponse(payment));
    }

    public async Task<ApiResponse<object>> RemoveAsync(int id, CurrentUser current, CancellationToken ct)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is not null)
        {
            _audit.StampDelete(payment, current.UserId);
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponseFactory.Ok<object>(new { });
    }

    /// <summary>
    /// DELETE /v1/projects/:project_id/payments/month/:month/:year
    /// ลบทั้งงวด: ใบทวงถามที่ผูกอยู่ ใบเสร็จที่เกี่ยวข้อง แล้วจึงลบงวด — ทั้งหมดในทรานแซกชันเดียว
    /// </summary>
    public async Task<DeleteByMonthResult> DeleteByMonthAsync(
        int projectId, int month, int year, CurrentUser current, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var payments = await _db.Payments
            .Where(p => p.Resident.ProjectId == projectId && p.Month == month && p.Year == year)
            .ToListAsync(ct);

        if (payments.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return new DeleteByMonthResult { Deleted = 0 };
        }

        var paymentIds = payments.Select(p => p.Id).ToList();
        var receiptIds = payments.Where(p => p.ReceiptsId != null)
            .Select(p => p.ReceiptsId!.Value).Distinct().ToList();

        var notices = await _db.CollectionNotices
            .Where(n => n.PaymentId != null && paymentIds.Contains(n.PaymentId.Value))
            .ToListAsync(ct);
        foreach (var notice in notices) _audit.StampDelete(notice, current.UserId);

        var receipts = await _db.Receipts
            .Where(r => r.ProjectId == projectId && receiptIds.Contains(r.Id))
            .ToListAsync(ct);
        foreach (var receipt in receipts) _audit.StampDelete(receipt, current.UserId);

        foreach (var payment in payments) _audit.StampDelete(payment, current.UserId);

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new DeleteByMonthResult { Deleted = payments.Count };
    }

    private static IQueryable<Payment> ApplyFilter(IQueryable<Payment> query, PaymentFilterQuery filter)
    {
        query = query.WhereContains(filter.InvNo, p => p.InvNo)
                     .WhereContains(filter.HouseNo, p => p.Resident.HouseNo)
                     .WhereContains(filter.FullName, p => p.Resident.FullName);

        if (filter.Type == PaymentFilterType.OVERDUE)
        {
            // ยังค้าง และมียอดรวมมากกว่าศูนย์ (ตัดใบแจ้งเพื่อทราบออก)
            query = query.Where(p => p.Status != PaymentStatus.PAID
                                     && p.Status != PaymentStatus.CANCELLED
                                     && (p.AmountDue + (p.Fine ?? 0m) + (p.CollectionFee ?? 0m)) > 0m);
        }
        else if (filter.Status is { } status)
        {
            query = query.Where(p => p.Status == status);
        }

        if (filter.ResidentId is { } residentId) query = query.Where(p => p.ResidentId == residentId);
        if (filter.Month is { } month) query = query.Where(p => p.Month == month);
        if (filter.Year is { } year) query = query.Where(p => p.Year == year);
        if (filter.Advance is { } advance) query = query.Where(p => p.Advance == advance);
        if (filter.Active is { } active) query = query.Where(p => p.Active == active);

        return query;
    }

    /// <summary>
    /// คำนวณ total_due ใหม่ตอนอ่าน เหมือนระบบเดิม
    ///
    /// ระบบเดิมยิงคิวรีหาหนี้ค้างทีละแถว (N+1) ระบบใหม่ดึงหนี้ของทุกบ้านในหน้านั้นครั้งเดียว
    /// แล้วคำนวณในหน่วยความจำ ผลลัพธ์เท่ากันแต่เร็วกว่ามากเมื่อข้อมูลเยอะ
    /// </summary>
    private async Task<IReadOnlyList<PaymentResponse>> RecomputeTotalsAsync(
        int projectId, IReadOnlyList<Payment> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var residentIds = rows.Select(r => r.ResidentId).Distinct().ToList();

        var debtRows = await _db.Payments.AsNoTracking()
            .Where(p => p.Resident.ProjectId == projectId
                        && p.Status == PaymentStatus.NOT_PAY
                        && residentIds.Contains(p.ResidentId))
            .Select(p => new { p.Id, p.ResidentId, p.Year, p.Month, p.AmountDue, p.Fine, p.CollectionFee })
            .ToListAsync(ct);

        var debtsByResident = debtRows
            .GroupBy(d => d.ResidentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return rows.Select(payment =>
        {
            var debts = debtsByResident.TryGetValue(payment.ResidentId, out var list)
                ? list.Where(d => d.Id != payment.Id
                                  && (d.Year < payment.Year
                                      || (d.Year == payment.Year && d.Month < payment.Month)))
                      .Select(d => new DebtLine(d.Year, d.Month, d.AmountDue, d.Fine ?? 0m, d.CollectionFee ?? 0m))
                      .ToList()
                : [];

            var total = DebtSummary.PdfInvoiceGrandTotal(
                payment.AmountDue, payment.Fine, payment.CollectionFee,
                debts, payment.Year, payment.Month);

            return ToResponse(payment) with { TotalDue = total };
        }).ToList();
    }

    private static ResidentBrief ToBrief(Resident r) => new()
    {
        Id = r.Id,
        ProjectId = r.ProjectId,
        HouseNo = r.HouseNo,
        FullName = r.FullName,
        Address = r.Address,
    };

    public static PaymentResponse ToResponse(Payment p) => new()
    {
        Id = p.Id,
        ReceiptsId = p.ReceiptsId,
        ResidentId = p.ResidentId,
        InvNo = p.InvNo,
        Year = p.Year,
        Month = p.Month,
        AmountDue = p.AmountDue,
        PaidAmount = p.PaidAmount,
        Fine = p.Fine,
        CollectionFee = p.CollectionFee,
        TotalDue = p.TotalDue,
        PaymentDate = p.PaymentDate,
        PaymentMethodId = p.PaymentMethodId,
        Status = p.Status,
        Note = p.Note,
        Active = p.Active,
        CollectionNoticeId = p.CollectionNoticeId,
        HasCollectionNotice = p.HasCollectionNotice,
        CollectionPaid = p.CollectionPaid,
        Advance = p.Advance,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        Resident = p.Resident is null ? null : ToBrief(p.Resident),
    };

    public static InvoicePeriodResponse ToResponse(Domain.Billing.InvoicePeriodResult period) => new()
    {
        ProjectId = period.ProjectId,
        Year = period.Year,
        Month = period.Month,
        Min = period.Min,
        Max = period.Max,
        MonthTh = period.MonthTh,
        IsPayment = period.IsPayment,
        NextYear = period.NextYear,
        NextMonthTh = period.NextMonthTh,
        IsSupplement = period.IsSupplement,
    };
}
