using EasyStock.Server.Helpers;
using System.Text.Json;
using System.Text.Json.Serialization;
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

/// <summary>Timestamps go out as ISO 8601 in UTC with a "Z" (2026-11-12T10:00:00Z), whatever their offset.</summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime);
}
