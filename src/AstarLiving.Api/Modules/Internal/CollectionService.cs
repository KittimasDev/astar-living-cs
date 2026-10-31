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
/// ใบทวงถามและหน้าลดหนี้ — พอร์ตจาก src/modules/internal/collection/collection.service.ts
/// ส่วนการพิมพ์ PDF จะทำในเฟสถัดไป
/// </summary>
public class CollectionService(AstarLivingDbContext db, AuditStamp audit)
{
    private static readonly SortMap<CollectionNotice> Sort = new SortMap<CollectionNotice>("id")
        .Add("id", n => n.Id)
        .Add("resident_id", n => n.ResidentId)
        .Add("year", n => n.Year)
        .Add("month", n => n.Month)
        .Add("notice_date", n => n.NoticeDate)
        .Add("status", n => n.Status)
        .Add("fee_amount", n => n.FeeAmount)
        .Add("house_no", n => n.Resident.HouseNo)
        .Add("created_at", n => n.CreatedAt);

    private readonly AstarLivingDbContext _db = db;
    private readonly AuditStamp _audit = audit;

    /// <summary>
    /// GET /v1/reduce — รายการหน้าลดหนี้
    ///
    /// หนึ่งงวดอาจมีใบทวงถามได้ทั้งชนิด FINE และ COLLECTION แต่หน้าจอต้องการแถวเดียวต่องวด
    /// จึงเลือก "ตัวแทน" ด้วยลำดับความสำคัญ COLLECTION มาก่อน FINE แล้วเอา id ล่าสุด
    /// ใบที่ไม่ได้ผูกกับงวดใดจะถือเป็นกลุ่มของตัวเอง
    /// </summary>
    public async Task<ApiResponse<IReadOnlyList<ReduceNoticeResponse>>> FindAllForReduceAsync(
        CollectionFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        var projectId = await ResolveProjectIdAsync(current, ct);

        var candidates = BuildReduceQuery(projectId, filter);

        // คัดตัวแทนของแต่ละงวดด้วย ROW_NUMBER แบบเดียวกับระบบเดิม
        var ranked = await candidates
            .Select(n => new
            {
                n.Id,
                Key = n.PaymentId != null ? "p:" + n.PaymentId : "n:" + n.Id,
                TypeRank = n.NoticesType == NoticeType.COLLECTION ? 0 : n.NoticesType == NoticeType.FINE ? 1 : 2,
            })
            .ToListAsync(ct);

        var representativeIds = ranked
            .GroupBy(x => x.Key)
            .Select(g => g.OrderBy(x => x.TypeRank).ThenByDescending(x => x.Id).First().Id)
            .ToList();

        var total = representativeIds.Count;

        var query = _db.CollectionNotices.AsNoTracking()
            .Include(n => n.Resident)
            .Include(n => n.Payment)
            .Where(n => representativeIds.Contains(n.Id));

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        var items = await EnrichAsync(page.Items, ct);

        // จำนวนรวมต้องนับจาก "งวด" ไม่ใช่จำนวนใบทวงถามทั้งหมด
        var pageInfo = filter.NoLimit
            ? Pagination.BuildPageInfo(page.PageInformation.OrderField, page.PageInformation.OrderType,
                total, items.Count, items.Count, 1, noLimit: true)
            : Pagination.BuildPageInfo(page.PageInformation.OrderField, page.PageInformation.OrderType,
                total, items.Count, filter.Limit, filter.Page);

        return ApiResponseFactory.CreateFind(HttpStatusCatalog.Ok, items, pageInfo);
    }

    /// <summary>GET /v1/reduce/:id</summary>
    public async Task<ApiResponse<ReduceNoticeResponse?>> FindOneForReduceAsync(
        int id, CurrentUser current, CancellationToken ct)
    {
        var projectId = await ResolveProjectIdAsync(current, ct);

        var notice = await _db.CollectionNotices.AsNoTracking()
            .Include(n => n.Resident)
            .Include(n => n.Payment)
            .FirstOrDefaultAsync(n => n.Id == id && n.Resident.ProjectId == projectId, ct);

        if (notice is null) return ApiResponseFactory.NotFound<ReduceNoticeResponse>();

        var enriched = await EnrichAsync([notice], ct);

        return ApiResponseFactory.Create<ReduceNoticeResponse?>(HttpStatusCatalog.Ok, enriched[0]);
    }

