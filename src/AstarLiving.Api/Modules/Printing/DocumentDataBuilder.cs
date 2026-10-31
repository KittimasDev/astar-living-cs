using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Entities;
using AstarLiving.Domain.Enums;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Infrastructure.Storage;
using AstarLiving.Pdf;
using AstarLiving.Pdf.Models;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.Printing;

/// <summary>
/// ประกอบข้อมูลจากฐานข้อมูลให้เป็นรูปแบบที่เอกสาร PDF ต้องการ
///
/// แยกออกมาจากตัวเอกสารเพราะการจัดหน้ากระดาษกับการดึงข้อมูลเป็นคนละเรื่อง
/// และทำให้ทดสอบการจัดหน้าได้โดยไม่ต้องมีฐานข้อมูล
/// </summary>
public class DocumentDataBuilder(
    AstarLivingDbContext db,
    ImageStorage images,
    LogoLoader logoLoader)
{
    private readonly AstarLivingDbContext _db = db;
    private readonly ImageStorage _images = images;
    private readonly LogoLoader _logoLoader = logoLoader;

    /// <summary>
    /// ProjectInfoUtil.getProjectInfo() — หัวกระดาษของโครงการ
    /// หมายเหตุท้ายเอกสารมาจาก invoice_note หรือ receipt_note ตามชนิดเอกสาร
    /// </summary>
    public async Task<ProjectInfo> GetProjectInfoAsync(int projectId, CancellationToken ct)
    {
        var project = await _db.Projects.AsNoTracking()
            .Include(p => p.Settings)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null)
        {
            // ระบบเดิมคืนสตริงว่างแทนการโยน error เพื่อให้ยังพิมพ์เอกสารออกมาได้
            return new ProjectInfo();
        }

        var setting = project.Settings.OrderBy(s => s.Id).FirstOrDefault();

        return new ProjectInfo
        {
            ProjectName = project.Name,
            ProjectAddress = project.Address ?? string.Empty,
            ProjectTel = project.ContactPhone ?? string.Empty,
            ProjectLogoPath = project.ProjectLogo,
            Remark = setting?.InvoiceNote,
            ReceiptNote = setting?.ReceiptNote,
        };
    }

    /// <summary>SignInfoUtil — ผู้ลงนามคือผู้ที่กดพิมพ์</summary>
    public async Task<SignInfo> GetSignInfoAsync(int userId, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.Role })
            .FirstOrDefaultAsync(ct);

        return new SignInfo
        {
            Name = user?.FullName ?? string.Empty,
            Position = user?.Role.ToString() ?? string.Empty,
        };
    }

    public byte[] LoadLogo(ProjectInfo project) =>
        _logoLoader.Load(_images.ResolvePhysicalPath(project.ProjectLogoPath));

    /// <summary>
    /// สร้างใบแจ้งหนี้สำหรับพิมพ์
    ///
    /// แต่ละใบมี 3 บรรทัดรายการเสมอ (ค่าส่วนกลาง ค่าเบี้ยปรับ ค่าทวงถาม)
    /// โดยค่าเบี้ยปรับและค่าทวงถามใช้ชื่อเดือน "ก่อนหน้า" งวดนั้น
    /// และยอดค้างแสดงเป็นบล็อก 3 บรรทัดที่คำนวณจากงวดค้างก่อนหน้า
    /// </summary>
    public async Task<List<InvoiceDocumentModel>> BuildInvoicesAsync(
        int projectId, IReadOnlyList<Payment> payments, DateOnly today, CancellationToken ct)
    {
        var setting = await _db.Settings.AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct);

        var documents = new List<InvoiceDocumentModel>(payments.Count);

        foreach (var payment in payments)
        {
            var resident = payment.Resident;

            var debts = await _db.Payments.AsNoTracking()
                .Where(p => p.Resident.ProjectId == projectId
                            && p.ResidentId == payment.ResidentId
                            && p.Status == PaymentStatus.NOT_PAY
                            && p.Id != payment.Id
                            && (p.Year < payment.Year
                                || (p.Year == payment.Year && p.Month < payment.Month)))
                .Select(p => new { p.Year, p.Month, p.AmountDue, p.Fine, p.CollectionFee })
                .ToListAsync(ct);

            var debtLines = debts
                .Select(d => new DebtLine(d.Year, d.Month, d.AmountDue, d.Fine ?? 0m, d.CollectionFee ?? 0m))
                .ToList();

            var summary = DebtSummary.PdfInvoiceDebtSummary(debtLines, payment.Year, payment.Month);
            var previous = ThaiCalendar.PreviousMonth(payment.Month, payment.Year);

            var items = new List<DocumentItem>
            {
                new()
                {
                    Name = $"ค่าส่วนกลาง ({ThaiCalendar.FormatThaiMonthYear(payment.Month, payment.Year)})",
                    Unit = resident?.OwnershipRatio,
                    Price = setting?.MaintenanceFeeRate,
                    Total = payment.AmountDue,
                },
                new()
                {
                    Name = $"ค่าเบี้ยปรับ ({ThaiCalendar.FormatThaiMonthYear(previous.Month, previous.Year)})",
                    Unit = 1m,
                    Price = payment.Fine,
                    Total = payment.Fine,
                },
                new()
                {
                    Name = $"ค่าทวงถาม ({ThaiCalendar.FormatThaiMonthYear(previous.Month, previous.Year)})",
                    Unit = 1m,
                    Price = payment.CollectionFee,
                    Total = payment.CollectionFee,
                },
            };

            var subtotal = items.Sum(i => i.Total ?? 0m);

            documents.Add(new InvoiceDocumentModel
            {
                InvoiceNo = payment.InvNo ?? string.Empty,
                CustomerName = resident?.FullName ?? string.Empty,
                HouseNo = resident?.HouseNo ?? string.Empty,
                Address = resident?.Address ?? string.Empty,
                Date = ThaiCalendar.ToThaiDate(today),
                Month = $"{payment.Month}/{payment.Year}",
                Items = items,
                DebtItems = summary.Items.Select(i => i.Text).ToList(),
                Subtotal = subtotal,
                Debt = summary.DebtTotal,
                Total = subtotal + summary.DebtTotal,
            });
        }

        return documents;
    }

    /// <summary>
    /// สร้างใบเสร็จสำหรับพิมพ์
    /// รายการถูกรวบด้วย ReceiptItemBuilder ซึ่งยุบหลายงวดให้เหลือบรรทัดเดียวพร้อมช่วงเดือน
    /// </summary>
    public async Task<ReceiptDocumentModel> BuildReceiptAsync(Receipt receipt, CancellationToken ct)
    {
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.ReceiptsId == receipt.Id)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        var paymentIds = payments.Select(p => p.Id).ToList();

        var notices = await _db.CollectionNotices.AsNoTracking()
            .Where(n => n.ReceiptId == receipt.Id
                        || (n.PaymentId != null && paymentIds.Contains(n.PaymentId.Value)))
            .ToListAsync(ct);

        var groups = payments.Select(payment =>
        {
            var related = notices.Where(n => n.PaymentId == payment.Id).ToList();
            return new ReceiptPaymentGroup(
                payment,
                related.Where(n => n.NoticesType == NoticeType.FINE).ToList(),
                related.Where(n => n.NoticesType == NoticeType.COLLECTION).ToList());
        }).ToList();

        var setting = await _db.Settings.AsNoTracking()
            .Where(s => s.ProjectId == receipt.ProjectId)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct);

        var discount = receipt.HaveDiscount && receipt.DiscountRate is { } rate
            ? rate * groups.Sum(g => g.Payment.AmountDue) / 100m
            : 0m;

        var built = ReceiptItemBuilder.Build(
            groups, receipt.HaveDiscount, setting?.DiscountRate, discount);

        var paymentMethod = receipt.PaymentMethodId is null
            ? null
            : await _db.PaymentMethods.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == receipt.PaymentMethodId, ct);

        return new ReceiptDocumentModel
        {
            ReceiptNo = receipt.ReceiptNo ?? string.Empty,
            CustomerName = receipt.Resident?.FullName ?? string.Empty,
            HouseNo = receipt.Resident?.HouseNo ?? string.Empty,
            Address = receipt.Resident?.Address ?? string.Empty,
            Date = ThaiCalendar.ToThaiDate(receipt.IssueDate),
            Month = ThaiCalendar.ToThaiMonthYear(receipt.PaymentDate.ToDateTime(TimeOnly.MinValue)),
            Items = built.Items.Select(i => new DocumentItem
            {
                Name = i.Name,
                Unit = i.Unit,
                Price = i.Price,
                Total = i.Total,
            }).ToList(),
            Total = built.Total,
            Payment = new PaymentInfo
            {
                Name = paymentMethod?.Name,
                IssueDate = ThaiCalendar.ToThaiDate(receipt.IssueDate),
                BankName = receipt.BankName,
                ChequeNo = receipt.ChequeNo,
            },
        };
    }

    /// <summary>
    /// สร้างใบทวงถามสำหรับพิมพ์
    /// เนื้อความมาจากเทมเพลตของโครงการ โดยแทนที่ token {resident_name} และ {amount} ก่อน
    /// </summary>
    public async Task<List<CollectionDocumentModel>> BuildCollectionNoticesAsync(
        IReadOnlyList<CollectionNotice> notices, DateOnly today, CancellationToken ct)
    {
        if (notices.Count == 0) return [];

        var projectId = notices[0].Resident?.ProjectId ?? 0;

        var template = await _db.NoticeTemplates.AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync(ct);

        var documents = new List<CollectionDocumentModel>(notices.Count);
        var total = notices.Count;

        for (var index = 0; index < notices.Count; index++)
        {
            var notice = notices[index];
            var amount = notice.FeeAmount ?? 0m;
            var previous = ThaiCalendar.PreviousMonth(notice.Month, notice.Year);

            var tokens = new Dictionary<string, string>
            {
                ["resident_name"] = notice.Resident?.FullName ?? string.Empty,
                ["amount"] = MoneyFormat.Fixed2(amount),
            };

            documents.Add(new CollectionDocumentModel
            {
                CollectionId = notice.Id,
                Date = ThaiCalendar.ToThaiDate(today),
                CustomerName = notice.Resident?.FullName ?? string.Empty,
                HouseNo = notice.Resident?.HouseNo ?? string.Empty,
                Page = $"{index + 1}/{total}",
                Total = amount,
                Items = amount > 0m
                    ?
                    [
                        new CollectionLine
                        {
                            Date = ThaiCalendar.ToThaiDate(notice.NoticeDate),
                            InvoiceNo = notice.Payment?.InvNo ?? string.Empty,
                            Name = $"ค่าทวงถาม ({ThaiCalendar.FormatThaiMonthYear(previous.Month, previous.Year)})",
                            Amount = amount,
                            Remain = amount,
                        },
                    ]
                    : [],
                IntroductionDetail = NoticeTemplateRenderer.Apply(template?.IntroductionDetail, tokens),
                AdditionalDetail = NoticeTemplateRenderer.Apply(template?.AdditionalDetail, tokens),
                ClosureText = template?.ClosureText,
                ClosingWord = template?.ClosingWord ?? string.Empty,
                TemplateRemark = template?.Remark,
            });
        }

        return documents;
    }

    /// <summary>สร้างใบเสร็จรายรับอื่นสำหรับพิมพ์</summary>
    public async Task<ReceiptDocumentModel> BuildRevenueReceiptAsync(
        RevenueTransaction transaction, CancellationToken ct)
    {
        var paymentMethod = await _db.PaymentMethods.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == transaction.PaymentMethodId, ct);

        return new ReceiptDocumentModel
        {
            ReceiptNo = transaction.ReceiptNo,
            CustomerName = transaction.Resident?.FullName ?? string.Empty,
            HouseNo = transaction.Resident?.HouseNo ?? string.Empty,
            Address = transaction.Resident?.Address ?? string.Empty,
            Date = ThaiCalendar.ToThaiDate(transaction.IssueDate),
            Month = string.Empty,
            Items = transaction.PaymentDetail.Items.Select(i => new DocumentItem
            {
                Name = i.Description,
                Unit = i.Quantity ?? 1m,
                Price = i.UnitPrice,
                Total = i.Total,
            }).ToList(),
            Total = transaction.TotalAmount,
            Payment = new PaymentInfo
            {
                Name = paymentMethod?.Name,
                IssueDate = ThaiCalendar.ToThaiDate(transaction.IssueDate),
                BankName = transaction.BankName,
                ChequeNo = transaction.ChequeNo,
            },
        };
    }
}
