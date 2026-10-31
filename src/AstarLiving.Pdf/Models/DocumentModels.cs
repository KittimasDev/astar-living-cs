namespace AstarLiving.Pdf.Models;

/// <summary>ข้อมูลหัวกระดาษของโครงการ — ตรงกับ ProjectInfo ของเดิม</summary>
public sealed class ProjectInfo
{
    public string ProjectName { get; init; } = string.Empty;
    public string ProjectAddress { get; init; } = string.Empty;
    public string ProjectTel { get; init; } = string.Empty;
    /// <summary>path สัมพัทธ์ของโลโก้ ใช้หาไฟล์จริงบนดิสก์</summary>
    public string? ProjectLogoPath { get; init; }
    /// <summary>หมายเหตุท้ายเอกสาร เก็บเป็น HTML จาก TinyMCE</summary>
    public string? Remark { get; init; }
    /// <summary>หมายเหตุสำหรับใบเสร็จโดยเฉพาะ</summary>
    public string? ReceiptNote { get; init; }
}

/// <summary>ผู้ลงนามท้ายเอกสาร</summary>
public sealed class SignInfo
{
    public string Name { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
}

/// <summary>หนึ่งบรรทัดในตารางรายการ</summary>
public sealed class DocumentItem
{
    public string? Name { get; init; }
    public decimal? Unit { get; init; }
    public decimal? Price { get; init; }
    public decimal? Total { get; init; }

    /// <summary>
    /// แถวที่ไม่มีชื่อหรือยอดเป็นศูนย์จะถูกพิมพ์เป็นแถวว่าง ไม่ใช่ถูกตัดทิ้ง
    /// เพื่อให้ความสูงของตารางคงที่ ซึ่งสำคัญกับการแบ่งครึ่งหน้ากระดาษ
    /// </summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Name) || (Total ?? 0m) == 0m;
}

/// <summary>ข้อมูลการชำระเงินท้ายใบเสร็จ</summary>
public sealed class PaymentInfo
{
    public string? Name { get; init; }
    public string? IssueDate { get; init; }
    public string? BankName { get; init; }
    public string? ChequeNo { get; init; }
}

/// <summary>ใบแจ้งหนี้หนึ่งใบที่พร้อมพิมพ์</summary>
public sealed class InvoiceDocumentModel
{
    public string InvoiceNo { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string HouseNo { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    /// <summary>วันที่แบบไทย DD/MM/YYYY</summary>
    public string Date { get; init; } = string.Empty;
    /// <summary>เดือนของงวด รูปแบบ M/YYYY</summary>
    public string Month { get; init; } = string.Empty;

    public List<DocumentItem> Items { get; init; } = [];

    /// <summary>บล็อกยอดค้างชำระ ต้องมี 3 บรรทัดเสมอ บรรทัดว่างใช้ NBSP</summary>
    public List<string> DebtItems { get; init; } = [];

    public decimal Subtotal { get; init; }
    public decimal Debt { get; init; }
    public decimal Total { get; init; }

    /// <summary>ใช้เฉพาะใบแจ้งหนี้ล่วงหน้า</summary>
    public PaymentInfo? Payment { get; init; }

    public bool HasDebtRows => DebtItems.Any(d => !string.IsNullOrWhiteSpace(d));
}

/// <summary>ใบเสร็จหนึ่งใบที่พร้อมพิมพ์</summary>
public sealed class ReceiptDocumentModel
{
    public string ReceiptNo { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string HouseNo { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Month { get; init; } = string.Empty;
    public List<DocumentItem> Items { get; init; } = [];
    public decimal Total { get; init; }
    public PaymentInfo? Payment { get; init; }
}

/// <summary>หนึ่งบรรทัดในใบทวงถาม</summary>
public sealed class CollectionLine
{
    /// <summary>ระบบเดิมแสดงวันที่เฉพาะแถวแรกของใบ แถวที่เหลือเว้นว่าง</summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>เลขที่ใบแจ้งหนี้ แสดงเฉพาะแถวแรกเช่นกัน</summary>
    public string InvoiceNo { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal Remain { get; init; }
}

/// <summary>ใบทวงถามหนึ่งใบที่พร้อมพิมพ์</summary>
public sealed class CollectionDocumentModel
{
    public int CollectionId { get; init; }
    public string Subject { get; init; } = "เตือนให้ชำระค่าใช้จ่ายค้างชำระ";
    public string Date { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string HouseNo { get; init; } = string.Empty;
    /// <summary>เลขหน้าในชุด เช่น "1/5"</summary>
    public string Page { get; init; } = "1/1";
    public decimal Total { get; init; }
    public List<CollectionLine> Items { get; init; } = [];

    /// <summary>เนื้อหาจากเทมเพลต ผ่านการแทนที่ token แล้ว</summary>
    public string? IntroductionDetail { get; init; }
    public string? AdditionalDetail { get; init; }
    public string? ClosureText { get; init; }
    public string ClosingWord { get; init; } = string.Empty;
    public string? TemplateRemark { get; init; }
}

/// <summary>หนึ่งงวดในรายงานสรุปค่าใช้จ่าย</summary>
public sealed class SummaryExpenseLine
{
    public string Period { get; init; } = string.Empty;

    /// <summary>ปี พ.ศ. ใช้จัดกลุ่มในรายงานรายบ้าน 0 = ไม่ได้ใช้</summary>
    public int Year { get; init; }

    /// <summary>ชื่อเดือนย่อ เช่น ต.ค. ใช้ในคอลัมน์ "เดือน" ของรายงานรายบ้าน</summary>
    public string MonthLabel { get; init; } = string.Empty;

    /// <summary>เลขที่ใบแจ้งหนี้ของงวดนั้น</summary>
    public string InvoiceNo { get; init; } = string.Empty;
    public decimal CommonFee { get; init; }
    public decimal Fine { get; init; }
    public decimal CollectionFee { get; init; }
    public decimal Total { get; init; }
}

/// <summary>กลุ่มของหนึ่งบ้านในรายงานสรุปค่าใช้จ่าย</summary>
public sealed class SummaryExpenseGroup
{
    public string HouseNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public List<SummaryExpenseLine> Lines { get; init; } = [];
    public decimal Total { get; init; }
}

/// <summary>รายงานสรุปค่าใช้จ่ายที่พร้อมพิมพ์</summary>
public enum SummaryExpenseScope
{
    /// <summary>รายงานของทั้งโครงการ — ตารางแบนตารางเดียว หนึ่งแถวต่อหนึ่งบ้าน</summary>
    Project,

    /// <summary>รายงานของบ้านหลังเดียว — แยกบล็อกตามปี แล้วปิดท้ายด้วยตารางสรุป</summary>
    Resident,
}

public sealed class SummaryExpensesDocumentModel
{
    public SummaryExpenseScope Scope { get; init; } = SummaryExpenseScope.Project;

    public string PeriodLabel { get; init; } = string.Empty;
    /// <summary>หัวคอลัมน์ค่าปรับที่มีอัตราต่อท้าย เช่น "ค่าปรับ (10%)"</summary>
    public string FineColumnLabel { get; init; } = "ค่าปรับ";
    public List<SummaryExpenseGroup> Groups { get; init; } = [];
    public decimal GrandTotal { get; init; }

    /// <summary>ยอดรวมแยกตามชนิด ใช้ในตารางสรุปท้ายรายงานรายบ้าน</summary>
    public decimal TotalCommonFee { get; init; }

    public decimal TotalFine { get; init; }

    public decimal TotalCollectionFee { get; init; }
}
