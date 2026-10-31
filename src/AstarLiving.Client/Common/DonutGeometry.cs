using System.Windows;
using System.Windows.Media;

namespace AstarLiving.Client.Common;

/// <summary>
/// สร้างรูปร่างของ "ชิ้น" ในกราฟวงแหวน
///
/// เขียนเองแทนการใช้ไลบรารีกราฟ เพราะกราฟในระบบนี้มีแค่สองแบบและเรียบมาก
/// การดึงไลบรารีกราฟเข้ามาจะพ่วง SkiaSharp ซึ่งเพิ่มขนาดโปรแกรมหลายสิบเมกะไบต์
/// โดยที่ลูกค้าได้ประโยชน์เพิ่มน้อยมาก
/// </summary>
public static class DonutGeometry
{
    /// <summary>
    /// ชิ้นหนึ่งของวงแหวน วัดมุมตามเข็มนาฬิกาโดยเริ่มจากตำแหน่งสิบสองนาฬิกา
    /// </summary>
    /// <param name="startAngle">องศาเริ่มต้น 0 คือสิบสองนาฬิกา</param>
    /// <param name="sweepAngle">องศาที่กิน</param>
    public static Geometry Build(double startAngle, double sweepAngle, Point center, double outerRadius, double innerRadius)
    {
        if (sweepAngle <= 0) return Geometry.Empty;

        // เต็มวงพอดี วาดด้วยส่วนโค้งไม่ได้เพราะจุดเริ่มกับจุดจบทับกัน
        // ต้องใช้วงกลมใหญ่หักวงกลมเล็กแทน
        if (sweepAngle >= 359.999)
        {
            return new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new EllipseGeometry(center, outerRadius, outerRadius),
                new EllipseGeometry(center, innerRadius, innerRadius));
        }

        var endAngle = startAngle + sweepAngle;
        var isLarge = sweepAngle > 180;

        var outerStart = PointOnCircle(center, outerRadius, startAngle);
        var outerEnd = PointOnCircle(center, outerRadius, endAngle);
        var innerEnd = PointOnCircle(center, innerRadius, endAngle);
        var innerStart = PointOnCircle(center, innerRadius, startAngle);

        var figure = new PathFigure { StartPoint = outerStart, IsClosed = true, IsFilled = true };

        figure.Segments.Add(new ArcSegment(
            outerEnd, new Size(outerRadius, outerRadius), 0, isLarge, SweepDirection.Clockwise, true));

        figure.Segments.Add(new LineSegment(innerEnd, true));

        figure.Segments.Add(new ArcSegment(
            innerStart, new Size(innerRadius, innerRadius), 0, isLarge, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();

        return geometry;
    }

    /// <summary>จุดกึ่งกลางของชิ้น ใช้วางตัวเลขกำกับ</summary>
    public static Point LabelPoint(double startAngle, double sweepAngle, Point center, double radius) =>
        PointOnCircle(center, radius, startAngle + (sweepAngle / 2));

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        // ลบ 90 เพื่อให้ 0 องศาอยู่ที่สิบสองนาฬิกา ไม่ใช่สามนาฬิกาแบบคณิตศาสตร์ปกติ
        var radians = (degrees - 90) * Math.PI / 180;

        return new Point(
            center.X + (radius * Math.Cos(radians)),
            center.Y + (radius * Math.Sin(radians)));
    }
}
