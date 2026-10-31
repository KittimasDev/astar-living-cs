using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Common;
using AstarLiving.Domain.Entities;
using AstarLiving.Domain.Enums;
using AstarLiving.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.Internal;

/// <summary>
/// บริบทของงวดที่จะออกใบแจ้งหนี้ — ตรงกับ getInvoicePeriodContext() ของเดิม
/// </summary>
/// <param name="Period">งวดที่จะออก พร้อมสถานะว่าออกได้หรือไม่</param>
/// <param name="PrepaidResidentIds">บ้านที่ชำระล่วงหน้าไว้แล้วในงวดนี้</param>
/// <param name="IncludedResidentIds">
/// รายชื่อบ้านที่ต้องออกใบให้ — null หมายถึง "ทุกบ้านในโครงการ"
/// </param>
public sealed record InvoicePeriodContext(
    InvoicePeriodResult Period,
    IReadOnlySet<int> PrepaidResidentIds,
    IReadOnlyList<int>? IncludedResidentIds);

/// <summary>
/// คิวรีที่ใช้ร่วมกันระหว่างหน้าพรีวิว การออกใบแจ้งหนี้ และการสร้าง PDF
/// แยกออกมาเพราะตรรกะพวกนี้ละเอียดอ่อนและถูกเรียกจากหลายที่
/// </summary>
public class PaymentQueries(AstarLivingDbContext db, ThaiCalendar calendar)
{
    private readonly AstarLivingDbContext _db = db;
    private readonly ThaiCalendar _calendar = calendar;

    /// <summary>
    /// findPriorNotPayDebts() — งวดค้างก่อนหน้างวดที่ระบุ
    ///
    /// เงื่อนไขของระบบเดิมคือ status = NOT_PAY เท่านั้น (ไม่รวม PARTIAL_PAY)
    /// และนับเฉพาะงวดที่อยู่ก่อนงวดอ้างอิง โดยตัดตัวมันเองออกเมื่อระบุ excludePaymentId
    /// </summary>
    public async Task<List<DebtLine>> FindPriorNotPayDebtsAsync(
        int projectId, int residentId, int year, int month, int? excludePaymentId, CancellationToken ct)
    {
        var rows = await _db.Payments.AsNoTracking()
            .Where(p => p.Resident.ProjectId == projectId
                        && p.ResidentId == residentId
                        && p.Status == PaymentStatus.NOT_PAY
                        && (p.Year < year || (p.Year == year && p.Month < month))
                        && (excludePaymentId == null || p.Id != excludePaymentId))
            .Select(p => new { p.Year, p.Month, p.AmountDue, p.Fine, p.CollectionFee })
            .ToListAsync(ct);

        return rows
            .Select(r => new DebtLine(r.Year, r.Month, r.AmountDue, r.Fine ?? 0m, r.CollectionFee ?? 0m))
            .ToList();
    }

    /// <summary>
    /// resolveIssuePeriod() — หางวดที่จะออก แล้วตรวจว่าต้องเข้าโหมดเติมใบย้อนหลังหรือไม่
    /// </summary>
    public async Task<InvoicePeriodResult> ResolveIssuePeriodAsync(int projectId, CancellationToken ct)
    {
        var now = _calendar.CurrentPeriod;

        // งวดล่าสุดที่เคยออกใบปกติ (ไม่นับงวดชำระล่วงหน้า)
        var latestRow = await _db.Payments.AsNoTracking()
            .Where(p => p.Resident.ProjectId == projectId && !p.Advance)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new { p.Year, p.Month })
            .FirstOrDefaultAsync(ct);

        MonthYear? latest = latestRow is null ? null : new MonthYear(latestRow.Month, latestRow.Year);
        var period = InvoicePeriodResolver.Resolve(projectId, latest, now);

        if (latest is null) return period;

        // ตรวจว่ามีบ้านจ่ายล่วงหน้าที่ยังค้างหนี้และยังไม่มีใบปกติของงวดล่าสุดหรือไม่
        var (periodPayments, priorDebtIds) =
            await LoadPeriodPaymentsAndPriorDebtAsync(projectId, latest.Value.Month, latest.Value.Year, ct);

        var missing = InvoiceInclusion.GetMissingPrepaidDebtResidentIds(periodPayments, priorDebtIds);

