using AstarLiving.Api.Auth;
using AstarLiving.Api.Common;
using AstarLiving.Domain.Entities;
using AstarLiving.Infrastructure.Auditing;
using AstarLiving.Infrastructure.Persistence;
using AstarLiving.Infrastructure.Queries;
using AstarLiving.Shared.Api;
using AstarLiving.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AstarLiving.Api.Modules.Master;

/// <summary>พอร์ตจาก src/modules/master/payment-method/payment-method.service.ts</summary>
public class PaymentMethodService(AstarLivingDbContext db, AuditStamp audit)
{
    private static readonly SortMap<PaymentMethod> Sort = new SortMap<PaymentMethod>("id")
        .Add("id", m => m.Id)
        .Add("code", m => m.Code)
        .Add("name", m => m.Name)
        .Add("active", m => m.Active)
        .Add("created_at", m => m.CreatedAt)
        .Add("updated_at", m => m.UpdatedAt);

    private readonly AstarLivingDbContext _db = db;
    private readonly AuditStamp _audit = audit;

    public async Task<ApiResponse<PaymentMethodResponse>> CreateAsync(
        UpsertPaymentMethodRequest request, CurrentUser current, CancellationToken ct)
    {
        var code = request.Code.Trim();

        // คอลัมน์ code มี unique index อยู่ จึงตรวจก่อนเพื่อให้ได้ข้อความไทยแทน error ของฐานข้อมูล
        if (await _db.PaymentMethods.AnyAsync(m => m.Code == code, ct))
        {
            throw AppException.Conflict($"รหัสวิธีการชำระเงิน \"{code}\" ถูกใช้งานแล้ว");
        }

        var method = new PaymentMethod
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description,
            Active = request.Active ?? true,
        };
        _audit.StampCreate(method, current.UserId);

        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Ok(ToResponse(method));
    }

    public async Task<ApiResponse<IReadOnlyList<PaymentMethodResponse>>> FindAllAsync(
        PaymentMethodFilterQuery filter, CancellationToken ct)
    {
        var query = _db.PaymentMethods.AsNoTracking().WhereContains(filter.Name, m => m.Name);
        if (filter.Active is { } active) query = query.Where(m => m.Active == active);

        var page = await query.ToPagedListAsync(Sort, filter.ToFilter(), ct);

        return ApiResponseFactory.CreateFind(
            HttpStatusCatalog.Ok,
            (IReadOnlyList<PaymentMethodResponse>)page.Items.Select(ToResponse).ToList(),
            page.PageInformation);
    }

    public async Task<ApiResponse<PaymentMethodResponse?>> FindOneAsync(int id, CancellationToken ct)
    {
        var method = await _db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);

        return method is null
            ? ApiResponseFactory.NotFound<PaymentMethodResponse>()
            : ApiResponseFactory.Create<PaymentMethodResponse?>(HttpStatusCatalog.Ok, ToResponse(method));
    }

    public async Task<ApiResponse<PaymentMethodResponse?>> UpdateAsync(
        int id, UpsertPaymentMethodRequest request, CurrentUser current, CancellationToken ct)
    {
        var method = await _db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (method is null) return ApiResponseFactory.NotFound<PaymentMethodResponse>();

        var code = request.Code.Trim();
        if (code != method.Code && await _db.PaymentMethods.AnyAsync(m => m.Code == code && m.Id != id, ct))
        {
            throw AppException.Conflict($"รหัสวิธีการชำระเงิน \"{code}\" ถูกใช้งานแล้ว");
        }

        method.Code = code;
        method.Name = request.Name.Trim();
        method.Description = request.Description;
        if (request.Active is { } active) method.Active = active;

        _audit.StampUpdate(method, current.UserId);
        await _db.SaveChangesAsync(ct);

        return ApiResponseFactory.Create<PaymentMethodResponse?>(HttpStatusCatalog.Ok, ToResponse(method));
    }

    public async Task<ApiResponse<object>> RemoveAsync(int id, CurrentUser current, CancellationToken ct)
    {
        var method = await _db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (method is not null)
        {
            _audit.StampDelete(method, current.UserId);
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponseFactory.Ok<object>(new { });
    }

    private static PaymentMethodResponse ToResponse(PaymentMethod m) => new()
    {
        Id = m.Id,
        Code = m.Code,
        Name = m.Name,
        Description = m.Description,
        Active = m.Active,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
    };
}
