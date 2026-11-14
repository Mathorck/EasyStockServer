using EasyStock.Server.Helpers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;

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

/// <summary>
/// Brotli and gzip for JSON answers (docs/04-server-implementation-plan.md, decision 32). Compressing a secret that
/// sits next to attacker-controlled input is what makes BREACH work, so /api/v1/auth/* (tokens) is never
/// compressed, which is what makes EnableForHttps safe. Photos are JPEG and are not in the MIME list.
/// </summary>
public static class Compression
{
    private static readonly PathString AuthPath = new("/api/v1/auth");

    public static IServiceCollection AddEasyStockCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Append(ProblemTypesHelper.ContentType);
        });
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        return services;
    }

    public static IApplicationBuilder UseEasyStockCompression(this IApplicationBuilder app) =>
        app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments(AuthPath, StringComparison.OrdinalIgnoreCase),
            branch => branch.UseResponseCompression());
}

/// <summary>Timestamps go out as ISO 8601 in UTC with a "Z" (2026-11-12T10:00:00Z), whatever their offset.</summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime);
}