    /// <summary>
    /// GET /v1/reduce-check/:id — ตรวจก่อนกดลดหนี้
    /// เตือนเมื่องวดเดือนก่อนหน้ายังค้างชำระ เพราะการลดหนี้งวดนี้อาจไม่ใช่สิ่งที่ผู้ใช้ตั้งใจ
    /// </summary>
    public async Task<ApiResponse<ReduceCheckResponse?>> ReduceCheckAsync(
        int id, CurrentUser current, CancellationToken ct)
    {
        var projectId = await ResolveProjectIdAsync(current, ct);

        var notice = await _db.CollectionNotices.AsNoTracking()
            .Include(n => n.Resident)
            .Include(n => n.Payment)
            .FirstOrDefaultAsync(n => n.Id == id && n.Resident.ProjectId == projectId, ct);

        if (notice is null) return ApiResponseFactory.NotFound<ReduceCheckResponse>();

        var previous = ThaiCalendar.PreviousMonth(notice.Month, notice.Year);

        var previousPayment = await _db.Payments.AsNoTracking()
            .Include(p => p.Resident)
            .FirstOrDefaultAsync(p => p.ResidentId == notice.ResidentId
                                      && p.Year == previous.Year
                                      && p.Month == previous.Month
                                      && p.Active, ct);

        var hasOutstanding = previousPayment is not null
            && previousPayment.Status is PaymentStatus.NOT_PAY or PaymentStatus.PARTIAL_PAY;

        var fees = await SummariseFeesAsync(notice.PaymentId, ct);
        var enriched = await EnrichAsync([notice], ct);

        return ApiResponseFactory.Create<ReduceCheckResponse?>(HttpStatusCatalog.Ok, new ReduceCheckResponse
        {
            Message = hasOutstanding ? "เดือนก่อนหน้ามียอดค้างชำระ" : null,
            PreviousMonthHasOutstanding = hasOutstanding,
            CollectionNotice = enriched[0],
            NoticePayment = notice.Payment is null ? null : PaymentService.ToResponse(notice.Payment),
            PreviousMonth = new PreviousMonthInfo
            {
                Year = previous.Year,
                Month = previous.Month,
                Payment = previousPayment is null ? null : PaymentService.ToResponse(previousPayment),
            },
            FeeAmountFine = fees.Fine,
            FeeAmountCollection = fees.Collection,
            FeeAmountTotal = fees.Total,
        });
    }

