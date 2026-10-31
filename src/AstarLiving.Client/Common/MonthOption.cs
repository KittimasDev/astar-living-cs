namespace AstarLiving.Client.Common;

/// <summary>
/// ตัวเลือกเดือนที่ใช้ร่วมกันทุกหน้าจอ
/// Value คือเลขเดือน 1-12 ที่ส่งไป API ส่วน Label คือชื่อเดือนภาษาไทยที่แสดงให้ผู้ใช้
/// </summary>
public sealed record MonthOption(int Value, string Label)
{
    /// <summary>
    /// กล่องเลื่อนลงที่ถูกจัดรูปแบบเองจะแสดงผลจาก ToString เมื่อหา ContentTemplate ไม่เจอ
    /// ถ้าไม่เขียนทับไว้ ผู้ใช้จะเห็นชื่อชนิดข้อมูลแทนชื่อเดือน
    /// </summary>
    public override string ToString() => Label;
}
