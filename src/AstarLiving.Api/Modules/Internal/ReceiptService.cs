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
/// ใบเสร็จรับเงินค่าส่วนกลาง — พอร์ตจาก src/modules/internal/receipt/receipt.service.ts
/// ส่วนการพิมพ์ PDF จะทำในเฟสถัดไป
/// </summary>
public class ReceiptService(
    AstarLivingDbContext db,
    PaymentQueries queries,
    AuditStamp audit,
    ThaiCalendar calendar)
{
    private static readonly SortMap<Receipt> Sort = new SortMap<Receipt>("id")
        .Add("id", r => r.Id)
        .Add("receipt_no", r => r.ReceiptNo)
        .Add("issue_date", r => r.IssueDate)
        .Add("payment_date", r => r.PaymentDate)
        .Add("total_amount", r => r.TotalAmount)
        .Add("active", r => r.Active)
        .Add("house_no", r => r.Resident.HouseNo)
        .Add("full_name", r => r.Resident.FullName)
        .Add("created_at", r => r.CreatedAt);

    private readonly AstarLivingDbContext _db = db;
    private readonly PaymentQueries _queries = queries;
    private readonly AuditStamp _audit = audit;
    private readonly ThaiCalendar _calendar = calendar;

    /// <summary>
    /// POST /v1/receipts — ออกใบเสร็จ ทั้งหมดอยู่ในทรานแซกชันเดียว
    ///
    /// ขั้นตอน
    /// 1) แปลงรายการที่ติ๊กเลือกเป็นสถานะของแต่ละงวด (เลือกหมด/บางส่วน/ไม่เลือก)
    /// 2) บันทึกใบเสร็จ
    /// 3) ถ้าใช้ส่วนลดรายปี สร้างงวดล่วงหน้าเดือน 2-12 ให้อัตโนมัติ
    /// 4) ปิดใบทวงถามที่ถูกเลือกเป็น PAID แล้วผูกเข้าใบเสร็จ
    /// 5) อัปเดตสถานะงวดที่เกี่ยวข้อง
    /// </summary>
    public async Task<ApiResponse<ReceiptResponse>> CreateAsync(
        CreateReceiptRequest request, CurrentUser current, CancellationToken ct)
    {
        var resident = await _db.Residents.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.ResidentId && r.ProjectId == request.ProjectId, ct)
            ?? throw AppException.BadRequest("ไม่พบข้อมูลลูกบ้านในโครงการนี้");

        var setting = await _db.Settings.AsNoTracking()
            .Where(s => s.ProjectId == request.ProjectId)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw AppException.BadRequest("ไม่พบข้อมูลตั้งค่าค่าส่วนกลางของโครงการ");

        var monthNumbers = request.Months
            .Select(ThaiMonths.FromEnAbbr)
            .Where(m => m is not null)
            .Select(m => m!.Value)
            .ToList();

        if (monthNumbers.Count == 0) throw AppException.BadRequest("Invalid month");

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var documentDate = _audit.ThaiDocumentDate;

        var receipt = new Receipt
        {
            ProjectId = request.ProjectId,
            ReceiptNo = await _queries.NextReceiptNumberAsync(request.ProjectId, ct),
            IssueDate = request.IssueDate,
            ResidentId = request.ResidentId,
            MonthCodes = request.Months,
            TotalAmount = request.TotalAmount ?? 0m,
            PaymentMethodId = request.PaymentMethodId,
            BankName = request.BankName,
            ChequeNo = request.ChequeNo,
            Note = request.Note,
            PaymentDate = documentDate,
            Active = true,
            HaveDiscount = request.HaveDiscount,
            DiscountRate = request.DiscountRate,
        };

        // ระบบเดิมใช้ createDefaultNonActive() ตรงนี้ จึงไม่ได้บันทึก created_by ของใบเสร็จ
        // แต่หน้ารายงานต้องใช้ชื่อผู้ออกใบ ระบบใหม่จึงบันทึกให้ครบ
        _audit.StampCreate(receipt, current.UserId);

        _db.Receipts.Add(receipt);
        await _db.SaveChangesAsync(ct);

        if (request.HaveDiscount)
        {
            await CreateAnnualDiscountAdvancesAsync(request, resident, setting, receipt, documentDate, current, ct);
        }

        var selections = request.Payments.Select(BuildSelection).ToList();
        await ApplySelectedNoticesAsync(selections, receipt, current, ct);
        await ApplySelectedPaymentsAsync(selections, request, receipt, current, ct);

        await transaction.CommitAsync(ct);

        return ApiResponseFactory.Ok(ToResponse(receipt));
    }

    /// <summary>
    /// GET /v1/receipts/residents/:resident_id — งวดที่ยังเลือกออกใบเสร็จได้
    /// </summary>
    public async Task<ApiResponse<SelectableInvoicesResponse?>> GetSelectableInvoicesAsync(
        int residentId, CancellationToken ct)
    {
        var resident = await _db.Residents.AsNoTracking().FirstOrDefaultAsync(r => r.Id == residentId, ct);
        if (resident is null) return ApiResponseFactory.NotFound<SelectableInvoicesResponse>();

        var year = _calendar.CurrentYear;
        var month = _calendar.CurrentMonth;

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.ResidentId == residentId && p.Status != PaymentStatus.PAID)
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync(ct);

        // ใบแจ้งเพื่อทราบที่ไม่มียอดคิดใหม่ ไม่ต้องให้เลือก
        var selectable = payments
            .Where(p => !InvoiceInclusion.IsZeroCharge(p.AmountDue, p.Fine, p.CollectionFee))
            .ToList();

        var haveDiscount = await EvaluateAnnualDiscountAsync(residentId, year, month, ct);

        var paymentIds = selectable.Select(p => p.Id).ToList();
        var notices = await _db.CollectionNotices.AsNoTracking()
            .Where(n => n.PaymentId != null && paymentIds.Contains(n.PaymentId.Value))
            .ToListAsync(ct);

        var noticesByPayment = notices
            .GroupBy(n => n.PaymentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var invoices = new List<SelectableInvoice>();

        foreach (var payment in selectable)
        {
            var related = noticesByPayment.GetValueOrDefault(payment.Id, []);

            // ใบทวงถามของงวดนั้นที่ยังไม่ถูกยกเลิก และต้องเป็นงวดเดือน/ปีเดียวกัน
            var fine = related.FirstOrDefault(n => n.NoticesType == NoticeType.FINE
                                                   && n.Status != NoticeStatus.CANCELLED
                                                   && n.Month == payment.Month && n.Year == payment.Year);
            var collection = related.FirstOrDefault(n => n.NoticesType == NoticeType.COLLECTION
                                                         && n.Status != NoticeStatus.CANCELLED
                                                         && n.Month == payment.Month && n.Year == payment.Year);

            var maintenance = payment.AmountDue;
            var fineAmount = fine?.FeeAmount ?? 0m;
            var collectionAmount = collection?.FeeAmount ?? 0m;

            var items = new List<SelectableItem>
            {
                new()
                {
                    Name = "ค่าส่วนกลาง",
                    Key = nameof(ReceiptItemType.MAINTENANCE),
                    Amount = maintenance,
                    Id = payment.Id,
                },
            };

            if (fineAmount > 0m && fine is not null)
            {
                items.Add(new SelectableItem
                {
                    Name = "ค่าเบี้ยปรับ",
                    Key = nameof(ReceiptItemType.FINE),
                    Amount = fineAmount,
                    Id = fine.Id,
                    Status = fine.Status,
                });
            }

            if (collectionAmount > 0m && collection is not null)
            {
                items.Add(new SelectableItem
                {
                    Name = "ค่าทวงถาม",
                    Key = nameof(ReceiptItemType.COLLECTION),
                    Amount = collectionAmount,
                    Id = collection.Id,
                    Status = collection.Status,
                });
            }

            // งวดที่ทุกรายการชำระแล้วจะไม่แสดงให้เลือก
            if (!items.Any(i => i.Status != NoticeStatus.PAID)) continue;

            invoices.Add(new SelectableInvoice
            {
                PaymentId = payment.Id,
                Status = payment.Status,
                InvoiceNo = payment.InvNo,
                Month = payment.Month,
                Year = payment.Year,
                MonthTh = ThaiCalendar.FormatThaiMonthYear(payment.Month, payment.Year),
                MonthEn = ThaiMonths.GetEnAbbr(payment.Month),
                IsNoticesCancelled = related.Count > 0 && related.All(n => n.Status == NoticeStatus.CANCELLED),
                Items = items,
                Total = maintenance + fineAmount + collectionAmount,
            });
        }

        return ApiResponseFactory.Create<SelectableInvoicesResponse?>(HttpStatusCatalog.Ok,
            new SelectableInvoicesResponse
            {
                HaveDiscount = haveDiscount,
                Resident = ToNameBrief(resident),
                Invoices = invoices,
            });
    }

    /// <summary>
    /// POST /v1/receipts/residents/:resident_id — สรุปยอดก่อนออกใบเสร็จ
    ///
    /// เมื่อใช้ส่วนลดรายปี บรรทัดค่าส่วนกลางจะถูกคูณ 12 และช่วงเดือนถูกบังคับเป็น ม.ค. ถึง ธ.ค.
    /// </summary>
    public async Task<ApiResponse<ReceiptPreviewResponse?>> CalculatePreviewAsync(
        int residentId, CalculateReceiptPreviewRequest request, CancellationToken ct)
    {
        var resident = await _db.Residents.AsNoTracking()
            .Include(r => r.Project).ThenInclude(p => p.Settings)
            .FirstOrDefaultAsync(r => r.Id == residentId, ct);

        if (resident is null) return ApiResponseFactory.NotFound<ReceiptPreviewResponse>();

        if (request.Payments.Count == 0)
        {
            return ApiResponseFactory.Create<ReceiptPreviewResponse?>(
                HttpStatusCatalog.BadRequest with { Message = "กรุณาเลือกงวดชำระอย่างน้อย 1 งวด" }, null);
        }

        var paymentIds = request.Payments.Select(p => p.PaymentId).ToList();
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => paymentIds.Contains(p.Id) && p.ResidentId == residentId)
            .ToListAsync(ct);

        if (payments.Count == 0)
        {
            return ApiResponseFactory.Create<ReceiptPreviewResponse?>(
                HttpStatusCatalog.NotFound with { Message = "ไม่พบข้อมูลงวดชำระที่เลือก" }, null);
        }

        var notices = await _db.CollectionNotices.AsNoTracking()
            .Where(n => n.PaymentId != null && paymentIds.Contains(n.PaymentId.Value))
            .ToListAsync(ct);

        var noticesByPayment = notices
            .GroupBy(n => n.PaymentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var selectionByPayment = request.Payments.ToDictionary(p => p.PaymentId, p => p.Items);
        var summary = new Dictionary<ReceiptItemType, SummaryAccumulator>
        {
            [ReceiptItemType.MAINTENANCE] = new(),
            [ReceiptItemType.FINE] = new(),
            [ReceiptItemType.COLLECTION] = new(),
        };

        foreach (var payment in payments)
        {
            if (!selectionByPayment.TryGetValue(payment.Id, out var items)) continue;

            var related = noticesByPayment.GetValueOrDefault(payment.Id, []);
            var amounts = new Dictionary<ReceiptItemType, decimal>
            {
                [ReceiptItemType.MAINTENANCE] = payment.AmountDue,
                [ReceiptItemType.FINE] = related
                    .FirstOrDefault(n => n.NoticesType == NoticeType.FINE && n.Status != NoticeStatus.CANCELLED)
                    ?.FeeAmount ?? 0m,
                [ReceiptItemType.COLLECTION] = related
                    .FirstOrDefault(n => n.NoticesType == NoticeType.COLLECTION && n.Status != NoticeStatus.CANCELLED)
                    ?.FeeAmount ?? 0m,
            };

            foreach (var item in items.Where(i => i.Selected))
            {
                var unitPrice = amounts.GetValueOrDefault(item.Type);
                if (unitPrice == 0m) continue;

                // ค่าปรับและค่าทวงถามใช้ชื่อเดือน "ก่อนหน้า" งวดนั้นเสมอ
                var period = item.Type == ReceiptItemType.MAINTENANCE
                    ? new Domain.Common.MonthYear(payment.Month, payment.Year)
                    : ThaiCalendar.PreviousMonth(payment.Month, payment.Year);

                summary[item.Type].Add(unitPrice, period);
            }
        }

        var discountRate = resident.Project.Settings.OrderBy(s => s.Id).FirstOrDefault()?.DiscountRate ?? 0m;
        var summaryItems = BuildSummaryItems(summary, request.HaveDiscount);

        var subtotal = summaryItems.Sum(i => i.Total);
        var discount = discountRate * subtotal / 100m;
        var totalAmount = request.HaveDiscount ? subtotal - discount : subtotal;

        if (request.HaveDiscount)
        {
            summaryItems.Add(new ReceiptSummaryItem
            {
                Name = $"ส่วนลด {discountRate} %",
                Count = string.Empty,
                UnitPrice = string.Empty,
                Total = Round2(discount),
            });
        }

        return ApiResponseFactory.Create<ReceiptPreviewResponse?>(HttpStatusCatalog.Ok, new ReceiptPreviewResponse
        {
            Resident = ToNameBrief(resident),
            Summary = new ReceiptSummary
            {
                Items = summaryItems,
                HaveDiscount = request.HaveDiscount,
                Discount = discount,
                TotalAmount = Round2(totalAmount),
            },
        });
    }

    public async Task<ApiResponse<IReadOnlyList<ReceiptResponse>>> FindAllByProjectAsync(
        int projectId, ReceiptFilterQuery filter, CancellationToken ct)
    {
        var query = _db.Receipts.AsNoTracking()
            .Include(r => r.Resident)
            .Where(r => r.ProjectId == projectId)
            .WhereContains(filter.ReceiptNo, r => r.ReceiptNo)
            .WhereContains(filter.HouseNo, r => r.Resident.HouseNo);

        if (filter.ResidentId is { } residentId) query = query.Where(r => r.ResidentId == residentId);
        if (filter.Active is { } active) query = query.Where(r => r.Active == active);
        if (filter.IssueStartDate is { } start) query = query.Where(r => r.IssueDate >= start);
        if (filter.IssueEndDate is { } end) query = query.Where(r => r.IssueDate <= end);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok,
            (IReadOnlyList<ReceiptResponse>)page.Items.Select(ToResponse).ToList(),
            page.PageInformation);
    }

    public async Task<ApiResponse<ReceiptResponse?>> FindOneAsync(int id, CancellationToken ct)
    {
        var receipt = await _db.Receipts.AsNoTracking()
            .Include(r => r.Resident)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        return receipt is null
            ? ApiResponseFactory.NotFound<ReceiptResponse>()
            : ApiResponseFactory.Create<ReceiptResponse?>(HttpStatusCatalog.Ok, ToResponse(receipt));
    }

    public async Task<ApiResponse<ReceiptResponse?>> UpdateAsync(
        int id, UpdateReceiptRequest request, CurrentUser current, CancellationToken ct)
    {
        var receipt = await _db.Receipts.Include(r => r.Resident).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (receipt is null) return ApiResponseFactory.NotFound<ReceiptResponse>();

        if (request.IssueDate is { } issueDate) receipt.IssueDate = issueDate;
        if (request.TotalAmount is { } total) receipt.TotalAmount = total;
        if (request.PaymentMethodId is not null) receipt.PaymentMethodId = request.PaymentMethodId;
        if (request.BankName is not null) receipt.BankName = request.BankName;
        if (request.ChequeNo is not null) receipt.ChequeNo = request.ChequeNo;
        if (request.Note is not null) receipt.Note = request.Note;
        if (request.Active is { } active) receipt.Active = active;

        _audit.StampUpdate(receipt, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Create<ReceiptResponse?>(HttpStatusCatalog.Ok, ToResponse(receipt));
    }

    /// <summary>
    /// PATCH /v1/receipts/rejected/:id — ยกเลิกใบเสร็จและคืนสถานะทุกอย่างกลับ
    ///
    /// 1) ปิดใบเสร็จและบันทึกเหตุผล
    /// 2) งวดปกติกลับเป็น NOT_PAY และตัดความผูกพันกับใบเสร็จ
    /// 3) งวดล่วงหน้าที่ระบบสร้างให้ตอนใช้ส่วนลด ถูกลบแบบ soft delete
    /// 4) ใบทวงถามกลับเป็น PENDING
    ///
    /// หมายเหตุ: ระบบเดิมมีทางออกก่อนกำหนด (return เปล่า ๆ) เมื่อใบเสร็จไม่มีงวดผูกอยู่
    /// ทำให้ใบทวงถามไม่ถูกคืนสถานะและผู้เรียกไม่ได้รับซองตอบกลับ ระบบใหม่ทำครบทุกขั้นเสมอ
    /// </summary>
    public async Task<ApiResponse<ReceiptResponse?>> RejectAsync(
        int id, RejectReceiptRequest request, CurrentUser current, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var receipt = await _db.Receipts.Include(r => r.Resident).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (receipt is null) return ApiResponseFactory.NotFound<ReceiptResponse>();

        receipt.Active = false;
        receipt.RejectedReason = request.RejectedReason;
        _audit.StampUpdate(receipt, current.UserId);

        var payments = await _db.Payments.Where(p => p.ReceiptsId == id).ToListAsync(ct);

        foreach (var payment in payments)
        {
            payment.Status = PaymentStatus.NOT_PAY;
            payment.ReceiptsId = null;

            if (payment.Advance)
            {
                // งวดล่วงหน้าเกิดจากใบเสร็จใบนี้ เมื่อยกเลิกใบเสร็จจึงต้องหายไปด้วย
                _audit.StampDelete(payment, current.UserId);
            }
            else
            {
                _audit.StampUpdate(payment, current.UserId);
            }
        }

        var notices = await _db.CollectionNotices.Where(n => n.ReceiptId == id).ToListAsync(ct);
        foreach (var notice in notices)
        {
            notice.Status = NoticeStatus.PENDING;
            notice.ReceiptId = null;
            _audit.StampUpdate(notice, current.UserId);
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return ApiResponseFactory.Create<ReceiptResponse?>(HttpStatusCatalog.Ok, ToResponse(receipt));
    }

    public async Task<ApiResponse<object>> RemoveAsync(int id, CurrentUser current, CancellationToken ct)
    {
        var receipt = await _db.Receipts.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (receipt is not null)
        {
            _audit.StampDelete(receipt, current.UserId);
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponseFactory.Ok<object>(new { });
    }

    // ─── ตัวช่วยภายใน ───────────────────────────────────────────────────────

    /// <summary>
    /// เงื่อนไขได้ส่วนลดรายปี: ยังไม่มีงวดใดของปีนี้ที่เดือนมากกว่า 1
    /// และตอนนี้เป็นเดือนมกราคม และมีงวดเดือน 1 ที่ยังไม่ชำระอยู่
    /// </summary>
    private async Task<bool> EvaluateAnnualDiscountAsync(
        int residentId, int year, int month, CancellationToken ct)
    {
        var laterMonthCount = await _db.Payments.AsNoTracking()
            .CountAsync(p => p.ResidentId == residentId && p.Year == year && p.Month > 1, ct);

        var januaryUnpaid = await _db.Payments.AsNoTracking()
            .CountAsync(p => p.ResidentId == residentId && p.Year == year
                             && p.Month == 1 && p.Status == PaymentStatus.NOT_PAY, ct);

        return laterMonthCount == 0 && month == 1 && januaryUnpaid > 0;
    }

    /// <summary>สร้างงวดล่วงหน้าเดือน 2-12 ให้อัตโนมัติเมื่อใช้ส่วนลดรายปี</summary>
    private async Task CreateAnnualDiscountAdvancesAsync(
        CreateReceiptRequest request, Resident resident, Setting setting,
        Receipt receipt, DateOnly documentDate, CurrentUser current, CancellationToken ct)
    {
        var maintenance = BillingCalculator.Maintenance(setting.MaintenanceFeeRate, resident.OwnershipRatio);
        var invNo = await _queries.NextInvoiceNumberAsync(request.ProjectId, ct);
        var year = _calendar.CurrentYear;

        foreach (var month in InvoiceIssuingService.AdvanceMaintenanceMonths)
        {
            var payment = new Payment
            {
                ResidentId = request.ResidentId,
                InvNo = invNo,
                Year = year,
                Month = (short)month,
                AmountDue = maintenance,
                Fine = 0m,
                CollectionFee = 0m,
                TotalDue = maintenance,
                PaymentDate = documentDate,
                Status = PaymentStatus.PAID,
                Active = true,
                HasCollectionNotice = false,
                CollectionPaid = false,
                ReceiptsId = receipt.Id,
                Advance = true,
            };
            _audit.StampCreate(payment, current.UserId);
            _db.Payments.Add(payment);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>ปิดใบทวงถามที่ถูกเลือก แล้วอัปเดตสถานะงวดที่ใบทวงถามนั้นผูกอยู่</summary>
    private async Task ApplySelectedNoticesAsync(
        List<PaymentSelectionResult> selections, Receipt receipt, CurrentUser current, CancellationToken ct)
    {
        var noticeIds = selections.SelectMany(s => s.NoticeIds).Distinct().ToList();
        if (noticeIds.Count == 0) return;

        var notices = await _db.CollectionNotices.Where(n => noticeIds.Contains(n.Id)).ToListAsync(ct);

        foreach (var notice in notices)
        {
            notice.Status = NoticeStatus.PAID;
            notice.ReceiptId = receipt.Id;
            _audit.StampUpdate(notice, current.UserId);
        }

        var relatedPaymentIds = notices
            .Where(n => n.PaymentId is not null)
            .Select(n => n.PaymentId!.Value)
            .Distinct()
            .ToList();

        if (relatedPaymentIds.Count == 0)
        {
            await _db.SaveChangesAsync(ct);
            return;
        }

        var selectionByPayment = selections.ToDictionary(s => s.PaymentId ?? 0, s => s);
        var payments = await _db.Payments.Where(p => relatedPaymentIds.Contains(p.Id)).ToListAsync(ct);

        foreach (var payment in payments)
        {
            // ถ้างวดนี้อยู่ในชุดที่ผู้ใช้เลือกมาด้วย ใช้สถานะที่คำนวณไว้
            // ถ้าไม่อยู่ ให้คงสถานะ PAID เดิมไว้ มิฉะนั้นถือว่าชำระบางส่วน
            payment.Status = selectionByPayment.TryGetValue(payment.Id, out var selection)
                ? selection.Status
                : payment.Status == PaymentStatus.PAID ? PaymentStatus.PAID : PaymentStatus.PARTIAL_PAY;

            payment.ReceiptsId = receipt.Id;
            _audit.StampUpdate(payment, current.UserId);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>ผูกงวดที่เลือกชำระค่าส่วนกลางเข้ากับใบเสร็จ</summary>
    private async Task ApplySelectedPaymentsAsync(
        List<PaymentSelectionResult> selections, CreateReceiptRequest request,
        Receipt receipt, CurrentUser current, CancellationToken ct)
    {
        var paymentIds = selections
            .Where(s => s.PaymentId is not null)
            .Select(s => s.PaymentId!.Value)
            .ToList();

        if (paymentIds.Count == 0) return;

        var selectionByPayment = selections
            .Where(s => s.PaymentId is not null)
            .ToDictionary(s => s.PaymentId!.Value, s => s);

        var payments = await _db.Payments
            .Where(p => paymentIds.Contains(p.Id)
                        && p.ResidentId == request.ResidentId
                        && p.Resident.ProjectId == request.ProjectId)
            .ToListAsync(ct);

        foreach (var payment in payments)
        {
            if (!selectionByPayment.TryGetValue(payment.Id, out var selection)) continue;

            payment.ReceiptsId = receipt.Id;
            payment.Status = selection.Status;
            payment.CollectionPaid = selection.CollectionPaid;
            _audit.StampUpdate(payment, current.UserId);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>แปลงรายการที่ติ๊กเลือกของงวดหนึ่งเป็นสถานะและรายการใบทวงถามที่ต้องปิด</summary>
    private static PaymentSelectionResult BuildSelection(ReceiptPaymentSelection selection)
    {
        var outcome = ReceiptSelectionRules.Resolve(
            selection.Items
                .Select(i => new SelectedReceiptItem(i.Type, i.Selected, i.Id))
                .ToList());

        return new PaymentSelectionResult(
            outcome.MaintenancePaymentId,
            outcome.Status,
            outcome.NoticeIds.ToList(),
            outcome.CollectionPaid);
    }

    private static List<ReceiptSummaryItem> BuildSummaryItems(
        Dictionary<ReceiptItemType, SummaryAccumulator> summary, bool haveDiscount)
    {
        var names = new Dictionary<ReceiptItemType, string>
        {
            [ReceiptItemType.MAINTENANCE] = "ค่าส่วนกลาง",
            [ReceiptItemType.FINE] = "ค่าเบี้ยปรับ",
            [ReceiptItemType.COLLECTION] = "ค่าทวงถาม",
        };

        var items = new List<ReceiptSummaryItem>();

        foreach (var (type, row) in summary)
        {
            if (row.Count == 0) continue;

            // ส่วนลดรายปีคิดค่าส่วนกลางทั้งปี บรรทัดจึงถูกคูณ 12 และช่วงเดือนบังคับเป็น ม.ค. ถึง ธ.ค.
            var multiplier = haveDiscount && type == ReceiptItemType.MAINTENANCE ? 12 : 1;

            items.Add(new ReceiptSummaryItem
            {
                Name = $"{names[type]} {row.BuildRangeText(forceFullYear: multiplier == 12)}",
                Count = row.Count * multiplier,
                UnitPrice = Round2(row.UnitPrice * multiplier),
                Total = Round2(row.Total * multiplier),
            });
        }

        return items;
    }

    /// <summary>ตรงกับ Number(x.toFixed(2)) ของ JavaScript</summary>
    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static ResidentNameBrief ToNameBrief(Resident r) => new()
    {
        Id = r.Id,
        Name = r.FullName,
        HouseNo = r.HouseNo,
    };

    private static ReceiptResponse ToResponse(Receipt r) => new()
    {
        Id = r.Id,
        ProjectId = r.ProjectId,
        ReceiptNo = r.ReceiptNo,
        IssueDate = r.IssueDate,
        ResidentId = r.ResidentId,
        PaymentIds = r.MonthCodes,
        TotalAmount = r.TotalAmount,
        PaymentMethodId = r.PaymentMethodId,
        BankName = r.BankName,
        ChequeNo = r.ChequeNo,
        Note = r.Note,
        RejectedReason = r.RejectedReason,
        PaymentDate = r.PaymentDate,
        Active = r.Active,
        HaveDiscount = r.HaveDiscount,
        DiscountRate = r.DiscountRate,
        CreatedBy = r.CreatedBy,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        Resident = r.Resident is null ? null : new ResidentBrief
        {
            Id = r.Resident.Id,
            ProjectId = r.Resident.ProjectId,
            HouseNo = r.Resident.HouseNo,
            FullName = r.Resident.FullName,
            Address = r.Resident.Address,
        },
    };

    /// <summary>ผลการแปลงรายการที่เลือกของงวดหนึ่ง</summary>
    private sealed record PaymentSelectionResult(
        int? PaymentId, PaymentStatus Status, List<int> NoticeIds, bool CollectionPaid);

    /// <summary>ตัวสะสมยอดต่อชนิดรายการ พร้อมช่วงเดือนที่น้อยสุดและมากสุด</summary>
    private sealed class SummaryAccumulator
    {
        public int Count { get; private set; }
        public decimal UnitPrice { get; private set; }
        public decimal Total { get; private set; }

        private Domain.Common.MonthYear? _min;
        private Domain.Common.MonthYear? _max;

        public void Add(decimal unitPrice, Domain.Common.MonthYear period)
        {
            Count += 1;
            UnitPrice = unitPrice;
            Total = Count * unitPrice;

            if (_min is null || period < _min.Value) _min = period;
            if (_max is null || period > _max.Value) _max = period;
        }

        public string BuildRangeText(bool forceFullYear)
        {
            if (_min is null) return string.Empty;

            var min = _min.Value;
            var max = _max ?? min;

            var startMonth = forceFullYear ? 1 : min.Month;
            var endMonth = forceFullYear ? 12 : max.Month;
            var endYear = forceFullYear ? min.Year : max.Year;

            var start = ThaiCalendar.FormatThaiMonthYear(startMonth, min.Year);
            var end = ThaiCalendar.FormatThaiMonthYear(endMonth, endYear);

            return start == end ? $"({start})" : $"({start} - {end})";
        }
    }
}
