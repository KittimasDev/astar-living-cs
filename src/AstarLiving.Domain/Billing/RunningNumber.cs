namespace AstarLiving.Domain.Billing;

/// <summary>
/// เลขรันเอกสาร (inv_no / receipt_no) — พอร์ตจาก src/common/utils/running-number.util.ts
/// รูปแบบ: YYMMDD ของวันที่ไทย (+7 ชม.) ตามด้วยลำดับ 5 หลัก รวม 11 ตัวอักษร
/// ลำดับนับต่อจากเลขล่าสุดของ "โครงการเดียวกันและวันเดียวกัน" เท่านั้น
/// </summary>
public static class RunningNumber
{
    public const int SequenceDigits = 5;

    /// <summary>generateInvNo() — ประกอบ prefix กับลำดับ</summary>
    public static string Format(string prefix, int sequence) =>
        $"{prefix}{sequence.ToString(new string('0', SequenceDigits))}";

    /// <summary>
    /// คำนวณลำดับถัดไปจากเลขล่าสุด
    /// ระบบเดิมตัด 5 ตัวท้ายของเลขล่าสุดมาบวกหนึ่ง และเริ่มที่ 1 เมื่อยังไม่มีเลขของวันนั้น
    /// </summary>
    public static int NextSequence(string? lastDocumentNo)
    {
        if (string.IsNullOrWhiteSpace(lastDocumentNo)) return 1;

        var tail = lastDocumentNo.Length <= SequenceDigits
            ? lastDocumentNo
            : lastDocumentNo[^SequenceDigits..];

        return int.TryParse(tail, out var value) ? value + 1 : 1;
    }

    /// <summary>ประกอบเลขถัดไปจาก prefix ของวันนี้กับเลขล่าสุดที่หามาได้</summary>
    public static string Next(string prefix, string? lastDocumentNo) =>
        Format(prefix, NextSequence(lastDocumentNo));
}
