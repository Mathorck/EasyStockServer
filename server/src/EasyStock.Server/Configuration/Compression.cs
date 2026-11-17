using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace EasyStock.Server.Configuration;

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