        return InvoicePeriodResolver.ApplySupplement(period, now, missing.Count > 0);
    }

    /// <summary>getInvoicePeriodContext() — รวมงวด บ้านที่จ่ายล่วงหน้า และรายชื่อบ้านที่ต้องออกใบ</summary>
    public async Task<InvoicePeriodContext> GetInvoicePeriodContextAsync(int projectId, CancellationToken ct)
    {
        var period = await ResolveIssuePeriodAsync(projectId, ct);

        var (periodPayments, priorDebtIds) =
            await LoadPeriodPaymentsAndPriorDebtAsync(projectId, period.Month, period.Year, ct);

        var prepaidIds = InvoiceInclusion.GetPrepaidResidentIds(periodPayments);
        var excludedIds = InvoiceInclusion.GetExcludedResidentIds(periodPayments, priorDebtIds);
        var missingPrepaidDebtIds = InvoiceInclusion.GetMissingPrepaidDebtResidentIds(periodPayments, priorDebtIds);

        IReadOnlyList<int>? included = null;

        if (period.IsSupplement)
        {
            // โหมดเติมใบ: ออกเฉพาะบ้านที่ขาด ถ้าไม่มีเลยให้ได้ผลลัพธ์ว่าง
            included = missingPrepaidDebtIds.Count > 0 ? missingPrepaidDebtIds : [0];
        }
        else if (excludedIds.Count > 0)
        {
            var excluded = excludedIds.ToHashSet();
            included = await _db.Residents.AsNoTracking()
                .Where(r => r.ProjectId == projectId && !excluded.Contains(r.Id))
                .Select(r => r.Id)
                .ToListAsync(ct);
        }

        return new InvoicePeriodContext(period, prepaidIds, included);
    }

    /// <summary>
    /// loadPeriodPaymentsAndPriorDebt() — แถวของงวดนี้ และรายชื่อบ้านที่มีหนี้ค้างก่อนหน้า
    /// บ้านจะถือว่ามีหนี้ก็ต่อเมื่อแถวนั้นมียอดมากกว่าศูนย์ (ใบแจ้งเพื่อทราบไม่นับ)
    /// </summary>
    private async Task<(List<PeriodPaymentRow> PeriodPayments, HashSet<int> PriorDebtIds)>
        LoadPeriodPaymentsAndPriorDebtAsync(int projectId, int month, int year, CancellationToken ct)
    {
        var periodPayments = await _db.Payments.AsNoTracking()
            .Where(p => p.Resident.ProjectId == projectId && p.Month == month && p.Year == year)
            .Select(p => new { p.ResidentId, p.Advance })
            .ToListAsync(ct);

        var priorDebtRows = await _db.Payments.AsNoTracking()
            .Where(p => p.Resident.ProjectId == projectId
                        && p.Status != PaymentStatus.PAID
                        && p.Status != PaymentStatus.CANCELLED
                        && (p.Year < year || (p.Year == year && p.Month < month)))
            .Select(p => new { p.ResidentId, p.AmountDue, p.Fine, p.CollectionFee })
            .ToListAsync(ct);

        var priorDebtIds = priorDebtRows
            .Where(p => !InvoiceInclusion.IsZeroCharge(p.AmountDue, p.Fine, p.CollectionFee))
            .Select(p => p.ResidentId)
            .ToHashSet();

        return (
            periodPayments.Select(p => new PeriodPaymentRow(p.ResidentId, p.Advance)).ToList(),
            priorDebtIds);
    }

    /// <summary>
    /// getNextRunningNumber() — เลขเอกสารถัดไปของโครงการสำหรับวันนี้
    ///
    /// ระบบเดิมอ่านเลขล่าสุดแล้วบวกหนึ่งโดยไม่มีการล็อก ทำให้ออกพร้อมกันแล้วเลขชนกันได้
    /// ระบบใหม่กันด้วย advisory lock ต่อ (โครงการ, วันที่) ภายในทรานแซกชันเดียวกับการบันทึก
    /// รูปแบบเลขยังเหมือนเดิมทุกประการ
    /// </summary>
    public async Task<string> NextInvoiceNumberAsync(int projectId, CancellationToken ct)
    {
        var prefix = _calendar.RunningNumberPrefix();
        await AcquireDocumentLockAsync(projectId, prefix, ct);

        var last = await _db.Payments.AsNoTracking()
            .Where(p => p.Resident.ProjectId == projectId
                        && p.InvNo != null
                        && p.InvNo.StartsWith(prefix))
            .OrderByDescending(p => p.InvNo)
            .Select(p => p.InvNo)
            .FirstOrDefaultAsync(ct);

        return RunningNumber.Next(prefix, last);
    }

    /// <summary>เลขใบเสร็จถัดไป ใช้กลไกเดียวกับเลขใบแจ้งหนี้</summary>
    public async Task<string> NextReceiptNumberAsync(int projectId, CancellationToken ct)
    {
        var prefix = _calendar.RunningNumberPrefix();
        await AcquireDocumentLockAsync(projectId, prefix, ct);

        var last = await _db.Receipts.AsNoTracking()
            .Where(r => r.ProjectId == projectId
                        && r.ReceiptNo != null
                        && r.ReceiptNo.StartsWith(prefix))
            .OrderByDescending(r => r.ReceiptNo)
            .Select(r => r.ReceiptNo)
            .FirstOrDefaultAsync(ct);

        return RunningNumber.Next(prefix, last);
    }

    /// <summary>
    /// ล็อกระดับทรานแซกชันของ PostgreSQL กันการออกเลขเอกสารชนกัน
    /// ปลดล็อกอัตโนมัติเมื่อทรานแซกชันจบ จึงไม่ค้างแม้โปรแกรมพัง
    /// </summary>
    private async Task AcquireDocumentLockAsync(int projectId, string prefix, CancellationToken ct)
    {
        // ใช้ prefix ของวัน (YYMMDD) เป็นกุญแจที่สอง เพื่อให้ล็อกแคบที่สุดเท่าที่จำเป็น
        //
        // PostgreSQL มีฟังก์ชันนี้สองแบบเท่านั้น: รับ bigint ตัวเดียว หรือรับ integer สองตัว
        // ไม่มีแบบรับ bigint สองตัว การแปลงเป็น bigint จึงทำให้หาฟังก์ชันไม่เจอและพังทั้งเส้นทาง
        var dayKey = int.Parse(prefix, System.Globalization.CultureInfo.InvariantCulture);

        await _db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({projectId}::int4, {dayKey}::int4)", ct);
    }
}
