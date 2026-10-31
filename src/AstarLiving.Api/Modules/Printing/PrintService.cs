using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Api.Modules.External;
using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Enums;
using AstarLiving.Infrastructure.Auditing;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Pdf.Documents;
using AstarLiving.Pdf.Models;
using AstarLiving.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace AstarLiving.Api.Modules.Printing;

/// <summary>คำขอสร้าง PDF ใบแจ้งหนี้ — ตรงกับ GenerateInvoicePdfDto ของเดิม</summary>
public class GenerateInvoicePdfRequest
{
    public List<int>? ResidentIds { get; set; }
    public int? Month { get; set; }
    public int? Year { get; set; }
    public int? PaymentId { get; set; }
    public List<int>? PaymentIds { get; set; }
    public string? InvNo { get; set; }

    /// <summary>รูปแบบ YYYYMMDD ปี พ.ศ. ถ้าระบุมาจะใช้แทน month กับ year</summary>
    public string? Date { get; set; }
}

/// <summary>
/// สร้างไฟล์ PDF ของเอกสารทุกชนิด
/// พอร์ตจากเส้นทาง print/pdf ที่กระจายอยู่ในหลายโมดูลของระบบเดิม
/// </summary>
public class PrintService(
    AstarLivingDbContext db,
    DocumentDataBuilder builder,
    SummaryExpensesService summaryExpenses,
    AuditStamp audit,
    ThaiCalendar calendar)
{
    private readonly AstarLivingDbContext _db = db;
    private readonly DocumentDataBuilder _builder = builder;
    private readonly SummaryExpensesService _summaryExpenses = summaryExpenses;
    private readonly AuditStamp _audit = audit;
    private readonly ThaiCalendar _calendar = calendar;

    /// <summary>
    /// ใบแจ้งหนี้ — เลือกงวดตามลำดับความเฉพาะเจาะจง
    /// รหัสงวดที่ระบุตรง ๆ มาก่อน ตามด้วยเลขที่ใบแจ้งหนี้ แล้วจึงเป็นเดือน/ปี
    /// </summary>
    public async Task<byte[]> GenerateInvoicePdfAsync(
        int projectId, GenerateInvoicePdfRequest request, CurrentUser current, CancellationToken ct)
    {
        var query = _db.Payments.AsNoTracking()
            .Include(p => p.Resident)
            .Where(p => p.Resident.ProjectId == projectId && !p.Advance);

        var explicitIds = new List<int>();
        if (request.PaymentId is { } single) explicitIds.Add(single);
        if (request.PaymentIds is { Count: > 0 } many) explicitIds.AddRange(many);

        if (explicitIds.Count > 0)
        {
            query = query.Where(p => explicitIds.Contains(p.Id));
        }
        else if (request.InvNo is { Length: > 0 } invNo)
        {
            query = query.Where(p => p.InvNo == invNo);
        }
        else
        {
            // ไม่ได้ระบุงวดเจาะจง จึงพิมพ์เฉพาะใบที่ยังไม่ถูกชำระ เหมือนระบบเดิม
            query = query.Where(p => p.Status == PaymentStatus.NOT_PAY);

            var period = ResolvePeriod(request);
            if (period is { } resolved)
            {
                query = query.Where(p => p.Month == resolved.Month && p.Year == resolved.Year);
            }

            if (request.ResidentIds is { Count: > 0 } residentIds)
            {
                query = query.Where(p => residentIds.Contains(p.ResidentId));
            }
        }

        var payments = await query.OrderBy(p => p.ResidentId).ThenBy(p => p.Id).ToListAsync(ct);

        if (payments.Count == 0) throw AppException.BadRequest("ไม่พบข้อมูลใบแจ้งหนี้");

        var project = await _builder.GetProjectInfoAsync(projectId, ct);
        var sign = await _builder.GetSignInfoAsync(current.UserId, ct);
        var logo = _builder.LoadLogo(project);

        var documents = await _builder.BuildInvoicesAsync(
            projectId, payments, _audit.ThaiDocumentDate, ct);

        return new InvoiceDocument(project, documents, sign, logo).GeneratePdf();
    }

    /// <summary>ใบแจ้งหนี้ล่วงหน้า — ทุกงวดที่ใช้ inv_no เดียวกันรวมเป็นใบเดียว</summary>
    public async Task<byte[]> GenerateAdvanceInvoicePdfAsync(
        int projectId, string invNo, CurrentUser current, CancellationToken ct)
    {
        var payments = await _db.Payments.AsNoTracking()
            .Include(p => p.Resident)
            .Include(p => p.Receipt).ThenInclude(r => r!.PaymentMethod)
            .Where(p => p.Resident.ProjectId == projectId && p.InvNo == invNo && p.Advance)
            .OrderBy(p => p.Month)
            .ToListAsync(ct);

        if (payments.Count == 0) throw AppException.BadRequest("ไม่พบข้อมูลใบแจ้งหนี้");

        var project = await _builder.GetProjectInfoAsync(projectId, ct);
        var sign = await _builder.GetSignInfoAsync(current.UserId, ct);
        var logo = _builder.LoadLogo(project);

        var first = payments[0];
        var resident = first.Resident;
        var receipt = first.Receipt;

        var months = payments.Select(p => (int)p.Month).ToList();
        var unitPrice = first.AmountDue;

        var document = new InvoiceDocumentModel
        {
            InvoiceNo = invNo,
            CustomerName = resident?.FullName ?? string.Empty,
            HouseNo = resident?.HouseNo ?? string.Empty,
            Address = resident?.Address ?? string.Empty,
            Date = ThaiCalendar.ToThaiDate(_audit.ThaiDocumentDate),
            Month = $"{first.Month}/{first.Year}",
            Items =
            [
                new DocumentItem
                {
                    Name = ReceiptItemBuilder.BuildThaiMonthRange(months, first.Year),
                    Unit = payments.Count,
                    Price = unitPrice,
                    Total = unitPrice * payments.Count,
                },
                // ระบบเดิมเติมแถวว่างสองแถวเพื่อให้ความสูงของตารางเท่าใบแจ้งหนี้ปกติ
                new DocumentItem(),
                new DocumentItem(),
            ],
            DebtItems = [" ", " ", " "],
            Subtotal = unitPrice * payments.Count,
            Debt = 0m,
            Total = unitPrice * payments.Count,
            Payment = new PaymentInfo
            {
                Name = receipt?.PaymentMethod?.Name,
                IssueDate = ThaiCalendar.ToThaiDate(receipt?.IssueDate ?? first.PaymentDate ?? _audit.ThaiDocumentDate),
                BankName = receipt?.BankName,
                ChequeNo = receipt?.ChequeNo,
            },
        };

        return new InvoiceDocument(project, [document], sign, logo, isAdvance: true).GeneratePdf();
    }

    /// <summary>ใบเสร็จรับเงินค่าส่วนกลาง</summary>
    public async Task<byte[]> GenerateReceiptPdfAsync(int receiptId, CurrentUser current, CancellationToken ct)
    {
        var receipt = await _db.Receipts.AsNoTracking()
            .Include(r => r.Resident)
            .FirstOrDefaultAsync(r => r.Id == receiptId, ct)
            ?? throw AppException.BadRequest("ไม่พบข้อมูลใบเสร็จ");

        // ระบบเดิมใช้โครงการของผู้ใช้ก่อน ถ้าไม่มีจึงใช้โครงการของลูกบ้าน
        var projectId = current.ProjectId ?? receipt.Resident?.ProjectId ?? receipt.ProjectId;

        var project = await _builder.GetProjectInfoAsync(projectId, ct);
        var sign = await _builder.GetSignInfoAsync(current.UserId, ct);
        var logo = _builder.LoadLogo(project);

        var document = await _builder.BuildReceiptAsync(receipt, ct);

        return new ReceiptDocument(project, [document], sign, logo).GeneratePdf();
    }

    /// <summary>ใบเสร็จรายรับอื่น ๆ</summary>
    public async Task<byte[]> GenerateRevenueReceiptPdfAsync(
        int transactionId, CurrentUser current, CancellationToken ct)
    {
        var transaction = await _db.RevenueTransactions.AsNoTracking()
            .Include(t => t.Resident)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw AppException.BadRequest("ไม่พบข้อมูลรายการรายรับ");

        var project = await _builder.GetProjectInfoAsync(transaction.ProjectId, ct);
        var sign = await _builder.GetSignInfoAsync(current.UserId, ct);
        var logo = _builder.LoadLogo(project);

        var document = await _builder.BuildRevenueReceiptAsync(transaction, ct);

        return new ReceiptDocument(project, [document], sign, logo, ReceiptDocument.RevenueTitle).GeneratePdf();
    }

    /// <summary>ใบทวงถาม — รับได้ทั้งใบเดียวและหลายใบในชุดเดียว</summary>
    public async Task<byte[]> GenerateCollectionPdfAsync(
        IReadOnlyList<int> collectionIds, CurrentUser current, CancellationToken ct)
    {
        var notices = await _db.CollectionNotices.AsNoTracking()
            .Include(n => n.Resident)
            // ต้องโหลดงวดมาด้วย ไม่อย่างนั้นคอลัมน์เลขที่ใบแจ้งหนี้จะว่างทุกใบ
            .Include(n => n.Payment)
            .Where(n => collectionIds.Contains(n.Id))
            .OrderBy(n => n.Id)
            .ToListAsync(ct);

        if (notices.Count == 0) throw AppException.BadRequest("ไม่พบข้อมูลสำหรับสร้างใบทวงถาม");

        var projectId = notices[0].Resident?.ProjectId ?? current.RequireProjectId();

        var project = await _builder.GetProjectInfoAsync(projectId, ct);
        var sign = await _builder.GetSignInfoAsync(current.UserId, ct);
        var logo = _builder.LoadLogo(project);

        var documents = await _builder.BuildCollectionNoticesAsync(notices, _audit.ThaiDocumentDate, ct);

        return new CollectionDocument(project, documents, sign, logo).GeneratePdf();
    }

    /// <summary>สรุปค่าใช้จ่ายทั้งโครงการ</summary>
    /// <summary>
    /// บันทึกตารางรายงานเป็นไฟล์ Excel
    ///
    /// ใช้คำขอชุดเดียวกับการพิมพ์ PDF เพราะทั้งสองอย่างคือ "สิ่งที่หน้าจอกำลังแสดง"
    /// ต่างกันแค่รูปแบบไฟล์ปลายทาง
    /// </summary>
    public async Task<byte[]> GenerateReportWorkbookAsync(
        ReportPrintRequest request, CurrentUser current, CancellationToken ct)
    {
        if (request.Columns.Count == 0) throw AppException.BadRequest("ไม่พบคอลัมน์ของรายงาน");
        if (request.Rows.Count == 0) throw AppException.BadRequest("ไม่มีข้อมูลให้บันทึก");

        var projectId = current.RequireProjectId();

        var projectName = await _db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var now = _calendar.NowBangkok;
        var buddhistYear = now.Year + 543;

        return Modules.External.ReportWorkbook.Build(
            request,
            projectName,
            $"ประจำปี พ.ศ. {buddhistYear}",
            $"ณ วันที่ {now.Day} {ThaiMonths.GetFull(now.Month)} {buddhistYear}");
    }

    /// <summary>
    /// พิมพ์ตารางรายงานตามที่หน้าจอกำลังแสดงอยู่
    ///
    /// ระบบเดิมสร้างไฟล์นี้ที่เบราว์เซอร์ด้วย jsPDF จากตารางบนหน้าจอโดยตรง
    /// ระบบใหม่ย้ายมาสร้างที่เครื่องแม่ แต่ยังรับหัวตารางและแถวจากหน้าจอเหมือนเดิม
    /// เพราะรายงานแต่ละชนิดมีคอลัมน์ไม่เหมือนกัน และหน้าจอเป็นฝ่ายรู้ว่าผู้ใช้ดูอะไรอยู่
    /// </summary>
    public async Task<byte[]> GenerateReportPdfAsync(
        ReportPrintRequest request, CurrentUser current, CancellationToken ct)
    {
        if (request.Columns.Count == 0) throw AppException.BadRequest("ไม่พบคอลัมน์ของรายงาน");
        if (request.Rows.Count == 0) throw AppException.BadRequest("ไม่มีข้อมูลให้พิมพ์");

        var projectId = current.RequireProjectId();

        var projectName = await _db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        // ใช้เวลาไทยชุดเดียวกับที่ใช้ออกเลขเอกสาร เพื่อไม่ให้วันที่บนรายงานเพี้ยนข้ามวัน
        var now = _calendar.NowBangkok;
        var buddhistYear = now.Year + 543;

        var model = new ReportDocumentModel
        {
            ProjectName = projectName,
            ReportTitle = $"{request.ReportLabel} ประจำปี พ.ศ. {buddhistYear}",
            AsOfDate = $"ณ วันที่ {now.Day} {ThaiMonths.GetFull(now.Month)} {buddhistYear}",
            PrintedAt =
                $"{now.Day:00} {ThaiMonths.GetAbbr(now.Month)} {buddhistYear} เวลา {now:HH:mm} น.",
            Columns = request.Columns
                .Select(c => new ReportDocumentColumn
                {
                    Header = c.Header,
                    RelativeWidth = c.RelativeWidth,
                    Alignment = c.Alignment switch
                    {
                        ReportPrintAlignment.Right => ReportColumnAlignment.Right,
                        ReportPrintAlignment.Center => ReportColumnAlignment.Center,
                        _ => ReportColumnAlignment.Left,
                    },
                })
                .ToList(),
            Rows = request.Rows.Select(r => (IReadOnlyList<string>)r).ToList(),
        };

        return new ReportDocument(model).GeneratePdf();
    }

    public async Task<byte[]> GenerateSummaryExpensesPdfAsync(
        SummaryExpensesFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        var projectId = current.RequireProjectId();
        var data = await _summaryExpenses.GetSummaryAsync(
            new SummaryExpensesFilterQuery
            {
                Month = filter.Month,
                Year = filter.Year,
                ResidentId = filter.ResidentId,
                NoLimit = true,
            }, current, ct);

        var body = data.Body ?? new SummaryExpensesResponse();

        if (body.ByHouse.Count == 0) throw AppException.BadRequest("ไม่พบข้อมูลค้างชำระ");

        var fineRate = await _db.Settings.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Id)
            .Select(s => s.FineRate)
            .FirstOrDefaultAsync(ct);

        var project = await _builder.GetProjectInfoAsync(projectId, ct);
        var logo = _builder.LoadLogo(project);

        var model = new SummaryExpensesDocumentModel
        {
            PeriodLabel = SummaryExpensesCalculator.BuildPeriodLabel(
                filter.Year?.ToString(), filter.Month?.ToString()),
            FineColumnLabel = SummaryExpensesCalculator.FineColumnLabel(fineRate),
            Groups = body.ByHouse.Select(h => new SummaryExpenseGroup
            {
                HouseNo = h.HouseNo,
                FullName = h.FullName,
                Lines =
                [
                    new SummaryExpenseLine
                    {
                        Period = $"{h.PaymentCount} งวด",
                        CommonFee = h.CommonFee,
                        Fine = h.Fine,
                        CollectionFee = h.CollectionFee,
                        Total = h.Total,
                    },
                ],
                Total = h.Total,
            }).ToList(),
            GrandTotal = body.Total.Total,
            TotalCommonFee = body.Total.CommonFee,
            TotalFine = body.Total.Fine,
            TotalCollectionFee = body.Total.CollectionFee,
            Scope = SummaryExpenseScope.Project,
        };

        return new SummaryExpensesDocument(project, model, logo).GeneratePdf();
    }

    /// <summary>สรุปค่าใช้จ่ายของบ้านหลังเดียว แยกตามปีและเดือน</summary>
    /// <summary>
    /// ไฟล์ Excel ของรายงานค้างชำระแจ้งลูกบ้าน
    ///
    /// แยกออกมาจากเส้นทาง reports/xlsx ทั่วไป เพราะแท็บนี้ออกไฟล์สองชีต
    /// และต้องใช้ข้อมูลที่ยังแบ่งกลุ่มตามปีอยู่ ไม่ใช่ตารางแบนที่หน้าจอแสดง
    /// </summary>
    public async Task<byte[]> GenerateArrearsResidentWorkbookAsync(
        int residentId, SummaryExpensesFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        var projectId = current.RequireProjectId();

        var data = await _summaryExpenses.GetByResidentAsync(residentId, filter, current, ct);
        var body = data.Body ?? throw AppException.BadRequest("ไม่พบข้อมูลค้างชำระ");

        var projectName = await _db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var now = _calendar.NowBangkok;

        return Modules.External.ArrearsResidentWorkbook.Build(
            body,
            projectName,
            $"ข้อมูล ณ วันที่ {now:dd/MM/}{now.Year + 543}");
    }

    public async Task<byte[]> GenerateSummaryExpensesByResidentPdfAsync(
        int residentId, SummaryExpensesFilterQuery filter, CurrentUser current, CancellationToken ct)
    {
        var projectId = current.RequireProjectId();

        var data = await _summaryExpenses.GetByResidentAsync(residentId, filter, current, ct);
        var body = data.Body ?? throw AppException.BadRequest("ไม่พบข้อมูลค้างชำระ");

        var fineRate = await _db.Settings.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Id)
            .Select(s => s.FineRate)
            .FirstOrDefaultAsync(ct);

        var project = await _builder.GetProjectInfoAsync(projectId, ct);
        var logo = _builder.LoadLogo(project);

        var model = new SummaryExpensesDocumentModel
        {
            PeriodLabel = SummaryExpensesCalculator.BuildPeriodLabel(
                filter.Year?.ToString(), filter.Month?.ToString()),
            FineColumnLabel = SummaryExpensesCalculator.FineColumnLabel(fineRate),
            Groups =
            [
                new SummaryExpenseGroup
                {
                    HouseNo = body.HouseNo,
                    FullName = body.FullName,
                    Lines = body.ByYear
                        .SelectMany(year => year.Months.Select(month => new SummaryExpenseLine
                        {
                            Period = ThaiCalendar.FormatThaiMonthYear(month.Month, month.Year),
                            Year = month.Year,
                            MonthLabel = ThaiMonths.GetAbbr(month.Month),
                            InvoiceNo = month.InvNo ?? string.Empty,
                            CommonFee = month.CommonFee,
                            Fine = month.Fine,
                            CollectionFee = month.CollectionFee,
                            Total = month.Total,
                        }))
                        .ToList(),
                    Total = body.Total.Total,
                },
            ],
            GrandTotal = body.Total.Total,
            TotalCommonFee = body.Total.CommonFee,
            TotalFine = body.Total.Fine,
            TotalCollectionFee = body.Total.CollectionFee,
            Scope = SummaryExpenseScope.Resident,
        };

        return new SummaryExpensesDocument(project, model, logo).GeneratePdf();
    }

    /// <summary>
    /// ระบบเดิมเมื่อส่ง date มาจะแยกปีและเดือนจากสตริงโดยตรงโดย "ไม่" ลบ 543
    /// ซึ่งถูกต้องแล้วเพราะคอลัมน์ year เก็บเป็น พ.ศ. อยู่
    /// </summary>
    private static Domain.Common.MonthYear? ResolvePeriod(GenerateInvoicePdfRequest request)
    {
        if (ThaiCalendar.ParseThaiDateRaw(request.Date) is { } parsed)
        {
            return new Domain.Common.MonthYear(parsed.Month, parsed.Year);
        }

        if (request.Month is { } month && request.Year is { } year)
        {
            return new Domain.Common.MonthYear(month, year);
        }

        return null;
    }
}
