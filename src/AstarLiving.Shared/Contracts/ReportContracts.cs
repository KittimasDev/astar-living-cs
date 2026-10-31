using AstarLiving.Domain.Enums;

namespace AstarLiving.Shared.Contracts;

/// <summary>รายงานค้างชำระ — แถวคือหนึ่งงวดที่สถานะ NOT_PAY</summary>
public class OutstandingReportRow
{
    public int Id { get; init; }
    public int ResidentId { get; init; }
    public string? InvNo { get; init; }
    public int Year { get; init; }
    public short Month { get; init; }
    public string MonthTh { get; init; } = string.Empty;
    public decimal AmountDue { get; init; }
    public decimal? Fine { get; init; }
    public decimal? CollectionFee { get; init; }
    public decimal TotalDue { get; init; }
    public PaymentStatus Status { get; init; }
    public string HouseNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Address { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>รายงานการชำระเงินส่วนกลาง — แถวคือหนึ่งใบเสร็จ (mapPaymentRow ของเดิม)</summary>
public class PaymentReportRow
{
    public int Id { get; init; }
    public int ResidentId { get; init; }
    public string HouseNo { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public bool Active { get; init; }
    /// <summary>ระบบเดิมใส่ค่าเดียวกับ active ลงในคีย์ status ด้วย</summary>
    public bool Status { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public string? CreatedByName { get; init; }
    public string RejectedReason { get; init; } = string.Empty;
    public string ReceiptNo { get; init; } = string.Empty;
    public DateOnly? IssueDate { get; init; }
    public decimal TotalAmount { get; init; }
    public DateOnly? PaymentDate { get; init; }
    public string PaymentMethodCode { get; init; } = string.Empty;
    public string PaymentMethodName { get; init; } = string.Empty;
    public string UserFullName { get; init; } = string.Empty;
}

/// <summary>
/// รายงานใบแจ้งค่าใช้จ่าย — จัดกลุ่มตาม (ผู้ออกใบ, ปี, เดือน)
/// ยอดทุกตัวรวมค่าส่วนกลาง ค่าปรับ และค่าทวงถามเข้าด้วยกัน
/// </summary>
public class InvoiceReportRow
{
    /// <summary>เลขลำดับที่นับใหม่ทุกครั้ง ไม่ใช่รหัสในฐานข้อมูล</summary>
    public int Id { get; init; }
    public string MonthTh { get; init; } = string.Empty;
    /// <summary>จำนวนใบแจ้งหนี้ในกลุ่มนี้</summary>
    public int CountPm { get; init; }
    public string CreatedUser { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public decimal TotalPaid { get; init; }
    public decimal TotalNotPaid { get; init; }
}

/// <summary>รายงานค่าปรับและค่าทวงถาม</summary>
public class FineReportRow
{
    public int Id { get; init; }
    public short Month { get; init; }
    public int Year { get; init; }
    public string MonthTh { get; init; } = string.Empty;
    public string ResidentHouseNo { get; init; } = string.Empty;
    public string ResidentFullName { get; init; } = string.Empty;
    public string ResidentAddress { get; init; } = string.Empty;
    public decimal? Fine { get; init; }
    public decimal? CollectionFee { get; init; }
    public decimal TotalDue { get; init; }
    public PaymentStatus? StatusPayments { get; init; }
    /// <summary>สถานะของใบทวงถามที่ถูกเลือกเป็นตัวแทนของงวดนี้</summary>
    public NoticeStatus? StatusNotices { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public string? CreatedByName { get; init; }
}

/// <summary>การจัดวางข้อความในคอลัมน์ของรายงานที่จะพิมพ์</summary>
public enum ReportPrintAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>คอลัมน์หนึ่งของรายงานที่หน้าจอขอให้พิมพ์</summary>
public enum ReportPrintValueKind
{
    Text,

    /// <summary>จำนวนเงิน ในไฟล์ Excel ต้องเขียนเป็นตัวเลขจริงพร้อมรูปแบบ #,##0.00</summary>
    Money,

    /// <summary>จำนวนนับ เขียนเป็นตัวเลขจำนวนเต็ม</summary>
    Integer,
}

public class ReportPrintColumn
{
    public string Header { get; set; } = string.Empty;

    public ReportPrintAlignment Alignment { get; set; } = ReportPrintAlignment.Left;

    /// <summary>น้ำหนักความกว้าง 0 = แบ่งเท่ากัน</summary>
    public float RelativeWidth { get; set; }

    /// <summary>
    /// ชนิดของค่าในคอลัมน์
    ///
    /// ใช้เฉพาะตอนบันทึกเป็น Excel เพื่อเขียนตัวเลขเป็นตัวเลขจริง
    /// ถ้าเขียนเป็นข้อความ ผู้ใช้จะรวมยอดหรือเรียงลำดับใน Excel ไม่ได้
    /// ส่วนการพิมพ์ PDF ใช้ข้อความที่จัดรูปแบบมาแล้วเหมือนเดิม
    /// </summary>
    public ReportPrintValueKind Kind { get; set; } = ReportPrintValueKind.Text;
}

/// <summary>
/// คำขอพิมพ์ตารางรายงาน
///
/// หน้าจอส่งหัวตารางและแถวที่จัดรูปแบบแล้วมาให้ เพราะรายงานแต่ละชนิดคอลัมน์ไม่เหมือนกัน
/// และหน้าจอเป็นฝ่ายรู้ว่าผู้ใช้กำลังดูอะไรอยู่ สิ่งที่พิมพ์จึงตรงกับสิ่งที่เห็นบนจอเสมอ
/// ตรงกับที่ระบบเดิมทำ (jsPDF สร้างจากตารางที่แสดงอยู่)
/// </summary>
/// <summary>หนึ่งบรรทัดในบล็อกสรุปท้ายรายงาน</summary>
public class ReportSummaryLine
{
    public string Label { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    /// <summary>true = บรรทัดยอดรวมสุดท้าย เน้นด้วยตัวหนาและสีพื้น</summary>
    public bool Emphasise { get; set; }
}

public class ReportPrintRequest
{
    /// <summary>ชื่อรายงานล้วน ๆ เช่น "รายงานค้างชำระ" ส่วนปีและวันที่เครื่องแม่เติมให้</summary>
    public string ReportLabel { get; set; } = string.Empty;

    public List<ReportPrintColumn> Columns { get; set; } = [];

    public List<List<string>> Rows { get; set; } = [];

    /// <summary>บล็อกสรุปท้ายรายงาน ว่างเมื่อรายงานชนิดนั้นไม่มียอดรวม</summary>
    public List<ReportSummaryLine> Summary { get; set; } = [];
}
