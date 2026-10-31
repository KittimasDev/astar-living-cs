namespace AstarLiving.Shared.Api;

/// <summary>ผลการคำนวณช่วงข้อมูลที่จะดึง — ตรงกับ PaginationService.calculatePagination ของเดิม</summary>
public readonly record struct PaginationWindow(int? Limit, int? Skip, string? OrderField, string? OrderType);

/// <summary>การแบ่งหน้าและการเรียง — พอร์ตจาก src/common/service/pagination.service.ts</summary>
public static class Pagination
{
    public const int DefaultLimit = 10;

    /// <summary>no_limit = true จะไม่จำกัดจำนวนแถว และยุบผลลัพธ์ให้เหลือหน้าเดียว</summary>
    public static PaginationWindow Calculate(
        int limit = DefaultLimit,
        int page = 1,
        string? orderField = null,
        string? orderType = null,
        bool noLimit = false)
    {
        if (noLimit) return new PaginationWindow(null, null, orderField, orderType);

        return new PaginationWindow(limit, (page - 1) * limit, orderField, orderType);
    }

    /// <summary>
    /// สร้าง page_information — ตรงกับ calculatePageInfo ของเดิม
    /// </summary>
    /// <param name="count">จำนวนแถวทั้งหมดที่ตรงเงื่อนไข</param>
    /// <param name="length">จำนวนแถวที่คืนจริงในหน้านี้</param>
    public static PageInformation BuildPageInfo(
        string? orderField,
        string? orderType,
        int count,
        int length,
        int limit,
        int page,
        bool noLimit = false)
    {
        if (noLimit)
        {
            return new PageInformation
            {
                Total = count,
                Size = count,
                Count = count,
                Page = 1,
                TotalPage = 1,
                NextPage = false,
                PrevPage = false,
                OrderField = orderField,
                OrderType = orderType,
            };
        }

        var totalPage = limit <= 0 ? 0 : (int)Math.Ceiling(count / (double)limit);

        return new PageInformation
        {
            Total = count,
            Size = limit,
            Count = length,
            Page = page,
            TotalPage = totalPage,
            NextPage = page < totalPage,
            PrevPage = page > 1,
            OrderField = orderField,
            OrderType = orderType,
        };
    }
}

/// <summary>
/// ตัวกรองพื้นฐานที่ทุก endpoint แบบรายการรับเหมือนกัน — ตรงกับ BaseFilterDto ของเดิม
/// </summary>
public class BaseFilter
{
    public int Limit { get; set; } = Pagination.DefaultLimit;

    public int Page { get; set; } = 1;

    public bool? Active { get; set; }

    public bool NoLimit { get; set; }

    public string? OrderField { get; set; }

    /// <summary>"ASC" หรือ "DESC"</summary>
    public string? OrderType { get; set; }
}
