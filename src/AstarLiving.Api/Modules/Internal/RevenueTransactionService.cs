using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Entities;
using AstarLiving.Infrastructure.Auditing;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Infrastructure.Queries;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.Internal;

/// <summary>
/// ใบเสร็จรายรับอื่น ๆ — พอร์ตจาก src/modules/transactions/transactions.service.ts
///
/// ตารางนี้ถูกสร้างด้วย SQL เขียนมือ ไม่ใช่ TypeORM จึงมีจุดต่างจากตารางอื่น
/// เช่นคอลัมน์ audit เป็น timestamp ไม่มีโซนเวลา และมี unique index บน receipt_no
/// </summary>
public class RevenueTransactionService(
    AstarLivingDbContext db,
    AuditStamp audit,
    ThaiCalendar calendar)
{
    /// <summary>ระบบเดิมลองออกเลขใหม่ได้สูงสุด 100 ครั้งก่อนยอมแพ้</summary>
    private const int MaxReceiptNumberAttempts = 100;

    private static readonly SortMap<RevenueTransaction> Sort = new SortMap<RevenueTransaction>("id")
        .Add("id", t => t.Id)
        .Add("receipt_no", t => t.ReceiptNo)
        .Add("issue_date", t => t.IssueDate)
        .Add("payment_date", t => t.PaymentDate)
        .Add("total_amount", t => t.TotalAmount)
        .Add("active", t => t.Active)
        .Add("house_no", t => t.Resident.HouseNo)
        .Add("full_name", t => t.Resident.FullName)
        .Add("created_at", t => t.CreatedAt);

    private readonly AstarLivingDbContext _db = db;
    private readonly AuditStamp _audit = audit;
    private readonly ThaiCalendar _calendar = calendar;

    public async Task<ApiResponse<RevenueTransactionResponse>> CreateAsync(
        CreateRevenueTransactionRequest request, CurrentUser current, CancellationToken ct)
    {
        if (!await _db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct))
        {
            throw AppException.NotFound("ไม่พบโครงการ");
        }

        if (!await _db.Residents.AnyAsync(r => r.Id == request.ResidentId, ct))
        {
            throw AppException.NotFound("ไม่พบลูกบ้าน");
        }

        if (!await _db.PaymentMethods.AnyAsync(m => m.Id == request.PaymentMethodId, ct))
        {
            throw AppException.NotFound("ไม่พบวิธีการชำระเงิน");
        }

        // ยอดรวมคิดจากผลรวมของทุกบรรทัดเสมอ ไม่เชื่อค่าที่ client ส่งมา
        var calculatedTotal = request.PaymentDetail.Items.Sum(i => i.Total);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var receiptNo = request.ReceiptNo is { Length: > 0 } supplied
            ? await EnsureSuppliedNumberIsFreeAsync(supplied, ct)
            : await GenerateReceiptNumberAsync(request.ProjectId, ct);

        var entity = new RevenueTransaction
        {
            ProjectId = request.ProjectId,
            ReceiptNo = receiptNo,
            IssueDate = request.IssueDate,
            ResidentId = request.ResidentId,
            PaymentDetail = new RevenuePaymentDetail
            {
                Items = request.PaymentDetail.Items.Select(i => new RevenuePaymentItem
                {
                    ItemNo = i.ItemNo,
                    Description = i.Description,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    Total = i.Total,
                }).ToList(),
                GrandTotal = calculatedTotal,
            },
            TotalAmount = calculatedTotal,
            PaymentMethodId = request.PaymentMethodId,
            BankName = request.BankName,
            ChequeNo = request.ChequeNo,
            Note = request.Note,
            PaymentDate = request.PaymentDate,
            Active = true,
        };
        _audit.StampCreate(entity, current.UserId);

        _db.RevenueTransactions.Add(entity);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw AppException.BadRequest($"เลขที่ใบเสร็จ \"{receiptNo}\" ถูกใช้งานแล้ว");
        }

        await transaction.CommitAsync(ct);

        return ApiResponseFactory.Ok(ToResponse(entity));
    }

    public async Task<ApiResponse<IReadOnlyList<RevenueTransactionResponse>>> FindAllByProjectAsync(
        int projectId, ReceiptFilterQuery filter, CancellationToken ct)
    {
        var query = _db.RevenueTransactions.AsNoTracking()
            .Include(t => t.Resident)
            .Where(t => t.ProjectId == projectId)
            .WhereContains(filter.ReceiptNo, t => t.ReceiptNo)
            .WhereContains(filter.HouseNo, t => t.Resident.HouseNo);

        if (filter.ResidentId is { } residentId) query = query.Where(t => t.ResidentId == residentId);
        if (filter.Active is { } active) query = query.Where(t => t.Active == active);
        if (filter.IssueStartDate is { } start) query = query.Where(t => t.IssueDate >= start);
        if (filter.IssueEndDate is { } end) query = query.Where(t => t.IssueDate <= end);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok,
            (IReadOnlyList<RevenueTransactionResponse>)page.Items.Select(ToResponse).ToList(),
            page.PageInformation);
    }

    /// <summary>
    /// GET /api/transactions/:id
    /// ระบบเดิมเส้นทางนี้เป็น stub ที่คืนสตริงเฉย ๆ และยังบังเส้นทางพิมพ์ PDF อยู่ด้วย
    /// ระบบใหม่ทำให้ใช้งานได้จริง
    /// </summary>
    public async Task<ApiResponse<RevenueTransactionResponse?>> FindOneAsync(int id, CancellationToken ct)
    {
        var entity = await _db.RevenueTransactions.AsNoTracking()
            .Include(t => t.Resident)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        return entity is null
            ? ApiResponseFactory.NotFound<RevenueTransactionResponse>()
            : ApiResponseFactory.Create<RevenueTransactionResponse?>(HttpStatusCatalog.Ok, ToResponse(entity));
    }

    /// <summary>ยกเลิกใบเสร็จรายรับ — ไม่มีการคืนสถานะอะไรเหมือนใบเสร็จค่าส่วนกลาง</summary>
    public async Task<ApiResponse<RevenueTransactionResponse?>> RejectAsync(
        int id, RejectRevenueTransactionRequest request, CurrentUser current, CancellationToken ct)
    {
        var entity = await _db.RevenueTransactions.Include(t => t.Resident)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (entity is null) return ApiResponseFactory.NotFound<RevenueTransactionResponse>();

        entity.Active = false;
        entity.RejectedReason = request.RejectedReason;
        _audit.StampUpdate(entity, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Create<RevenueTransactionResponse?>(HttpStatusCatalog.Ok, ToResponse(entity));
    }

    public async Task<ApiResponse<DeleteRevenueResult>> RemoveAsync(
        int id, CurrentUser current, CancellationToken ct)
    {
        var entity = await _db.RevenueTransactions.FirstOrDefaultAsync(t => t.Id == id, ct)
                     ?? throw AppException.NotFound("ไม่พบข้อมูลรายการรายรับ");

        _audit.StampDelete(entity, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Ok(new DeleteRevenueResult { Message = "ลบข้อมูลรายการรายรับสำเร็จ" });
    }

    /// <summary>
    /// ออกเลขใบเสร็จรายรับ
    ///
    /// ระบบเดิมวนหาเลขที่ไม่ซ้ำได้ถึง 100 ครั้งเพราะไม่มีการล็อก ระบบใหม่ใช้ advisory lock
    /// ทำให้ปกติได้เลขถูกต้องตั้งแต่ครั้งแรก แต่ยังคงวนสำรองไว้เผื่อมีข้อมูลเก่าที่เลขซ้อนกันอยู่
    /// </summary>
    private async Task<string> GenerateReceiptNumberAsync(int projectId, CancellationToken ct)
    {
        var prefix = _calendar.RunningNumberPrefix();
        var dayKey = int.Parse(prefix, System.Globalization.CultureInfo.InvariantCulture);

        // ฟังก์ชันแบบสองกุญแจของ PostgreSQL รับเฉพาะ integer ไม่มีแบบ bigint สองตัว
        await _db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({projectId}::int4, {dayKey}::int4)", ct);

        var last = await _db.RevenueTransactions.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.ReceiptNo.StartsWith(prefix))
            .OrderByDescending(t => t.ReceiptNo)
            .Select(t => t.ReceiptNo)
            .FirstOrDefaultAsync(ct);

        var next = RunningNumber.NextSequence(last);

        for (var attempt = 0; attempt < MaxReceiptNumberAttempts; attempt++)
        {
            var candidate = RunningNumber.Format(prefix, next + attempt);

            // ต้องนับแถวที่ถูกลบแบบ soft delete ด้วย เพราะ unique index ยังคุมอยู่
            var taken = await _db.RevenueTransactions.IgnoreQueryFilters()
                .AnyAsync(t => t.ReceiptNo == candidate, ct);

            if (!taken) return candidate;
        }

        throw AppException.BadRequest("ไม่สามารถสร้างเลขที่ใบเสร็จที่ไม่ซ้ำได้ กรุณาลองใหม่อีกครั้ง");
    }

    private async Task<string> EnsureSuppliedNumberIsFreeAsync(string receiptNo, CancellationToken ct)
    {
        var taken = await _db.RevenueTransactions.IgnoreQueryFilters()
            .AnyAsync(t => t.ReceiptNo == receiptNo, ct);

        if (taken) throw AppException.BadRequest($"เลขที่ใบเสร็จ \"{receiptNo}\" ถูกใช้งานแล้ว");

        return receiptNo;
    }

    /// <summary>23505 คือรหัสของ PostgreSQL เมื่อชน unique constraint</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private static RevenueTransactionResponse ToResponse(RevenueTransaction t) => new()
    {
        Id = t.Id,
        ProjectId = t.ProjectId,
        ReceiptNo = t.ReceiptNo,
        IssueDate = t.IssueDate,
        ResidentId = t.ResidentId,
        PaymentDetail = new RevenuePaymentDetailContract
        {
            Items = t.PaymentDetail.Items.Select(i => new RevenueItemContract
            {
                ItemNo = i.ItemNo,
                Description = i.Description,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Total = i.Total,
            }).ToList(),
            GrandTotal = t.PaymentDetail.GrandTotal,
        },
        TotalAmount = t.TotalAmount,
        PaymentMethodId = t.PaymentMethodId,
        BankName = t.BankName,
        ChequeNo = t.ChequeNo,
        Note = t.Note,
        PaymentDate = t.PaymentDate,
        Active = t.Active,
        RejectedReason = t.RejectedReason,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        Resident = t.Resident is null ? null : new ResidentBrief
        {
            Id = t.Resident.Id,
            ProjectId = t.Resident.ProjectId,
            HouseNo = t.Resident.HouseNo,
            FullName = t.Resident.FullName,
            Address = t.Resident.Address,
        },
    };
}
