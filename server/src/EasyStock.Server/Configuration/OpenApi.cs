using EasyStock.Server.Filters;
using EasyStock.Server.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace EasyStock.Server.Configuration;

/// <summary>
/// OpenAPI additions that follow from filters and middleware rather than from action signatures: the bearer scheme,
/// which operations need it, the version headers and their 426 answer. server/openapi/v1.json is written from this
/// document on every build.
/// </summary>
public static class OpenApi
{
    public const string BearerScheme = "Bearer";

    public static IServiceCollection AddEasyStockOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi("v1", o =>
        {
            o.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Access token from /auth/login, /auth/refresh or the other token answers.",
                };
                return Task.CompletedTask;
            });
            o.AddOperationTransformer(TransformOperationAsync);
        });
        return services;
    }

    private static async Task TransformOperationAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var description = context.Description;
        var metadata = description.ActionDescriptor.EndpointMetadata;
        var needsPermission = metadata.OfType<RequirePermissionAttribute>().Any();
        var needsSession = needsPermission || metadata.OfType<RequireSessionAttribute>().Any();
        operation.Responses ??= new OpenApiResponses();

        if (metadata.OfType<RawBodyAttribute>().FirstOrDefault() is { } raw)
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = raw.Description,
                Content = raw.ContentTypes.ToDictionary(
                    t => t,
                    t => new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } }),
            };
        }

        if (needsSession)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [],
            });
            await AddProblemAsync(operation, context, StatusCodes.Status401Unauthorized,
                "No valid session: unauthorized, token-expired, session-revoked or account-disabled.", ct);
        }
        if (needsPermission)
            await AddProblemAsync(operation, context, StatusCodes.Status403Forbidden, "The role lacks the permission: not-allowed.", ct);

        if (!VersionHeaderMiddleware.IsExempt(description.HttpMethod ?? "", "/" + description.RelativePath))
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = VersionHeaderMiddleware.AppHeader,
                In = ParameterLocation.Header,
                Required = true,
                Description = $"The app's version, at least {CompatHelper.MinAppVersion}. A pre-release suffix is ignored.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = VersionHeaderMiddleware.SchemaHeader,
                In = ParameterLocation.Header,
                Required = true,
                Description = $"The app's Room DB version. Supported: {string.Join(", ", CompatHelper.SupportedSchemaVersions.Order())}.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            });
            await AddProblemAsync(operation, context, StatusCodes.Status426UpgradeRequired,
                "The app version or schema version is not supported: app-too-old.", ct);
        }
    }

    /// <summary>Adds a Problem Details answer unless the action already declares that status.</summary>
    private static async Task AddProblemAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, int status, string text, CancellationToken ct)
    {
        var key = status.ToString();
        if (operation.Responses!.ContainsKey(key)) return;
        operation.Responses[key] = new OpenApiResponse
        {
            Description = text,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                [ProblemTypesHelper.ContentType] = new()
                {
                    Schema = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), null, ct),
                },
            },
        };
    }
}

/// <summary>Documents a request body that the action reads from <c>Request.Body</c> instead of binding it.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RawBodyAttribute(params string[] contentTypes) : Attribute
{
    public string[] ContentTypes { get; } = contentTypes;
    public string? Description { get; init; }
}
