namespace AstarLiving.Pdf.Models;

public enum ReportColumnAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>คอลัมน์หนึ่งของตารางรายงานที่จะพิมพ์</summary>
public sealed class ReportDocumentColumn
{
    public required string Header { get; init; }

    public ReportColumnAlignment Alignment { get; init; } = ReportColumnAlignment.Left;

    /// <summary>น้ำหนักความกว้างเทียบกับคอลัมน์อื่น 0 = แบ่งเท่ากัน</summary>
    public float RelativeWidth { get; init; }
}

/// <summary>
/// ข้อมูลของตารางรายงานที่จะพิมพ์
///
/// แถวถูกจัดรูปแบบมาเป็นข้อความเรียบร้อยแล้วจากฝั่งที่เรียก
/// เพราะรายงานแต่ละชนิดมีคอลัมน์ไม่เหมือนกัน และหน้าจอเป็นฝ่ายรู้ว่ากำลังแสดงอะไรอยู่
/// วิธีนี้ทำให้สิ่งที่พิมพ์ออกมาตรงกับสิ่งที่ผู้ใช้เห็นบนจอเสมอ เหมือนที่ระบบเดิมทำ
/// </summary>
public sealed class ReportDocumentModel
{
    public required string ProjectName { get; init; }

    /// <summary>เช่น "รายงานค้างชำระ ประจำปี พ.ศ. 2569"</summary>
    public required string ReportTitle { get; init; }

    /// <summary>เช่น "ณ วันที่ 22 พฤษภาคม 2569"</summary>
    public required string AsOfDate { get; init; }

    /// <summary>เช่น "22 พ.ค. 2569 เวลา 15:50 น."</summary>
    public required string PrintedAt { get; init; }

    public required IReadOnlyList<ReportDocumentColumn> Columns { get; init; }

    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }
}
