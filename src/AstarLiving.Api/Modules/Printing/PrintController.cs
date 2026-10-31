using AstarLiving.Api.Auth;
using AstarLiving.Api.Modules.External;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AstarLiving.Api.Modules.Printing;

/// <summary>
/// เส้นทางพิมพ์เอกสารทุกชนิด
///
/// ทุกเส้นทางคืนไฟล์ PDF โดยตรง ไม่ห่อซองตอบกลับ และใช้ Content-Disposition แบบ inline
/// เพื่อให้หน้าจอเปิดดูในตัวเองได้ก่อนตัดสินใจพิมพ์หรือดาวน์โหลด เหมือนระบบเดิม
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Operations)]
[Tags("Print")]
public class PrintController(PrintService service, ICurrentUserAccessor currentUser) : ControllerBase
{
    private const string Pdf = "application/pdf";

    private readonly PrintService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    /// <summary>ใบแจ้งหนี้ตามเงื่อนไขที่ระบุ อาจได้หลายใบในไฟล์เดียว</summary>
    [HttpPost("v1/projects/{projectId:int}/payments/invoices/pdf")]
    public async Task<IActionResult> InvoicePdf(
        int projectId, [FromBody] GenerateInvoicePdfRequest request, CancellationToken ct)
    {
        var bytes = await _service.GenerateInvoicePdfAsync(projectId, request, _currentUser.Require(), ct);
        return Inline(bytes, $"invoices-{projectId}.pdf");
    }

    /// <summary>ใบแจ้งหนี้ของงวดเดียวที่ระบุ</summary>
    [HttpPost("v1/projects/{projectId:int}/payments/invoices/pdf/{paymentId:int}")]
    public async Task<IActionResult> InvoicePdfById(
        int projectId, int paymentId, [FromBody] GenerateInvoicePdfRequest request, CancellationToken ct)
    {
        request.PaymentId = paymentId;
        var bytes = await _service.GenerateInvoicePdfAsync(projectId, request, _currentUser.Require(), ct);
        return Inline(bytes, $"invoices-{projectId}.pdf");
    }

    /// <summary>ใบแจ้งหนี้ล่วงหน้า</summary>
    [HttpPost("v1/projects/{projectId:int}/payments/advance-invoices/pdf")]
    public async Task<IActionResult> AdvanceInvoicePdf(
        int projectId, [FromBody] GenerateInvoicePdfRequest request, CancellationToken ct)
    {
        var invNo = request.InvNo ?? throw Common.AppException.BadRequest("กรุณาระบุเลขที่ใบแจ้งหนี้");
        var bytes = await _service.GenerateAdvanceInvoicePdfAsync(projectId, invNo, _currentUser.Require(), ct);
        return Inline(bytes, $"advance-invoice-{invNo}.pdf");
    }

    /// <summary>ใบเสร็จรับเงินค่าส่วนกลาง</summary>
    [HttpGet("v1/receipts/{id:int}/print")]
    public async Task<IActionResult> ReceiptPdf(int id, CancellationToken ct)
    {
        var bytes = await _service.GenerateReceiptPdfAsync(id, _currentUser.Require(), ct);
        return Inline(bytes, $"receipt-{id}.pdf");
    }

    /// <summary>ใบทวงถามใบเดียว</summary>
    [HttpGet("v1/collections/{id:int}/print")]
    public async Task<IActionResult> CollectionPdf(int id, CancellationToken ct)
    {
        var bytes = await _service.GenerateCollectionPdfAsync([id], _currentUser.Require(), ct);
        return Inline(bytes, $"collection-{id}.pdf");
    }

    /// <summary>ใบทวงถามหลายใบรวมในไฟล์เดียว (หน้าจอเลือกหลายแถวแล้วสั่งพิมพ์)</summary>
    [HttpPost("v1/collections/print")]
    public async Task<IActionResult> CollectionPdfBulk(
        [FromBody] PrintCollectionRequest request, CancellationToken ct)
    {
        var bytes = await _service.GenerateCollectionPdfAsync(
            request.CollectionIds, _currentUser.Require(), ct);

        return Inline(bytes, "collections.pdf");
    }

    /// <summary>ใบเสร็จรายรับอื่น ๆ</summary>
    [HttpGet("transactions/revenues/{id:int}/print")]
    public async Task<IActionResult> RevenueReceiptPdf(int id, CancellationToken ct)
    {
        var bytes = await _service.GenerateRevenueReceiptPdfAsync(id, _currentUser.Require(), ct);
        return Inline(bytes, $"transaction-{id}.pdf");
    }

    /// <summary>บันทึกตารางรายงานเป็นไฟล์ Excel</summary>
    [HttpPost("v1/reports/xlsx")]
    public async Task<IActionResult> ReportWorkbook(
        [FromBody] ReportPrintRequest request, CancellationToken ct)
    {
        var bytes = await _service.GenerateReportWorkbookAsync(request, _currentUser.Require(), ct);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "report.xlsx");
    }

    /// <summary>พิมพ์ตารางรายงานตามที่หน้าจอกำลังแสดงอยู่</summary>
    [HttpPost("v1/reports/pdf")]
    public async Task<IActionResult> ReportPdf(
        [FromBody] ReportPrintRequest request, CancellationToken ct)
    {
        var bytes = await _service.GenerateReportPdfAsync(request, _currentUser.Require(), ct);
        return Inline(bytes, "report.pdf");
    }

    /// <summary>สรุปค่าใช้จ่ายทั้งโครงการ</summary>
    [HttpGet("v1/summary_expenses/pdf")]
    public async Task<IActionResult> SummaryExpensesPdf(
        [FromQuery] SummaryExpensesFilterQuery filter, CancellationToken ct)
    {
        var bytes = await _service.GenerateSummaryExpensesPdfAsync(filter, _currentUser.Require(), ct);
        return Inline(bytes, "summary-expenses-all.pdf");
    }

    /// <summary>สรุปค่าใช้จ่ายของบ้านหลังเดียว</summary>
    [HttpGet("v1/summary_expenses/{residentId:int}/pdf")]
    public async Task<IActionResult> SummaryExpensesByResidentPdf(
        int residentId, [FromQuery] SummaryExpensesFilterQuery filter, CancellationToken ct)
    {
        var bytes = await _service.GenerateSummaryExpensesByResidentPdfAsync(
            residentId, filter, _currentUser.Require(), ct);

        return Inline(bytes, $"summary-expenses-{residentId}.pdf");
    }

    /// <summary>รายงานค้างชำระแจ้งลูกบ้านเป็นไฟล์ Excel สองชีต</summary>
    [HttpGet("v1/summary_expenses/{residentId:int}/xlsx")]
    public async Task<IActionResult> ArrearsResidentWorkbook(
        int residentId, [FromQuery] SummaryExpensesFilterQuery filter, CancellationToken ct)
    {
        var bytes = await _service.GenerateArrearsResidentWorkbookAsync(
            residentId, filter, _currentUser.Require(), ct);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"arrears-resident-{residentId}.xlsx");
    }

    private FileContentResult Inline(byte[] bytes, string fileName)
    {
        Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
        return File(bytes, Pdf);
    }
}
