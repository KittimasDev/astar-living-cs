using AstarLiving.Domain.Enums;
using Npgsql;
using Npgsql.NameTranslation;

namespace AstarLiving.Infrastructure.Persistence;

/// <summary>
/// ชื่อ enum ใน PostgreSQL ที่ TypeORM สร้างไว้ และการผูกเข้ากับ enum ฝั่ง C#
///
/// สำคัญ: label ในฐานข้อมูลเป็นตัวพิมพ์ใหญ่ทั้งหมด (SUPERADMIN, NOT_PAY, ...) ซึ่งตรงกับ
/// ชื่อสมาชิกของ enum ใน C# พอดี จึงต้องใช้ NpgsqlNullNameTranslator
/// ถ้าปล่อยให้ใช้ตัวแปลชื่อค่าเริ่มต้น (snake_case) จะกลายเป็น "superadmin" แล้วอ่านข้อมูลไม่ออก
/// </summary>
public static class NpgsqlEnums
{
    public const string UserRoleType = "gv_users_role_enum";
    public const string PaymentStatusType = "gv_payments_status_enum";
    public const string NoticeTypeType = "gv_collection_notices_notices_type_enum";
    public const string NoticeStatusType = "gv_collection_notices_status_enum";

    public static readonly INpgsqlNameTranslator Translator = new NpgsqlNullNameTranslator();

    /// <summary>ผูก enum ทั้งหมดเข้ากับ data source (ต้องทำก่อนเปิดการเชื่อมต่อ)</summary>
    public static NpgsqlDataSourceBuilder MapAstarLivingEnums(this NpgsqlDataSourceBuilder builder)
    {
        builder.MapEnum<UserRole>(UserRoleType, Translator);
        builder.MapEnum<PaymentStatus>(PaymentStatusType, Translator);
        builder.MapEnum<NoticeType>(NoticeTypeType, Translator);
        builder.MapEnum<NoticeStatus>(NoticeStatusType, Translator);
        return builder;
    }
}
