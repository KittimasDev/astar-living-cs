using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AstarLiving.Api.Modules.Internal;

/// <summary>
/// พอร์ตจาก src/modules/internal/payment/payment.controller.ts
///
/// หมายเหตุเรื่องลำดับเส้นทาง: ระบบเดิมประกาศ GET /payments/:id ไว้ก่อน /payments/summary
/// ทำให้คำว่า summary ถูกจับเป็น id และเส้นทางนั้นเรียกไม่ได้เลย (ตารางบั๊กข้อ 6)
/// ที่นี่ใช้ข้อจำกัด {id:int} จึงไม่ชนกัน และทั้งสองเส้นทางใช้งานได้จริง
/// </summary>
[ApiController]
[Route("v1")]
[Authorize(Policy = AuthorizationPolicies.Operations)]
[Tags("Payment (Transaction)")]
public class PaymentController(
    PaymentService payments,
    InvoiceIssuingService issuing,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    private readonly PaymentService _payments = payments;
    private readonly InvoiceIssuingService _issuing = issuing;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    /// <summary>บันทึกงวดค่าส่วนกลางรายตัว</summary>
    [HttpPost("payments")]
    public Task<ApiResponse<PaymentResponse>> Create(
        [FromBody] CreatePaymentRequest request, CancellationToken ct) =>
        _payments.CreateAsync(request, _currentUser.Require(), ct);

    /// <summary>พรีวิวรายการบ้านและยอดก่อนกดออกใบแจ้งหนี้</summary>
    [HttpPost("projects/{projectId:int}/payments/invoices")]
    public Task<ApiResponse<IReadOnlyList<InvoicePreviewRow>>> GetInvoiceList(
        int projectId,
        [FromQuery] BaseFilterQuery filter,
        [FromBody] CreateInvoicesRequest payload,
        CancellationToken ct) =>
        _issuing.GetInvoiceListAsync(projectId, filter, payload, ct);

    /// <summary>ออกใบแจ้งหนี้ทั้งงวด</summary>
    [HttpPost("projects/{projectId:int}/payments/invoices/create")]
    public Task<ApiResponse<CreateInvoicesResult>> CreateInvoices(
        int projectId, [FromBody] CreateInvoicesRequest payload, CancellationToken ct) =>
        _issuing.CreateInvoicesAsync(projectId, payload, _currentUser.Require(), ct);

    /// <summary>พรีวิวใบแจ้งหนี้ล่วงหน้า (ยังไม่บันทึก)</summary>
    [HttpPost("projects/{projectId:int}/payments/advance-invoices/preview")]
    public Task<ApiResponse<AdvanceInvoicePreviewResponse>> PreviewAdvance(
        int projectId, [FromBody] AdvanceInvoicePreviewRequest request, CancellationToken ct) =>
        _issuing.PreviewAdvanceAsync(projectId, request, ct);

    /// <summary>ปีที่เริ่มออกใบแจ้งหนี้ล่วงหน้าได้</summary>
    [HttpPost("projects/{projectId:int}/payments/advance-invoices/year")]
    public Task<ApiResponse<AdvanceInvoiceYearResponse>> PreviewAdvanceYear(
        int projectId, [FromBody] AdvanceInvoicePreviewRequest request, CancellationToken ct) =>
        _issuing.PreviewYearAsync(projectId, request, ct);

    /// <summary>เดือนที่ยังออกใบแจ้งหนี้ล่วงหน้าได้</summary>
    [HttpPost("projects/{projectId:int}/payments/advance-invoices/months")]
    public Task<ApiResponse<IReadOnlyList<AdvanceInvoiceMonthOption>>> PreviewAdvanceMonths(
        int projectId, [FromBody] AdvanceInvoicePreviewRequest request, CancellationToken ct) =>
        _issuing.PreviewMonthsAsync(projectId, request, ct);

    /// <summary>สร้างใบแจ้งหนี้ล่วงหน้า</summary>
    [HttpPost("projects/{projectId:int}/payments/advance-invoices")]
    public Task<ApiResponse<AdvanceInvoiceCreateResult>> CreateAdvance(
        int projectId, [FromBody] AdvanceInvoiceCreateRequest request, CancellationToken ct) =>
        _issuing.CreateAdvanceAsync(projectId, request, _currentUser.Require(), ct);

    /// <summary>รายการงวดค่าส่วนกลางของโครงการ (total_due คำนวณใหม่ตอนอ่าน)</summary>
    [HttpGet("projects/{projectId:int}/payments")]
    public Task<ApiResponse<IReadOnlyList<PaymentResponse>>> FindAllByProject(
        int projectId, [FromQuery] PaymentFilterQuery filter, CancellationToken ct) =>
        _payments.FindAllByProjectAsync(projectId, filter, ct);

    /// <summary>บ้านที่มี/ไม่มีงวดค้าง ตามค่า type</summary>
    [HttpGet("projects/{projectId:int}/payments/residents")]
    public Task<ApiResponse<IReadOnlyList<ResidentBrief>>> GetResidents(
        int projectId, [FromQuery] PaymentFilterQuery filter, CancellationToken ct) =>
        _payments.GetResidentsAsync(projectId, filter, ct);

    /// <summary>รายการทวงถามของเดือนก่อนหน้า</summary>
    [HttpGet("projects/{projectId:int}/payments/reminders")]
    public Task<ApiResponse<IReadOnlyList<ReminderRow>>> GetReminders(
        int projectId, [FromQuery] PaymentFilterQuery filter, CancellationToken ct) =>
        _payments.GetRemindersAsync(projectId, filter, ct);

    /// <summary>งวดที่ระบบจะออกใบแจ้งหนี้ให้ครั้งถัดไป</summary>
    [HttpGet("projects/{projectId:int}/payments/month-range")]
    public Task<ApiResponse<InvoicePeriodResponse>> GetMonthRange(int projectId, CancellationToken ct) =>
        _payments.GetMonthRangeAsync(projectId, ct);

    /// <summary>งวดที่ยังค้างชำระทั้งหมด</summary>
    [HttpGet("payments/outstanding")]
    public Task<ApiResponse<IReadOnlyList<PaymentResponse>>> GetOutstanding(CancellationToken ct) =>
        _payments.GetOutstandingAsync(_currentUser.Require(), ct);

    /// <summary>ยอดรวมรายเดือน — เส้นทางนี้ใช้งานไม่ได้เลยในระบบเดิม</summary>
    [HttpGet("payments/summary")]
    public Task<ApiResponse<IReadOnlyList<object>>> GetSummary(CancellationToken ct) =>
        _payments.GetSummaryAsync(_currentUser.Require(), ct);

    [HttpGet("payments/{id:int}")]
    public Task<ApiResponse<PaymentResponse?>> FindOne(int id, CancellationToken ct) =>
        _payments.FindOneAsync(id, ct);

    [HttpPatch("payments/{id:int}")]
    public Task<ApiResponse<PaymentResponse?>> Update(
        int id, [FromBody] UpdatePaymentRequest request, CancellationToken ct) =>
        _payments.UpdateAsync(id, request, _currentUser.Require(), ct);

    [HttpDelete("payments/{id:int}")]
    public Task<ApiResponse<object>> Remove(int id, CancellationToken ct) =>
        _payments.RemoveAsync(id, _currentUser.Require(), ct);

    /// <summary>ลบใบแจ้งหนี้ทั้งงวด พร้อมใบทวงถามและใบเสร็จที่เกี่ยวข้อง</summary>
    [HttpDelete("projects/{projectId:int}/payments/month/{month:int}/{year:int}")]
    public Task<DeleteByMonthResult> DeleteByMonth(
        int projectId, int month, int year, CancellationToken ct) =>
        _payments.DeleteByMonthAsync(projectId, month, year, _currentUser.Require(), ct);
}
