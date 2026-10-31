namespace AstarLiving.Domain.Billing;

/// <summary>
/// สูตรคิดเงินพื้นฐาน — พอร์ตตรงจาก src/common/utils/billing/billing.util.ts
/// ไม่มี VAT และไม่มีการปัดเศษระหว่างคำนวณ (ระบบเดิมปัดเฉพาะตอนแสดงผล)
/// </summary>
public static class BillingCalculator
{
    /// <summary>calculateMaintenance() — ค่าส่วนกลาง = อัตราต่อตารางวา x อัตราส่วนกรรมสิทธิ์</summary>
    public static decimal Maintenance(decimal? rate, decimal? ownershipRatio) =>
        (rate ?? 0m) * (ownershipRatio ?? 0m);

    /// <summary>
    /// calculateFine() — ค่าเบี้ยปรับ = ยอดฐาน x (อัตรา% / 100)
    /// คืน 0 เมื่อยอดฐานหรืออัตราไม่เป็นบวก
    /// หมายเหตุ: "ยอดฐาน" ต่างกันระหว่างหน้า preview กับตอนสร้างจริง ดู InvoiceCalculator
    /// </summary>
    public static decimal Fine(decimal? baseAmount, decimal? fineRatePercent)
    {
        var amount = baseAmount ?? 0m;
        var rate = fineRatePercent ?? 0m;
        if (amount <= 0m || rate <= 0m) return 0m;
        return amount * (rate / 100m);
    }

    /// <summary>calculateCollectionFee() — ค่าทวงถามคิดเมื่อบ้านหลังนั้นถูกเลือกเท่านั้น</summary>
    public static decimal CollectionFee(bool isSelected, decimal? collectionRate) =>
        isSelected ? collectionRate ?? 0m : 0m;
}
