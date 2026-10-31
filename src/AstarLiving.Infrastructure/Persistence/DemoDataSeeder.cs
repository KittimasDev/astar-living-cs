using AstarLiving.Domain.Billing;
using AstarLiving.Domain.Common;
using AstarLiving.Domain.Entities;
using AstarLiving.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AstarLiving.Infrastructure.Persistence;

/// <summary>
/// ข้อมูลตั้งต้นสำหรับเครื่องที่เพิ่งติดตั้งใหม่
///
/// ทำงานสองแบบตาม <see cref="SeedPlan"/>
///   ไซต์จริง  — โครงการเปล่า ค่าตั้งต้น วิธีชำระเงินมาตรฐาน และบัญชีผู้ดูแล 1 บัญชี
///   เครื่องสาธิต — เพิ่มลูกบ้าน งวดค่าส่วนกลาง และใบเสร็จปลอมให้กดดูได้ทุกหน้าจอ
///
/// ทำงานเฉพาะตอนฐานข้อมูลยังไม่มีผู้ใช้งานเลย จึงรันซ้ำได้โดยไม่สร้างข้อมูลซ้ำ
/// และจะไม่แตะฐานข้อมูลของลูกค้าที่มีข้อมูลอยู่แล้ว
/// </summary>
public class DemoDataSeeder(AstarLivingDbContext db, IClock clock, ILogger<DemoDataSeeder> logger)
{
    /// <summary>บัญชีผู้ดูแลระบบสูงสุด ใช้จัดการโครงการและผู้ใช้งาน</summary>
    public const string AdminUsername = "admin";

    /// <summary>บัญชีระดับโครงการ ใช้ทำงานประจำวันทุกหน้าจอ</summary>
    public const string ManagerUsername = "manager";

    private const string DefaultPassword = "password123";

    /// <summary>bcrypt cost เดียวกับระบบเดิม</summary>
    private const int BcryptWorkFactor = 12;

    private readonly AstarLivingDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ILogger<DemoDataSeeder> _logger = logger;

    /// <summary>คืน true เมื่อได้สร้างข้อมูลตั้งต้นจริง</summary>
    public async Task<bool> SeedIfEmptyAsync(SeedPlan plan, CancellationToken ct)
    {
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(ct))
        {
            return false;
        }

        // ไม่มีทั้งบัญชีผู้ดูแลและโหมดสาธิต = สร้างระบบที่ไม่มีใครเข้าได้
        // ต้องหยุดตรงนี้พร้อมบอกสาเหตุ ดีกว่าปล่อยให้ติดตั้งจบแล้วล็อกอินไม่ได้
        if (!plan.HasAdminAccount && !plan.IncludeDemoData)
        {
            throw new InvalidOperationException(
                "ฐานข้อมูลยังว่างแต่ไม่ได้ระบุบัญชีผู้ดูแลระบบ " +
                "ตัวติดตั้งต้องเรียก AstarLiving.Setup set-admin ก่อนเริ่มบริการ");
        }

        _logger.LogInformation(
            "ฐานข้อมูลยังว่าง กำลังสร้างข้อมูลตั้งต้น (โหมดสาธิต: {Demo})", plan.IncludeDemoData);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var calendar = new ThaiCalendar(_clock);
        var now = _clock.UtcNow;
        var documentDate = DateOnly.FromDateTime(calendar.NowThaiPlus7Hours);

