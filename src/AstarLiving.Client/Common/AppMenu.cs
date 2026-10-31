using AstarLiving.Client.ViewModels.Pages;

namespace AstarLiving.Client.Common;

/// <summary>
/// เมนูด้านซ้ายและการกรองตามบทบาท — พอร์ตตรงจาก Navbar.tsx ของเว็บเดิม
///
/// ต่างจากของเดิมตรงที่ฝั่ง API บังคับสิทธิ์จริงด้วย ไม่ได้ซ่อนแค่เมนู
/// การกรองตรงนี้จึงเป็นเรื่องของการใช้งานให้สะดวก ไม่ใช่กำแพงความปลอดภัย
/// </summary>
public static class AppMenu
{
    public const string SuperAdmin = "SUPERADMIN";
    public const string Admin = "ADMIN";
    public const string Manager = "MANAGER";
    public const string User = "USER";

    public static IReadOnlyList<NavigationNode> Build(string role)
    {
        var settingsChildren = BuildSettingsChildren(role);

        // SUPERADMIN ดูแลระบบในภาพรวม จึงได้เมนูตั้งค่า เมนูสำรองฐานข้อมูล
        // และเมนูบันทึกการทำงาน เพื่อให้ตรวจสอบได้ว่าใครเอาข้อมูลออกจากเครื่องไปบ้าง
        if (role == SuperAdmin)
        {
            return
            [
                new NavigationNode("settings", "การตั้งค่า", "IconSettings", null, settingsChildren),
                new NavigationNode("database-backup", "สำรองฐานข้อมูล", "IconDownload", typeof(DatabaseBackupPageViewModel)),
                new NavigationNode("system-logs", "บันทึกการทำงานของระบบ", "IconSystemLogs", typeof(SystemLogsPageViewModel)),
            ];
        }

        var menu = new List<NavigationNode>
        {
            new("dashboard", "แดชบอร์ด", "IconDashboard", typeof(DashboardPageViewModel)),
            new("resident", "ข้อมูลลูกบ้าน / ผู้อยู่อาศัย", "IconResident", typeof(ResidentPageViewModel)),
            new("payment", "ใบแจ้งหนี้", "IconInvoice", typeof(InvoicePageViewModel)),
            new("credit-note", "ลดหนี้", "IconCreditNote", typeof(CreditNotePageViewModel)),
            new("overdue", "รายการค้างชำระ", "IconOverdue", typeof(OverduePageViewModel)),
            new("collection-notice", "ใบทวงถาม", "IconCollectionNotice", typeof(CollectionNoticePageViewModel)),
            new("receipt", "ใบเสร็จรับเงิน", "IconReceipt", typeof(ReceiptPageViewModel)),
            new("report", "รายงาน", "IconReport", typeof(ReportPageViewModel)),
        };

        // USER ไม่เห็นทั้งเมนูการตั้งค่าและบันทึกการทำงานของระบบ
        if (role == User) return menu;

        menu.Add(new NavigationNode("settings", "การตั้งค่า", "IconSettings", null, settingsChildren));
        menu.Add(new NavigationNode("system-logs", "บันทึกการทำงานของระบบ", "IconSystemLogs", typeof(SystemLogsPageViewModel)));

        return menu;
    }

    private static List<NavigationNode> BuildSettingsChildren(string role)
    {
        var project = new NavigationNode("settings-project", "ตั้งค่าโครงการ", "IconProject", typeof(ProjectSettingsPageViewModel));
        var user = new NavigationNode("settings-user", "ผู้ใช้งาน", "IconUser", typeof(UserSettingsPageViewModel));

        if (role == SuperAdmin) return [project, user];

        // ADMIN กับ MANAGER เห็นทุกเมนูย่อยยกเว้นตั้งค่าโครงการ ซึ่งเป็นของ Astar
        if (role is Admin or Manager)
        {
            return
            [
                new NavigationNode("settings-payment-method", "ตั้งค่าวิธีการชำระเงิน", "IconPaymentMethod", typeof(PaymentMethodSettingsPageViewModel)),
                new NavigationNode("settings-project-rate", "ตั้งค่าอัตราค่าบริการ", "IconRate", typeof(RateSettingsPageViewModel)),
                new NavigationNode("settings-collection-notice-settings", "เทมเพลตค่าทวงถาม", "IconTemplate", typeof(NoticeTemplateSettingsPageViewModel)),
                user,
            ];
        }

        return [];
    }

    /// <summary>หน้าแรกหลังเข้าสู่ระบบ — SUPERADMIN ไม่มีแดชบอร์ด จึงไปที่ตั้งค่าโครงการแทน</summary>
    public static Type LandingPage(string role) =>
        role == SuperAdmin ? typeof(ProjectSettingsPageViewModel) : typeof(DashboardPageViewModel);
}
