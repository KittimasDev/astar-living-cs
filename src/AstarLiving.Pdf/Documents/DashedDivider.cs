using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AstarLiving.Pdf.Documents;

/// <summary>
/// เส้นประกลางหน้ากระดาษสำหรับฉีกแยกสำเนา
///
/// QuestPDF รุ่นที่ใช้อยู่ยังไม่มี LineDashPattern บน ILine จึงประกอบเส้นประเอง
/// จากขีดสั้น ๆ สลับช่องว่าง ความยาวขีดและช่องว่างเท่ากับ dash 3pt ของระบบเดิม
/// </summary>
internal static class DashedDivider
{
    private const float DashLength = 3f;
    private const float GapLength = 3f;

    public static void Draw(IContainer container, float totalWidth, float thickness = 1f)
    {
        var segmentCount = (int)Math.Floor(totalWidth / (DashLength + GapLength));

        container.AlignMiddle().Row(row =>
        {
            for (var i = 0; i < segmentCount; i++)
            {
                row.ConstantItem(DashLength, Unit.Point)
                    .LineHorizontal(thickness, Unit.Point).LineColor(Colors.Black);

                row.ConstantItem(GapLength, Unit.Point);
            }
        });
    }
}