        var project = CreateProject(now, plan);
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);

        _db.Settings.Add(CreateSettings(project.Id, now));
        _db.NoticeTemplates.Add(CreateNoticeTemplate(project.Id, now));
        _db.PaymentMethods.AddRange(CreatePaymentMethods(now));
        _db.Users.AddRange(CreateUsers(project.Id, now, plan));

        if (!plan.IncludeDemoData)
        {
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "สร้างข้อมูลตั้งต้นสำหรับใช้งานจริงเรียบร้อย: โครงการ {Project}", project.Name);

            return true;
        }

        var residents = CreateResidents(project.Id, now);
        _db.Residents.AddRange(residents);
        await _db.SaveChangesAsync(ct);

        await CreateBillingHistoryAsync(project.Id, residents, calendar, now, documentDate, ct);

        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "สร้างข้อมูลสาธิตเรียบร้อย: โครงการ {Project}, ลูกบ้าน {Residents} หลัง",
            project.Name, residents.Count);

        return true;
    }

    private static Project CreateProject(DateTimeOffset now, SeedPlan plan) => new()
    {
        // ไซต์จริงได้แค่ชื่อ ที่เหลือเว้นว่างให้ลูกค้ากรอกเอง
        // ถ้าใส่ที่อยู่ปลอมไว้ มันจะไปโผล่บนใบแจ้งหนี้จริงที่ส่งถึงลูกบ้าน
        Name = plan.IncludeDemoData ? "หมู่บ้านตัวอย่าง A star Living" : plan.ProjectName,
        Address = plan.IncludeDemoData
            ? "99/1 ซอยตัวอย่าง 12 ถนนทดสอบ แขวงสาธิต เขตทดลอง กรุงเทพมหานคร 10110"
            : string.Empty,
        ContactName = plan.IncludeDemoData ? "คุณผู้จัดการ นิติบุคคล" : string.Empty,
        ContactPhone = plan.IncludeDemoData ? "02-000-0000" : string.Empty,
        ContactFaxPhone = plan.IncludeDemoData ? "02-000-0001" : string.Empty,
        Active = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static Setting CreateSettings(int projectId, DateTimeOffset now) => new()
    {
        ProjectId = projectId,
        MaintenanceFeeRate = 50m,
        FineRate = 10m,
        CollectionFee = 200m,
        DiscountRate = 5m,
        InvoicePrefix = "INV",
        ReceiptPrefix = "REC",
        InvoiceNote = "<div>กรุณาชำระภายในวันที่ 10 ของทุกเดือน</div>"
                      + "<div>ชำระผ่านบัญชีธนาคารตัวอย่าง เลขที่ 123-4-56789-0</div>",
        ReceiptNote = "<div>ใบเสร็จนี้จะสมบูรณ์เมื่อเรียกเก็บเงินตามเช็คได้แล้ว</div>",
        Active = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static NoticeTemplate CreateNoticeTemplate(int projectId, DateTimeOffset now) => new()
    {
        ProjectId = projectId,
        TopicName = "หนังสือทวงถามค่าส่วนกลาง",
        IntroductionDetail =
            "<div>ตามที่ท่าน {resident_name} ได้ค้างชำระค่าส่วนกลางเป็นจำนวนเงิน {amount} บาท นั้น</div>",
        AdditionalDetail =
            "<div>นิติบุคคลขอความกรุณาให้ท่านติดต่อชำระภายใน 7 วันนับจากวันที่ได้รับหนังสือฉบับนี้</div>",
        ClosureText = "<div>จึงเรียนมาเพื่อโปรดดำเนินการ และขอขอบคุณมา ณ โอกาสนี้</div>",
        ClosingWord = "ขอแสดงความนับถือ",
        Remark = "<div>หากท่านชำระเรียบร้อยแล้ว ขอความกรุณาละเว้นหนังสือฉบับนี้</div>",
        Active = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static List<PaymentMethod> CreatePaymentMethods(DateTimeOffset now) =>
    [
        new() { Code = "CASH", Name = "เงินสด", Description = "ชำระด้วยเงินสดที่สำนักงานนิติบุคคล", Active = true, CreatedAt = now, UpdatedAt = now },
        new() { Code = "TRANSFER", Name = "โอนเงิน", Description = "โอนเข้าบัญชีธนาคารของนิติบุคคล", Active = true, CreatedAt = now, UpdatedAt = now },
        new() { Code = "CHEQUE", Name = "เช็ค", Description = "ชำระด้วยเช็คธนาคาร", Active = true, CreatedAt = now, UpdatedAt = now },
    ];

    /// <summary>
    /// บัญชีเริ่มต้นสองบัญชี รหัสผ่านเดียวกันทั้งคู่
    /// admin   = ผู้ดูแลระบบสูงสุด เห็นเมนูตั้งค่าโครงการและผู้ใช้งาน
    /// manager = ผู้จัดการโครงการ เห็นเมนูงานประจำวันทั้งหมด
    /// </summary>
    private static List<User> CreateUsers(int projectId, DateTimeOffset now, SeedPlan plan)
    {
        // ไซต์จริงได้บัญชีเดียวที่ช่างผู้ติดตั้งตั้งรหัสผ่านเอง
        // ห้ามแถมบัญชี manager ที่ใช้รหัสผ่านซึ่งรู้กันทั้งวงการมาด้วย
        if (!plan.IncludeDemoData)
        {
            return
            [
                new User
                {
                    Username = plan.AdminUsername!,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(plan.AdminPassword!, BcryptWorkFactor),
                    FullName = "ผู้ดูแลระบบ",
                    Email = string.Empty,
                    Role = UserRole.SUPERADMIN,
                    ProjectId = projectId,
                    Active = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
            ];
        }

        var hash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword, BcryptWorkFactor);

        return
        [
            new User
            {
                Username = AdminUsername,
                PasswordHash = hash,
                FullName = "ผู้ดูแลระบบ",
                Email = "admin@example.com",
                Role = UserRole.SUPERADMIN,
                ProjectId = projectId,
                Active = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new User
            {
                Username = ManagerUsername,
                PasswordHash = hash,
                FullName = "ผู้จัดการโครงการ",
                Email = "manager@example.com",
                Role = UserRole.ADMIN,
                ProjectId = projectId,
                Active = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
        ];
    }

    /// <summary>ลูกบ้าน 10 หลัง อัตราส่วนกรรมสิทธิ์ต่างกันเพื่อให้ยอดแต่ละหลังไม่เท่ากัน</summary>
    private static List<Resident> CreateResidents(int projectId, DateTimeOffset now)
    {
        string[] names =
        [
            "สมชาย ใจดี", "สมหญิง รักบ้าน", "ประยุทธ มั่นคง", "วรรณา สุขใจ", "อนันต์ พูนผล",
            "ปรียานุช ทองสุก", "ธนพล เจริญทรัพย์", "กมลวรรณ ศรีสุข", "ณัฐวุฒิ ตั้งใจ", "พรทิพย์ งามวงศ์",
        ];

        decimal[] ratios = [40m, 45.5m, 38m, 52m, 40m, 48.5m, 36m, 44m, 50m, 42.5m];

        return names.Select((name, index) => new Resident
        {
            ProjectId = projectId,
            HouseNo = $"99/{index + 1}",
            FullName = name,
            OwnershipRatio = ratios[index],
            Phone = $"08{index}-000-00{index}{index}",
            Email = $"resident{index + 1}@example.com",
            Address = $"99/{index + 1} ซอยตัวอย่าง 12 ถนนทดสอบ แขวงสาธิต เขตทดลอง กรุงเทพมหานคร 10110",
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();
    }

    /// <summary>
    /// สร้างงวดค่าส่วนกลางย้อนหลัง 5 เดือน
    ///
    /// จงใจให้สถานะคละกัน เพื่อให้ทุกหน้าจอมีข้อมูลให้ดู
    /// - งวดเก่าสุดชำระครบแล้ว
    /// - งวดกลางค้างบางหลัง พร้อมใบทวงถามและค่าเบี้ยปรับ
    /// - งวดล่าสุดยังไม่ชำระทั้งหมด
    /// </summary>
    private async Task CreateBillingHistoryAsync(
        int projectId,
        List<Resident> residents,
        ThaiCalendar calendar,
        DateTimeOffset now,
        DateOnly documentDate,
        CancellationToken ct)
    {
        const decimal maintenanceRate = 50m;
        const decimal fineRatePercent = 10m;
        const decimal collectionFeeRate = 200m;
        const int monthsOfHistory = 5;

        var current = calendar.CurrentPeriod;
        var sequence = 1;
        var receiptSequence = 1;

        // วนใช้ทั้งสามวิธีชำระ เพื่อให้กราฟวงแหวนบนแดชบอร์ดมีครบทุกชิ้น
        var methods = await _db.PaymentMethods.OrderBy(m => m.Id).ToListAsync(ct);

        // ไล่จากงวดเก่าสุดมาหางวดปัจจุบัน
        for (var offset = monthsOfHistory - 1; offset >= 0; offset--)
        {
            var period = Shift(current, -offset);
            var isOldest = offset == monthsOfHistory - 1;
            var isLatest = offset == 0;

            foreach (var (resident, index) in residents.Select((r, i) => (r, i)))
            {
                var maintenance = BillingCalculator.Maintenance(maintenanceRate, resident.OwnershipRatio);

                // บ้านเลขคู่ชำระตรงเวลา บ้านเลขคี่ค้างบางงวด
                var paysOnTime = index % 2 == 0;
                var status = (isOldest, isLatest, paysOnTime) switch
                {
                    (true, _, _) => PaymentStatus.PAID,
                    (_, true, _) => PaymentStatus.NOT_PAY,
                    (_, _, true) => PaymentStatus.PAID,
                    _ => PaymentStatus.NOT_PAY,
                };

                // บ้านที่ค้างมาก่อนจะถูกคิดค่าปรับและค่าทวงถามในงวดถัดมา
                var hasArrears = !paysOnTime && !isOldest;
                var fine = hasArrears && !isLatest
                    ? BillingCalculator.Fine(maintenance, fineRatePercent)
                    : 0m;
                var collectionFee = hasArrears && !isLatest ? collectionFeeRate : 0m;

                var payment = new Payment
                {
                    ResidentId = resident.Id,
                    InvNo = $"{documentDate:yyMMdd}{sequence++:00000}",
                    Year = period.Year,
                    Month = (short)period.Month,
                    AmountDue = maintenance,
                    Fine = fine,
                    CollectionFee = collectionFee,
                    TotalDue = maintenance + fine + collectionFee,
                    PaymentDate = documentDate,
                    Status = status,
                    HasCollectionNotice = collectionFee > 0m,
                    CollectionPaid = false,
                    Active = true,
                    Advance = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                _db.Payments.Add(payment);
                await _db.SaveChangesAsync(ct);

                // งวดที่ชำระแล้วต้องมีใบเสร็จคู่กันเสมอ
                // ถ้าไม่สร้าง หน้าใบเสร็จและกราฟวิธีชำระเงินบนแดชบอร์ดจะว่างเปล่า
                if (status == PaymentStatus.PAID && methods.Count > 0)
                {
                    var method = methods[(receiptSequence - 1) % methods.Count];

                    var receipt = new Receipt
                    {
                        ProjectId = projectId,
                        ReceiptNo = $"{documentDate:yyMMdd}{receiptSequence++:00000}",
                        IssueDate = documentDate,
                        ResidentId = resident.Id,
                        MonthCodes = [MonthCode(period.Month)],
                        TotalAmount = payment.TotalDue,
                        PaymentMethodId = method.Id,
                        BankName = method.Code == "TRANSFER" ? "ธนาคารกรุงเทพ" : null,
                        ChequeNo = method.Code == "CHEQUE" ? $"CHQ{receiptSequence:0000}" : null,
                        PaymentDate = documentDate,
                        Active = true,
                        HaveDiscount = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };

                    _db.Receipts.Add(receipt);
                    await _db.SaveChangesAsync(ct);

                    payment.ReceiptsId = receipt.Id;
                    payment.PaidAmount = payment.TotalDue;
                }

                if (fine > 0m)
                {
                    _db.CollectionNotices.Add(CreateNotice(
                        resident.Id, payment.Id, NoticeType.FINE, period, documentDate, fine, now));
                }

                if (collectionFee > 0m)
                {
                    var notice = CreateNotice(
                        resident.Id, payment.Id, NoticeType.COLLECTION, period, documentDate, collectionFee, now);

                    _db.CollectionNotices.Add(notice);
                    await _db.SaveChangesAsync(ct);

                    payment.CollectionNoticeId = notice.Id;
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        await CreateSampleRevenueAsync(projectId, residents[0], documentDate, now, ct);
    }

    private static CollectionNotice CreateNotice(
        int residentId, int paymentId, NoticeType type, MonthYear period,
        DateOnly noticeDate, decimal feeAmount, DateTimeOffset now) => new()
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
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>ใบเสร็จรายรับอื่นตัวอย่าง เพื่อให้หน้ารายรับอื่นและรายงานมีข้อมูล</summary>
    private async Task CreateSampleRevenueAsync(
        int projectId, Resident resident, DateOnly documentDate, DateTimeOffset now, CancellationToken ct)
    {
        var cash = await _db.PaymentMethods.FirstAsync(m => m.Code == "CASH", ct);

        var items = new List<RevenuePaymentItem>
        {
            new() { ItemNo = 1, Description = "ค่าทำบัตรจอดรถ", Quantity = 2m, UnitPrice = 100m, Total = 200m },
            new() { ItemNo = 2, Description = "ค่าคีย์การ์ดสำรอง", Quantity = 1m, UnitPrice = 350m, Total = 350m },
        };

        var total = items.Sum(i => i.Total);

        _db.RevenueTransactions.Add(new RevenueTransaction
        {
            ProjectId = projectId,
            ReceiptNo = $"{documentDate:yyMMdd}90001",
            IssueDate = documentDate,
            ResidentId = resident.Id,
            PaymentDetail = new RevenuePaymentDetail { Items = items, GrandTotal = total },
            TotalAmount = total,
            PaymentMethodId = cash.Id,
            Note = "ตัวอย่างรายรับอื่น ๆ",
            PaymentDate = documentDate,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// รหัสเดือนภาษาอังกฤษที่คอลัมน์ payment_ids ของ gv_receipts เก็บจริง
    /// (ชื่อคอลัมน์สื่อว่าเป็นรหัสของงวด แต่ของเดิมเก็บชื่อเดือน)
    /// </summary>
    private static string MonthCode(int month) => month switch
    {
        1 => "JAN", 2 => "FEB", 3 => "MAR", 4 => "APR", 5 => "MAY", 6 => "JUN",
        7 => "JUL", 8 => "AUG", 9 => "SEP", 10 => "OCT", 11 => "NOV", _ => "DEC",
    };

    /// <summary>เลื่อนงวดไปข้างหน้าหรือถอยหลังตามจำนวนเดือนที่ระบุ</summary>
    private static MonthYear Shift(MonthYear period, int months)
    {
        var result = period;

        for (var i = 0; i < Math.Abs(months); i++)
        {
            result = months < 0
                ? ThaiCalendar.PreviousMonth(result)
                : ThaiCalendar.NextMonth(result);
        }

        return result;
    }
}
