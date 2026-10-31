using AstarLiving.Domain.Enums;

namespace AstarLiving.Domain.Billing;

/// <summary>หนึ่งรายการที่ผู้ใช้ติ๊กเลือกในหน้าออกใบเสร็จ</summary>
/// <param name="Type">ชนิดรายการ</param>
/// <param name="Selected">ถูกเลือกหรือไม่</param>
/// <param name="Id">รหัสงวดสำหรับ MAINTENANCE หรือรหัสใบทวงถามสำหรับอีกสองชนิด</param>
public readonly record struct SelectedReceiptItem(ReceiptItemType Type, bool Selected, int Id);

/// <summary>ผลการแปลงรายการที่เลือกของงวดหนึ่ง</summary>
/// <param name="MaintenancePaymentId">รหัสงวดที่เลือกชำระค่าส่วนกลาง (null เมื่อไม่ได้เลือก)</param>
/// <param name="Status">สถานะที่งวดนั้นจะกลายเป็น</param>
/// <param name="NoticeIds">ใบทวงถามที่ต้องปิดเป็นชำระแล้ว</param>
public sealed record ReceiptSelectionOutcome(
    int? MaintenancePaymentId,
    PaymentStatus Status,
    IReadOnlyList<int> NoticeIds)
{
    /// <summary>มีการชำระใบทวงถามอย่างน้อยหนึ่งใบ</summary>
    public bool CollectionPaid => NoticeIds.Count > 0;
}

/// <summary>
/// กฎแปลงสิ่งที่ผู้ใช้ติ๊กเลือก เป็นสถานะของงวด — พอร์ตจาก receipt.service.ts
///
/// เลือกครบทุกรายการ  -> PAID
/// เลือกบางรายการ     -> PARTIAL_PAY
/// ไม่เลือกเลย         -> NOT_PAY
/// </summary>
public static class ReceiptSelectionRules
{
    public static ReceiptSelectionOutcome Resolve(IReadOnlyList<SelectedReceiptItem> items)
    {
        var hasSelected = items.Any(i => i.Selected);
        var hasUnselected = items.Any(i => !i.Selected);

        var status = hasSelected switch
        {
            true when !hasUnselected => PaymentStatus.PAID,
            true => PaymentStatus.PARTIAL_PAY,
            _ => PaymentStatus.NOT_PAY,
        };

        var noticeIds = items
            .Where(i => i.Selected && i.Type is ReceiptItemType.FINE or ReceiptItemType.COLLECTION)
            .Select(i => i.Id)
            .ToList();

        // ระบบเดิมหยิบรายการค่าส่วนกลางที่ถูกเลือกเป็นตัวแรกเท่านั้น
        var maintenanceId = items
            .Where(i => i.Selected && i.Type == ReceiptItemType.MAINTENANCE)
            .Select(i => (int?)i.Id)
            .FirstOrDefault();

        return new ReceiptSelectionOutcome(maintenanceId, status, noticeIds);
    }
}