    /// <summary>
    /// PATCH /v1/reduce/:id — ลดหนี้ (ยกเลิกค่าเบี้ยปรับและค่าทวงถามของงวดนั้น)
    ///
    /// ยกเลิก "ทุกใบที่ผูกกับงวดเดียวกัน" ไม่ใช่เฉพาะใบที่กด แล้วปรับงวดให้เหลือเฉพาะค่าส่วนกลาง
    /// หมายเหตุ: จงใจไม่ล้าง collection_notice_id และ has_collection_notice ของงวด
    /// เพื่อให้ยังรู้ประวัติว่าเคยมีใบทวงถาม ตามพฤติกรรมเดิม
    /// </summary>
    public async Task<ApiResponse<CancelReduceResponse?>> CancelReduceAsync(
        int id, CancelReduceRequest request, CurrentUser current, CancellationToken ct)
    {
        var projectId = await ResolveProjectIdAsync(current, ct);

        var notice = await _db.CollectionNotices
            .Include(n => n.Resident)
            .FirstOrDefaultAsync(n => n.Id == id && n.Resident.ProjectId == projectId, ct);

        if (notice is null) return ApiResponseFactory.NotFound<CancelReduceResponse>();

        if (notice.PaymentId is null)
        {
            return ApiResponseFactory.Create<CancelReduceResponse?>(
                HttpStatusCatalog.BadRequest with { Message = "ใบทวงถามนี้ไม่ได้ผูก gv_payments (payment_id ว่าง)" },
                null);
        }

        var payment = await _db.Payments
            .Include(p => p.Resident)
            .FirstOrDefaultAsync(p => p.Id == notice.PaymentId.Value, ct);

        if (payment is null) return ApiResponseFactory.NotFound<CancelReduceResponse>();

        if (payment.Status == PaymentStatus.PAID)
        {
            return ApiResponseFactory.Create<CancelReduceResponse?>(
                HttpStatusCatalog.BadRequest with { Message = "ไม่สามารถยกเลิกงวดที่ชำระแล้ว" }, null);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var siblings = await _db.CollectionNotices
            .Where(n => n.PaymentId == notice.PaymentId)
            .ToListAsync(ct);

        foreach (var sibling in siblings)
        {
            sibling.Status = NoticeStatus.CANCELLED;
            sibling.DeductedAt = _audit.Now;
            sibling.DeductedBy = current.UserId;
            if (request.DeductedReason is not null) sibling.DeductedReason = request.DeductedReason;
            _audit.StampUpdate(sibling, current.UserId);
        }

        // เหลือเฉพาะค่าส่วนกลางที่ยังไม่ได้ชำระ
        payment.Status = PaymentStatus.NOT_PAY;
        payment.Fine = 0m;
        payment.CollectionFee = 0m;
        payment.TotalDue = Math.Max(0m, payment.AmountDue - (payment.PaidAmount ?? 0m));
        _audit.StampUpdate(payment, current.UserId);

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var enriched = await EnrichAsync([notice], ct);

        return ApiResponseFactory.Create<CancelReduceResponse?>(HttpStatusCatalog.Ok, new CancelReduceResponse
        {
            CollectionNotice = enriched[0],
            Payment = PaymentService.ToResponse(payment),
        });
    }

    /// <summary>
    /// GET /v1/projects/:project_id/collections — หน้าใบทวงถาม
    /// ระบบเดิมกรองเฉพาะชนิด COLLECTION ในเส้นทางนี้ (ค่าเบี้ยปรับไม่ถูกแสดง)
    /// </summary>
    public async Task<ApiResponse<IReadOnlyList<CollectionNoticeResponse>>> FindAllByProjectAsync(
        int projectId, CollectionFilterQuery filter, CancellationToken ct)
    {
        var query = _db.CollectionNotices.AsNoTracking()
            .Include(n => n.Resident)
            .Where(n => n.Resident.ProjectId == projectId && n.NoticesType == NoticeType.COLLECTION);

        query = ApplyCommonFilter(query, filter);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok,
            (IReadOnlyList<CollectionNoticeResponse>)page.Items.Select(ToResponse).ToList(),
            page.PageInformation);
    }

    /// <summary>GET /v1/projects/:project_id/collections/outstanding</summary>
    public async Task<ApiResponse<IReadOnlyList<CollectionNoticeResponse>>> FindOutstandingAsync(
        int projectId, CancellationToken ct)
    {
        var rows = await _db.CollectionNotices.AsNoTracking()
            .Include(n => n.Resident)
            .Where(n => n.Resident.ProjectId == projectId && n.Status == NoticeStatus.PENDING)
            .OrderBy(n => n.Id)
            .ToListAsync(ct);

        return ApiResponseFactory.Ok((IReadOnlyList<CollectionNoticeResponse>)rows.Select(ToResponse).ToList());
    }

    public async Task<ApiResponse<CollectionNoticeResponse>> CreateAsync(
        CreateCollectionRequest request, CurrentUser current, CancellationToken ct)
    {
        var notice = new CollectionNotice
        {
            ResidentId = request.ResidentId,
            PaymentId = request.PaymentId,
            NoticesType = request.NoticesType,
            Year = request.Year,
            Month = request.Month,
            NoticeDate = request.NoticeDate,
            FeeAmount = request.FeeAmount,
            Status = request.Status,
            ReceiptId = request.ReceiptId,
            Note = request.Note,
            Active = true,
        };
        _audit.StampCreate(notice, current.UserId);

        _db.CollectionNotices.Add(notice);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Ok(ToResponse(notice));
    }

    public async Task<ApiResponse<CollectionNoticeResponse?>> FindOneAsync(int id, CancellationToken ct)
    {
        var notice = await _db.CollectionNotices.AsNoTracking()
            .Include(n => n.Resident)
            .FirstOrDefaultAsync(n => n.Id == id, ct);

        return notice is null
            ? ApiResponseFactory.NotFound<CollectionNoticeResponse>()
            : ApiResponseFactory.Create<CollectionNoticeResponse?>(HttpStatusCatalog.Ok, ToResponse(notice));
    }

