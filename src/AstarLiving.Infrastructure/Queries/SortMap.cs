namespace AstarLiving.Infrastructure.Queries;

/// <summary>
/// การเรียงลำดับที่อนุญาต — พอร์ตจาก createOrderByFuc ของเดิม
///
/// ต่างจากเดิมตรงที่ของเดิมรับชื่อคอลัมน์อะไรก็ได้จาก query string แล้วส่งให้ TypeORM ตรง ๆ
/// (ชื่อผิดก็พังเป็น 500) ระบบใหม่ใช้บัญชีรายชื่อที่อนุญาต ชื่อที่ไม่รู้จักจะตกไปใช้ค่าเริ่มต้น
/// ทั้งปลอดภัยกว่าและไม่ทำให้หน้าจอพังเพราะพิมพ์ชื่อคอลัมน์ผิด
/// </summary>
public sealed class SortMap<T>
{
    private readonly Dictionary<string, Func<IQueryable<T>, bool, IOrderedQueryable<T>>> _map =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _defaultField;
    private readonly bool _defaultAscending;

    public SortMap(string defaultField, bool defaultAscending = false)
    {
        _defaultField = defaultField;
        _defaultAscending = defaultAscending;
    }

    public SortMap<T> Add<TKey>(string field, System.Linq.Expressions.Expression<Func<T, TKey>> selector)
    {
        _map[field] = (query, ascending) =>
            ascending ? query.OrderBy(selector) : query.OrderByDescending(selector);
        return this;
    }

    /// <summary>ชื่อฟิลด์และทิศทางที่ใช้จริง สำหรับใส่ลงใน page_information</summary>
    public (string Field, string Type) Resolve(string? orderField, string? orderType)
    {
        var field = orderField is not null && _map.ContainsKey(orderField) ? orderField : _defaultField;

        var type = orderType?.ToUpperInvariant() switch
        {
            "ASC" => "ASC",
            "DESC" => "DESC",
            _ => _defaultAscending ? "ASC" : "DESC",
        };

        return (field, type);
    }

    public IQueryable<T> Apply(IQueryable<T> query, string? orderField, string? orderType)
    {
        var (field, type) = Resolve(orderField, orderType);

        if (!_map.TryGetValue(field, out var apply))
        {
            throw new InvalidOperationException(
                $"SortMap<{typeof(T).Name}> ไม่ได้ลงทะเบียนฟิลด์เริ่มต้น \"{field}\" ไว้");
        }

        return apply(query, type == "ASC");
    }
}
