using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Server.Configuration;

/// <summary>MVC, JSON and error answers shared by every controller.</summary>
public static class Api
{
    public static IServiceCollection AddEasyStockApi(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new UtcDateTimeOffsetConverter()));
        services.AddProblemDetails();
        services.AddExceptionHandler<ProblemExceptionHandler>();
        // Model validation and unreadable JSON: the app matches on the type, so not the default RFC link.
        services.Configure<ApiBehaviorOptions>(o => o.InvalidModelStateResponseFactory = ctx =>
        {
            var problem = new ValidationProblemDetails(ctx.ModelState)
            {
                Type = ProblemTypesHelper.ValidationFailed,
                Title = ProblemTypesHelper.DefaultTitle(ProblemTypesHelper.ValidationFailed),
                Status = StatusCodes.Status400BadRequest,
            };
            return new BadRequestObjectResult(problem) { ContentTypes = { ProblemTypesHelper.ContentType } };
        });
        return services;
    }
}
