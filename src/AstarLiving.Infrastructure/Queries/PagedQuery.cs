using AstarLiving.Shared.Api;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Infrastructure.Queries;

/// <summary>ผลลัพธ์รายการพร้อมข้อมูลการแบ่งหน้า</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, PageInformation PageInformation);

public static class PagedQuery
{
    /// <summary>
    /// นับจำนวนทั้งหมด เรียง แล้วตัดหน้า — ลำดับเดียวกับ findAndCount ของ TypeORM
    /// รองรับ no_limit ที่ยุบผลลัพธ์เหลือหน้าเดียวเหมือนของเดิม
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedListAsync<T>(
        this IQueryable<T> query,
        SortMap<T> sortMap,
        BaseFilter filter,
        CancellationToken ct = default)
    {
        var (field, type) = sortMap.Resolve(filter.OrderField, filter.OrderType);
        var ordered = sortMap.Apply(query, filter.OrderField, filter.OrderType);

        var total = await query.CountAsync(ct);

        if (filter.NoLimit)
        {
            var allItems = await ordered.ToListAsync(ct);
            return new PagedResult<T>(
                allItems,
                Pagination.BuildPageInfo(field, type, total, allItems.Count, allItems.Count, 1, noLimit: true));
        }

        var window = Pagination.Calculate(filter.Limit, filter.Page, field, type);
        var items = await ordered.Skip(window.Skip ?? 0).Take(window.Limit ?? Pagination.DefaultLimit).ToListAsync(ct);

        return new PagedResult<T>(
            items,
            Pagination.BuildPageInfo(field, type, total, items.Count, filter.Limit, filter.Page));
    }

    /// <summary>
    /// ตัวกรองข้อความแบบ ILIKE %ค่า% ตรงกับ ILike ของ TypeORM
    /// ค่าว่างหรือช่องว่างล้วนถือว่าไม่กรอง และตัดช่องว่างหัวท้ายก่อนเสมอเหมือนของเดิม
    /// </summary>
    public static IQueryable<T> WhereContains<T>(
        this IQueryable<T> query,
        string? value,
        System.Linq.Expressions.Expression<Func<T, string?>> selector)
    {
        if (string.IsNullOrWhiteSpace(value)) return query;

        var pattern = $"%{value.Trim()}%";
        var parameter = selector.Parameters[0];
        var body = System.Linq.Expressions.Expression.Call(
            typeof(NpgsqlDbFunctionsExtensions).GetMethod(
                nameof(NpgsqlDbFunctionsExtensions.ILike),
                [typeof(DbFunctions), typeof(string), typeof(string)])!,
            System.Linq.Expressions.Expression.Constant(EF.Functions),
            selector.Body,
            System.Linq.Expressions.Expression.Constant(pattern));

        return query.Where(
            System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, parameter));
    }
}
