using AstarLiving.Api.Auth;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AstarLiving.Api.Modules.External;

/// <summary>พอร์ตจาก src/modules/external/dashboard/dashboard.controller.ts</summary>
[ApiController]
[Route("v1/dashboard")]
[Authorize(Policy = AuthorizationPolicies.Operations)]
[Tags("Dashboard (External)")]
public class DashboardController(DashboardService service, ICurrentUserAccessor currentUser)
    : ControllerBase
{
    private readonly DashboardService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    /// <summary>ตัวเลขสรุป กราฟรายเดือน กราฟวิธีชำระ และตารางค้างชำระ</summary>
    [HttpGet]
    public Task<ApiResponse<DashboardResponse>> Get(
        [FromQuery] DashboardFilterQuery filter, CancellationToken ct) =>
        _service.GetDashboardAsync(filter, _currentUser.Require(), ct);
}

/// <summary>
/// พอร์ตจาก src/modules/external/report/report.controller.ts
/// ชนิดของรายงานเลือกด้วย query string type
/// </summary>
[ApiController]
[Route("v1/projects/{projectId:int}/reports")]
[Authorize(Policy = AuthorizationPolicies.Operations)]
[Tags("Report (External)")]
public class ReportController(ReportService service) : ControllerBase
{
    private readonly ReportService _service = service;

    /// <summary>
    /// รายงาน 5 ชนิด: REPORT_OUTSTANDING, REPORT_PAYMENT, REPORT_INVOICE,
    /// REPORT_FINE และ REPORT_REVENUE
    /// </summary>
    [HttpGet("payments")]
    public Task<ApiResponse<IReadOnlyList<object>>> GetPaymentReport(
        int projectId, [FromQuery] ReportFilterQuery filter, CancellationToken ct) =>
        _service.GetPaymentReportAsync(projectId, filter, ct);
}

/// <summary>พอร์ตจาก src/modules/external/summary-expenses/summary-expenses.controller.ts</summary>
[ApiController]
[Route("v1/summary_expenses")]
[Authorize(Policy = AuthorizationPolicies.Operations)]
[Tags("SummaryExpenses (External)")]
public class SummaryExpensesController(SummaryExpensesService service, ICurrentUserAccessor currentUser)
    : ControllerBase
{
    private readonly SummaryExpensesService _service = service;
    private readonly ICurrentUserAccessor _currentUser = currentUser;

    /// <summary>ยอดค้างชำระแยกตามบ้าน พร้อมยอดรวมทั้งโครงการ</summary>
    [HttpGet]
    public Task<ApiResponse<SummaryExpensesResponse>> GetSummary(
        [FromQuery] SummaryExpensesFilterQuery filter, CancellationToken ct) =>
        _service.GetSummaryAsync(filter, _currentUser.Require(), ct);

    /// <summary>ยอดค้างของบ้านหลังเดียว แยกตามปี พ.ศ.</summary>
    [HttpGet("{residentId:int}")]
    public Task<ApiResponse<SummaryExpensesByResidentResponse?>> GetByResident(
        int residentId, [FromQuery] SummaryExpensesFilterQuery filter, CancellationToken ct) =>
        _service.GetByResidentAsync(residentId, filter, _currentUser.Require(), ct);
}