    public async Task<ApiResponse<CollectionNoticeResponse?>> UpdateAsync(
        int id, UpdateCollectionRequest request, CurrentUser current, CancellationToken ct)
    {
        var notice = await _db.CollectionNotices.Include(n => n.Resident)
            .FirstOrDefaultAsync(n => n.Id == id, ct);

        if (notice is null) return ApiResponseFactory.NotFound<CollectionNoticeResponse>();

        if (request.NoticesType is { } type) notice.NoticesType = type;
        if (request.Year is { } year) notice.Year = year;
        if (request.Month is { } month) notice.Month = month;
        if (request.NoticeDate is { } noticeDate) notice.NoticeDate = noticeDate;
        if (request.FeeAmount is not null) notice.FeeAmount = request.FeeAmount;
        if (request.Status is { } status) notice.Status = status;
        if (request.ReceiptId is not null) notice.ReceiptId = request.ReceiptId;
        if (request.Note is not null) notice.Note = request.Note;
        if (request.Active is { } active) notice.Active = active;

        _audit.StampUpdate(notice, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Create<CollectionNoticeResponse?>(HttpStatusCatalog.Ok, ToResponse(notice));
    }

    public async Task<ApiResponse<object>> RemoveAsync(int id, CurrentUser current, CancellationToken ct)
    {
        var notice = await _db.CollectionNotices.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (notice is not null)
        {
            _audit.StampDelete(notice, current.UserId);
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponseFactory.Ok<object>(new { });
    }

    // ─── ตัวช่วยภายใน ───────────────────────────────────────────────────────

    /// <summary>
    /// โครงการของผู้ใช้ — หน้าลดหนี้ทั้งหมดผูกกับโครงการของผู้ล็อกอิน ไม่ได้รับ project_id จาก URL
    /// </summary>
    private async Task<int> ResolveProjectIdAsync(CurrentUser current, CancellationToken ct)
    {
        if (current.ProjectId is { } projectId) return projectId;

        var fromDb = await _db.Users.AsNoTracking()
            .Where(u => u.Id == current.UserId)
            .Select(u => u.ProjectId)
            .FirstOrDefaultAsync(ct);

        return fromDb ?? throw AppException.NotFound("ไม่พบโครงการของผู้ใช้งาน");
    }

    private IQueryable<CollectionNotice> BuildReduceQuery(int projectId, CollectionFilterQuery filter)
    {
        var query = _db.CollectionNotices.AsNoTracking()
            .Where(n => n.Resident.ProjectId == projectId);

        query = ApplyCommonFilter(query, filter);

        if (filter.PaymentStatus is { Count: > 0 } statuses)
        {
            query = query.Where(n => n.Payment != null && statuses.Contains(n.Payment.Status));
        }

        return query;
    }

    private static IQueryable<CollectionNotice> ApplyCommonFilter(
        IQueryable<CollectionNotice> query, CollectionFilterQuery filter)
    {
        query = query.WhereContains(filter.HouseNo, n => n.Resident.HouseNo);

        if (filter.InvNo is { Length: > 0 })
        {
            var pattern = $"%{filter.InvNo.Trim()}%";
            query = query.Where(n => n.Payment != null && n.Payment.InvNo != null
                                     && EF.Functions.ILike(n.Payment.InvNo, pattern));
        }

        if (filter.ResidentId is { } residentId) query = query.Where(n => n.ResidentId == residentId);
        if (filter.Month is { } month) query = query.Where(n => n.Month == month);
        if (filter.Year is { } year) query = query.Where(n => n.Year == year);
        if (filter.Status is { } status) query = query.Where(n => n.Status == status);
        if (filter.Active is { } active) query = query.Where(n => n.Active == active);

        return query;
    }

    /// <summary>รวมยอดค่าเบี้ยปรับและค่าทวงถามของทุกใบที่ผูกกับงวดเดียวกัน</summary>
    private async Task<(decimal? Fine, decimal? Collection, decimal? Total)> SummariseFeesAsync(
        int? paymentId, CancellationToken ct)
    {
        if (paymentId is null) return (null, null, null);

        var rows = await _db.CollectionNotices.AsNoTracking()
            .Where(n => n.PaymentId == paymentId)
            .Select(n => new { n.NoticesType, n.FeeAmount })
            .ToListAsync(ct);

        var fine = rows.Where(r => r.NoticesType == NoticeType.FINE).Sum(r => r.FeeAmount ?? 0m);
        var collection = rows.Where(r => r.NoticesType == NoticeType.COLLECTION).Sum(r => r.FeeAmount ?? 0m);

        return (fine, collection, fine + collection);
    }

    /// <summary>เติมยอดรวมต่องวดและชื่อผู้ลดหนี้ให้แต่ละแถว</summary>
    private async Task<IReadOnlyList<ReduceNoticeResponse>> EnrichAsync(
        IReadOnlyList<CollectionNotice> notices, CancellationToken ct)
    {
        if (notices.Count == 0) return [];

        var paymentIds = notices.Where(n => n.PaymentId is not null)
            .Select(n => n.PaymentId!.Value).Distinct().ToList();

        var feeRows = paymentIds.Count == 0
            ? []
            : await _db.CollectionNotices.AsNoTracking()
                .Where(n => n.PaymentId != null && paymentIds.Contains(n.PaymentId.Value))
                .Select(n => new { PaymentId = n.PaymentId!.Value, n.NoticesType, n.FeeAmount })
                .ToListAsync(ct);

        var feesByPayment = feeRows
            .GroupBy(r => r.PaymentId)
            .ToDictionary(
                g => g.Key,
                g => (
                    Fine: g.Where(r => r.NoticesType == NoticeType.FINE).Sum(r => r.FeeAmount ?? 0m),
                    Collection: g.Where(r => r.NoticesType == NoticeType.COLLECTION).Sum(r => r.FeeAmount ?? 0m)));

        var deductedByIds = notices.Where(n => n.DeductedBy is not null)
            .Select(n => n.DeductedBy!.Value).Distinct().ToList();

        var namesById = deductedByIds.Count == 0
            ? []
            : await _db.Users.AsNoTracking()
                .Where(u => deductedByIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return notices.Select(n =>
        {
            decimal? fine = null, collection = null, total = null;

            if (n.PaymentId is { } paymentId)
            {
                if (feesByPayment.TryGetValue(paymentId, out var fees))
                {
                    fine = fees.Fine;
                    collection = fees.Collection;
                }
                else
                {
                    fine = 0m;
                    collection = 0m;
                }

                total = fine + collection;
            }

            return new ReduceNoticeResponse
            {
                Id = n.Id,
                ResidentId = n.ResidentId,
                PaymentId = n.PaymentId,
                NoticesType = n.NoticesType,
                Year = n.Year,
                Month = n.Month,
                NoticeDate = n.NoticeDate,
                FeeAmount = n.FeeAmount,
                Status = n.Status,
                ReceiptId = n.ReceiptId,
                Note = n.Note,
                DeductedAt = n.DeductedAt,
                DeductedBy = n.DeductedBy,
                DeductedReason = n.DeductedReason,
                Active = n.Active,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt,
                Resident = ToBrief(n.Resident),
                FeeAmountFine = fine,
                FeeAmountCollection = collection,
                FeeAmountTotal = total,
                DeductedName = n.DeductedBy is { } by ? namesById.GetValueOrDefault(by) : null,
                Payment = n.Payment is null ? null : PaymentService.ToResponse(n.Payment),
            };
        }).ToList();
    }

    private static ResidentBrief? ToBrief(Resident? r) => r is null ? null : new ResidentBrief
    {
        Id = r.Id,
        ProjectId = r.ProjectId,
        HouseNo = r.HouseNo,
        FullName = r.FullName,
        Address = r.Address,
    };

    private static CollectionNoticeResponse ToResponse(CollectionNotice n) => new()
    {
        Id = n.Id,
        ResidentId = n.ResidentId,
        PaymentId = n.PaymentId,
        NoticesType = n.NoticesType,
        Year = n.Year,
        Month = n.Month,
        NoticeDate = n.NoticeDate,
        FeeAmount = n.FeeAmount,
        Status = n.Status,
        ReceiptId = n.ReceiptId,
        Note = n.Note,
        DeductedAt = n.DeductedAt,
        DeductedBy = n.DeductedBy,
        DeductedReason = n.DeductedReason,
        Active = n.Active,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt,
        Resident = ToBrief(n.Resident),
    };
}
