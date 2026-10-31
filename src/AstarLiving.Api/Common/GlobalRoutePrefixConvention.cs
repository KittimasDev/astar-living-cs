using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace AstarLiving.Api.Common;

/// <summary>
/// เติม prefix "api" ให้ทุกเส้นทาง เทียบเท่า app.setGlobalPrefix('api') ของ NestJS
/// ทำให้ controller เขียน [Route("v1/auth")] ได้ตรงกับโค้ดเดิม แล้วได้ /api/v1/auth
/// </summary>
public class GlobalRoutePrefixConvention(string prefix) : IApplicationModelConvention
{
    private readonly AttributeRouteModel _prefix = new(new Microsoft.AspNetCore.Mvc.RouteAttribute(prefix));

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            foreach (var selector in controller.Selectors.Where(s => s.AttributeRouteModel is not null))
            {
                selector.AttributeRouteModel =
                    AttributeRouteModel.CombineAttributeRouteModel(_prefix, selector.AttributeRouteModel);
            }

            foreach (var selector in controller.Selectors.Where(s => s.AttributeRouteModel is null))
            {
                selector.AttributeRouteModel = _prefix;
            }
        }
    }
}
