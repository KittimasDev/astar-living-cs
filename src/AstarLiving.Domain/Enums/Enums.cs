namespace AstarLiving.Domain.Enums;

/// <summary>ตรงกับ postgres enum "UserRole" ในตาราง gv_users</summary>
public enum UserRole
{
    /// <summary>Astar (ผู้ดูแลระบบสูงสุด)</summary>
    SUPERADMIN,
    /// <summary>ผู้จัดการโครงการ</summary>
    ADMIN,
    /// <summary>หัวหน้านิติ</summary>
    MANAGER,
    /// <summary>นิติ</summary>
    USER,
}

/// <summary>ตรงกับ postgres enum "PaymentStatus" ในตาราง gv_payments</summary>
public enum PaymentStatus
{
    PAID,
    NOT_PAY,
    PARTIAL_PAY,
    CANCELLED,
}

/// <summary>ตรงกับ postgres enum "NoticeType" ในตาราง gv_collection_notices</summary>
public enum NoticeType
{
    /// <summary>ค่าเบี้ยปรับ</summary>
    FINE,
    /// <summary>ค่าทวงถาม</summary>
    COLLECTION,
}

/// <summary>ตรงกับ postgres enum "NoticeStatus" ในตาราง gv_collection_notices</summary>
public enum NoticeStatus
{
    PENDING,
    PAID,
    WAITING,
    CANCELLED,
}

/// <summary>ตัวกรองพิเศษของ FilterPaymentDto เดิม (query string "type")</summary>
public enum PaymentFilterType
{
    OVERDUE,
    RECEIPT,
}

/// <summary>ชนิดรายงานของ GET /v1/projects/:id/reports/payments?type=</summary>
public enum ReportType
{
    REPORT_OUTSTANDING,
    REPORT_PAYMENT,
    REPORT_INVOICE,
    REPORT_FINE,
    REPORT_REVENUE,
}

/// <summary>ชนิดรายการในใบเสร็จ (ReceiptPreviewItemDto.type เดิม)</summary>
public enum ReceiptItemType
{
    MAINTENANCE,
    FINE,
    COLLECTION,
}
