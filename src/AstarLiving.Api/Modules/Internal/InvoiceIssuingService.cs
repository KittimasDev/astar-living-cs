using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Common;
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
/// การออกใบแจ้งหนี้ — ส่วนที่ซับซ้อนที่สุดของระบบ
/// พอร์ตจาก getInvoiceList() / createInvoices() / createAdvanceInvoice() ของเดิม
///
/// ⚠ ฐานคำนวณค่าปรับของหน้าพรีวิวกับตอนบันทึกจริงไม่เหมือนกัน และเป็นพฤติกรรมที่ตั้งใจคงไว้
/// รายละเอียดอยู่ใน InvoiceCalculator
/// </summary>
public class InvoiceIssuingService(
    AstarLivingDbContext db,
    PaymentQueries queries,
    AuditStamp audit,
    ThaiCalendar calendar)
{
    /// <summary>เดือนที่ระบบสร้างงวดล่วงหน้าให้อัตโนมัติเมื่อใช้ส่วนลดรายปี</summary>
    public static readonly int[] AdvanceMaintenanceMonths = [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

    private static readonly SortMap<Resident> ResidentSort = new SortMap<Resident>("id", defaultAscending: true)
        .Add("id", r => r.Id)
        .Add("house_no", r => r.HouseNo)
        .Add("full_name", r => r.FullName);

    private readonly AstarLivingDbContext _db = db;
    private readonly PaymentQueries _queries = queries;
    private readonly AuditStamp _audit = audit;
    private readonly ThaiCalendar _calendar = calendar;

    /// <summary>
    /// POST /v1/projects/:project_id/payments/invoices — หน้าพรีวิวก่อนออกใบจริง
    /// </summary>
    public async Task<ApiResponse<IReadOnlyList<InvoicePreviewRow>>> GetInvoiceListAsync(
        int projectId, BaseFilterQuery filter, CreateInvoicesRequest payload, CancellationToken ct)
    {
        var selected = payload.ResidentIds?.ToHashSet() ?? [];
        var context = await _queries.GetInvoicePeriodContextAsync(projectId, ct);

        var query = BuildResidentQuery(projectId, context);
        var page = await query.ToPagedListAsync(ResidentSort, filter.ToFilter(), ct);

        var rates = await LoadRatesAsync(projectId, ct);
        var period = new MonthYear(context.Period.Month, context.Period.Year);

        var items = new List<InvoicePreviewRow>(page.Items.Count);

        foreach (var resident in page.Items)
        {
            var debts = await _queries.FindPriorNotPayDebtsAsync(
                projectId, resident.Id, period.Year, period.Month, null, ct);

            var line = InvoiceCalculator.Preview(
                rates,
                resident.OwnershipRatio,
                debts,
                period,
                isSelectedResident: selected.Contains(resident.Id),
                isPrepaidPeriod: context.PrepaidResidentIds.Contains(resident.Id));

            items.Add(new InvoicePreviewRow
            {
                Id = resident.Id,
                ProjectId = resident.ProjectId,
                HouseNo = resident.HouseNo,
                FullName = resident.FullName,
                Address = resident.Address,
                OwnershipRatio = resident.OwnershipRatio,
                MaintenanceFeeRate = rates.MaintenanceFeeRate,
                Maintenance = line.Maintenance,
                FineAmount = line.FineAmount,
                FineAmountPreviewCurrentYear = line.FineAmountPreviewCurrentYear,
                CollectionAmount = line.CollectionAmount,
                DebtTotal = line.DebtTotal,
                Total = line.Total,
                TotalPreviewCurrentYear = line.TotalPreviewCurrentYear,
            });
        }

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok, (IReadOnlyList<InvoicePreviewRow>)items, page.PageInformation);
    }

    /// <summary>
    /// POST /v1/projects/:project_id/payments/invoices/create — ออกใบแจ้งหนี้จริง
    ///
    /// นอกจากสร้างแถวใน gv_payments แล้ว ยังสร้างใบทวงถามใน gv_collection_notices ตามเงื่อนไข
    /// - มีหนี้ค้างและคิดค่าปรับได้ -> ใบชนิด FINE
    /// - ไม่ใช่บ้านจ่ายล่วงหน้า ถูกติ๊กเลือก และมีค่าทวงถาม -> ใบชนิด COLLECTION
    ///   แล้วผูก collection_notice_id กลับเข้างวดนั้น
    /// </summary>
    public async Task<ApiResponse<CreateInvoicesResult>> CreateInvoicesAsync(
        int projectId, CreateInvoicesRequest payload, CurrentUser current, CancellationToken ct)
    {
        var selected = payload.ResidentIds?.ToHashSet() ?? [];

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var context = await _queries.GetInvoicePeriodContextAsync(projectId, ct);

        if (!context.Period.IsPayment)
        {
            throw AppException.BadRequest(context.Period.DuplicateIssueMessage);
        }

        var residents = await BuildResidentQuery(projectId, context).OrderBy(r => r.Id).ToListAsync(ct);
        if (residents.Count == 0) throw AppException.BadRequest("ไม่พบบ้านที่ต้องออกใบแจ้งหนี้");

        var rates = await LoadRatesAsync(projectId, ct);
        var period = new MonthYear(context.Period.Month, context.Period.Year);
        var previous = ThaiCalendar.PreviousMonth(period);
        var documentDate = _audit.ThaiDocumentDate;

        // ยอดของเดือนก่อนหน้า ใช้เป็นฐานคำนวณค่าปรับตอนบันทึกจริง
        var residentIds = residents.Select(r => r.Id).ToList();
        var previousAmounts = await _db.Payments.AsNoTracking()
            .Where(p => p.Resident.ProjectId == projectId
                        && residentIds.Contains(p.ResidentId)
                        && p.Month == previous.Month
                        && p.Year == previous.Year
                        && p.Status != PaymentStatus.PAID
                        && p.Status != PaymentStatus.CANCELLED)
            .OrderBy(p => p.Id)
            .Select(p => new { p.ResidentId, p.AmountDue })
            .ToListAsync(ct);

        // ระบบเดิมหยิบแถวแรกที่เจอเมื่อมีหลายแถว
        var previousByResident = previousAmounts
            .GroupBy(p => p.ResidentId)
            .ToDictionary(g => g.Key, g => g.First().AmountDue);

        var createdIds = new List<int>();

        foreach (var resident in residents)
        {
            var debts = await _queries.FindPriorNotPayDebtsAsync(
                projectId, resident.Id, period.Year, period.Month, null, ct);

            var isPrepaid = context.PrepaidResidentIds.Contains(resident.Id);
            var isSelected = selected.Contains(resident.Id);

            var line = InvoiceCalculator.Create(
                rates, resident.OwnershipRatio, debts, period,
                isSelectedResident: isSelected,
                isPrepaidPeriod: isPrepaid,
                previousMonthAmountDue: previousByResident.GetValueOrDefault(resident.Id));

            var payment = new Payment
            {
                ResidentId = resident.Id,
                InvNo = await _queries.NextInvoiceNumberAsync(projectId, ct),
                Year = period.Year,
                Month = (short)period.Month,
                AmountDue = line.Maintenance,
                Fine = line.Fine,
                CollectionFee = line.CollectionFee,
                TotalDue = line.TotalDue,
                PaymentDate = documentDate,
                Status = PaymentStatus.NOT_PAY,
                HasCollectionNotice = !isPrepaid && isSelected,
                CollectionPaid = false,
                Active = true,
                Advance = false,
            };
            _audit.StampCreate(payment, current.UserId);

            _db.Payments.Add(payment);
            await _db.SaveChangesAsync(ct);
            createdIds.Add(payment.Id);

            if (line.NeedsFineNotice)
            {
                var fineNotice = BuildNotice(resident.Id, payment.Id, NoticeType.FINE,
                    period, documentDate, line.Fine, current.UserId);
                _db.CollectionNotices.Add(fineNotice);
            }

            if (line.NeedsCollectionNotice)
            {
                var collectionNotice = BuildNotice(resident.Id, payment.Id, NoticeType.COLLECTION,
                    period, documentDate, line.CollectionFee, current.UserId);
                _db.CollectionNotices.Add(collectionNotice);
                await _db.SaveChangesAsync(ct);

                payment.CollectionNoticeId = collectionNotice.Id;
            }

            await _db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);

        return ApiResponseFactory.Ok(new CreateInvoicesResult
        {
            Month = period.Month,
            Year = period.Year,
            PaymentIds = createdIds,
            IsSupplement = context.Period.IsSupplement,
        });
    }

    // ─── ใบแจ้งหนี้ล่วงหน้า ──────────────────────────────────────────────────

    /// <summary>POST .../advance-invoices/preview</summary>
    public async Task<ApiResponse<AdvanceInvoicePreviewResponse>> PreviewAdvanceAsync(
        int projectId, AdvanceInvoicePreviewRequest request, CancellationToken ct)
    {
        var resident = await LoadResidentAsync(projectId, request.ResidentId, ct);
        var rates = await LoadRatesAsync(projectId, ct);

        var months = request.Months ?? [];
        var year = request.Year ?? _calendar.CurrentYear;

        var unitPrice = BillingCalculator.Maintenance(rates.MaintenanceFeeRate, resident.OwnershipRatio);

        return ApiResponseFactory.Ok(new AdvanceInvoicePreviewResponse
        {
            Resident = new ResidentBrief
            {
                Id = resident.Id,
                ProjectId = resident.ProjectId,
                HouseNo = resident.HouseNo,
                FullName = resident.FullName,
                Address = resident.Address,
            },
            Label = months.Count > 0 ? ReceiptItemBuilder.BuildThaiMonthRange(months, year) : string.Empty,
            UnitPrice = unitPrice,
            Units = months.Count,
            Total = unitPrice * months.Count,
        });
    }

    /// <summary>
    /// POST .../advance-invoices/year
    /// ขยับไปปีถัดไปเมื่อเป็นเดือนธันวาคมแล้ว หรือออกครบ 12 งวดของปีนี้แล้ว
    /// </summary>
    public async Task<ApiResponse<AdvanceInvoiceYearResponse>> PreviewYearAsync(
        int projectId, AdvanceInvoicePreviewRequest request, CancellationToken ct)
    {
        var resident = await LoadResidentAsync(projectId, request.ResidentId, ct);

        var currentYear = _calendar.CurrentYear;
        var currentMonth = _calendar.CurrentMonth;

        var count = await _db.Payments.AsNoTracking()
            .CountAsync(p => p.ResidentId == resident.Id && p.Year == currentYear, ct);

        var shouldMoveToNextYear = currentMonth == 12 || count == 12;

        return ApiResponseFactory.Ok(new AdvanceInvoiceYearResponse
        {
            Min = shouldMoveToNextYear ? currentYear + 1 : currentYear,
        });
    }

    /// <summary>POST .../advance-invoices/months — คืน 12 แถวเสมอ เดือนที่มีงวดแล้วจะเลือกไม่ได้</summary>
    public async Task<ApiResponse<IReadOnlyList<AdvanceInvoiceMonthOption>>> PreviewMonthsAsync(
        int projectId, AdvanceInvoicePreviewRequest request, CancellationToken ct)
    {
        var resident = await LoadResidentAsync(projectId, request.ResidentId, ct);
        var year = request.Year ?? _calendar.CurrentYear;

        var used = await _db.Payments.AsNoTracking()
            .Where(p => p.ResidentId == resident.Id && p.Year == year)
            .Select(p => (int)p.Month)
            .Distinct()
            .ToListAsync(ct);

        var usedSet = used.ToHashSet();

        var options = Enumerable.Range(1, 12).Select(month => new AdvanceInvoiceMonthOption
        {
            Month = month,
            MonthTh = ThaiMonths.GetAbbr(month),
            Active = !usedSet.Contains(month),
        }).ToList();

        return ApiResponseFactory.Ok((IReadOnlyList<AdvanceInvoiceMonthOption>)options);
    }

    /// <summary>
    /// POST .../advance-invoices — สร้างใบเสร็จหนึ่งใบ และงวดชำระล่วงหน้าหนึ่งแถวต่อเดือน
    /// ทุกแถวใช้ inv_no ร่วมกัน และสถานะเป็น PAID ตั้งแต่ต้น
    /// </summary>
    public async Task<ApiResponse<AdvanceInvoiceCreateResult>> CreateAdvanceAsync(
        int projectId, AdvanceInvoiceCreateRequest request, CurrentUser current, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var resident = await LoadResidentAsync(projectId, request.ResidentId, ct);
        var rates = await LoadRatesAsync(projectId, ct);

        var unitPrice = BillingCalculator.Maintenance(rates.MaintenanceFeeRate, resident.OwnershipRatio);
        var documentDate = _audit.ThaiDocumentDate;

        var receipt = new Receipt
        {
            ProjectId = projectId,
            ReceiptNo = await _queries.NextReceiptNumberAsync(projectId, ct),
            IssueDate = request.IssueDate,
            ResidentId = resident.Id,
            MonthCodes = ThaiMonths.ToEnAbbrList(request.Months),
            TotalAmount = request.TotalAmount ?? unitPrice * request.Months.Count,
            PaymentMethodId = request.PaymentMethodId,
            BankName = request.BankName,
            ChequeNo = request.ChequeNo,
            Note = request.Note,
            PaymentDate = documentDate,
            Active = true,
        };
        _audit.StampCreate(receipt, current.UserId);

        _db.Receipts.Add(receipt);
        await _db.SaveChangesAsync(ct);

        var invNo = await _queries.NextInvoiceNumberAsync(projectId, ct);
        var created = new List<Payment>();

        foreach (var month in request.Months.OrderBy(m => m))
        {
            var payment = new Payment
            {
                ResidentId = resident.Id,
                InvNo = invNo,
                Year = request.Year,
                Month = (short)month,
                AmountDue = unitPrice,
                Fine = 0m,
                CollectionFee = 0m,
                TotalDue = unitPrice,
                PaymentDate = documentDate,
                PaymentMethodId = request.PaymentMethodId,
                Status = PaymentStatus.PAID,
                Active = true,
                Advance = true,
                ReceiptsId = receipt.Id,
            };
            _audit.StampCreate(payment, current.UserId);

            _db.Payments.Add(payment);
            created.Add(payment);
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return ApiResponseFactory.Ok(new AdvanceInvoiceCreateResult
        {
            InvNo = invNo,
            Items = created.Select(PaymentService.ToResponse).ToList(),
        });
    }

    // ─── ตัวช่วย ────────────────────────────────────────────────────────────

    private IQueryable<Resident> BuildResidentQuery(int projectId, InvoicePeriodContext context)
    {
        var query = _db.Residents.AsNoTracking().Where(r => r.ProjectId == projectId);

        if (context.IncludedResidentIds is { } included)
        {
            var ids = included.ToList();
            query = query.Where(r => ids.Contains(r.Id));
        }

        return query;
    }

    private async Task<Resident> LoadResidentAsync(int projectId, int? residentId, CancellationToken ct)
    {
        if (residentId is null) throw AppException.BadRequest("ไม่พบข้อมูลลูกบ้านในโครงการนี้");

        return await _db.Residents.AsNoTracking()
                   .FirstOrDefaultAsync(r => r.Id == residentId && r.ProjectId == projectId, ct)
               ?? throw AppException.BadRequest("ไม่พบข้อมูลลูกบ้านในโครงการนี้");
    }

    /// <summary>อัตราค่าบริการของโครงการ — ระบบเดิมใช้แถวแรกเสมอ</summary>
    private async Task<BillingRates> LoadRatesAsync(int projectId, CancellationToken ct)
    {
        var setting = await _db.Settings.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw AppException.BadRequest("ไม่พบข้อมูลตั้งค่าค่าส่วนกลางของโครงการ");

        return new BillingRates(setting.MaintenanceFeeRate, setting.FineRate, setting.CollectionFee);
    }

    private CollectionNotice BuildNotice(
        int residentId, int paymentId, NoticeType type,
        MonthYear period, DateOnly noticeDate, decimal feeAmount, int userId)
    {
        var notice = new CollectionNotice
        {
            ResidentId = residentId,
            PaymentId = paymentId,
            NoticesType = type,
            Year = period.Year,
            Month = (short)period.Month,
            NoticeDate = noticeDate,
            FeeAmount = feeAmount,
            Status = NoticeStatus.PENDING,
            Active = true,
        };
        _audit.StampCreate(notice, userId);
        return notice;
    }
}
