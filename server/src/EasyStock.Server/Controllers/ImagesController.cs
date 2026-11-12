using EasyStock.Server.Filters;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Configuration;
using EasyStock.Server.DB;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.Enums;
using EasyStock.Server.Helpers;
using EasyStock.Server.Models;
using EasyStock.Server.Handlers.Images;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace EasyStock.Server.Controllers;

[ApiController]
[Route("api/v1/images/{sha256:regex(^[[0-9a-fA-F]]{{64}}$)}")]
public sealed class ImagesController(EasyStockDbContext db, ImageHandler store, TimeProvider time) : ControllerBase
{
    [HttpHead]
    [RequirePermission(Permission.ViewStock)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Head(string sha256, CancellationToken ct)
    {
        sha256 = sha256.ToLowerInvariant();
        if (!await db.Set<ImageRecord>().AnyAsync(i => i.Sha256 == sha256, ct) || !store.Exists(sha256))
            return NotFound();
        Response.Headers.ETag = ETag(sha256).ToString();
        Response.Headers.CacheControl = CacheControl;
        return Ok();
    }

    [HttpGet]
    [RequirePermission(Permission.ViewStock)]
    [Produces("image/jpeg")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string sha256, CancellationToken ct)
    {
        sha256 = sha256.ToLowerInvariant();
        if (!await db.Set<ImageRecord>().AnyAsync(i => i.Sha256 == sha256, ct))
            return NotFound();
        FileStream stream;
        try { stream = store.OpenRead(sha256); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }
        Response.Headers.CacheControl = CacheControl;
        return File(stream, "image/jpeg", lastModified: null, entityTag: ETag(sha256));
    }

    /// <summary>Body: the raw JPEG bytes (Content-Type image/jpeg, at most 5 MB). 201 when new, 200 when already stored.</summary>
    [HttpPut]
    [RequirePermission(Permission.Edit)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [RequestSizeLimit(ImageHandler.MaxBytes + 1)]
    public async Task<IActionResult> Put(string sha256, CancellationToken ct)
    {
        sha256 = sha256.ToLowerInvariant();
        if (!string.Equals(MediaTypeHeaderValue.TryParse(Request.ContentType, out var mt) ? mt.MediaType.Value : null,
                "image/jpeg", StringComparison.OrdinalIgnoreCase))
            return ProblemTypes.Problem(StatusCodes.Status415UnsupportedMediaType, ProblemTypes.UnsupportedMediaType);
        if (Request.ContentLength > ImageHandler.MaxBytes)
            return ProblemTypes.Problem(StatusCodes.Status413PayloadTooLarge, ProblemTypes.ImageTooLarge);

        ImageSaveResult result;
        try { result = await store.SaveAsync(sha256, Request.Body, ct); }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return ProblemTypes.Problem(StatusCodes.Status413PayloadTooLarge, ProblemTypes.ImageTooLarge);
        }

        switch (result.Status)
        {
            case ImageSaveStatus.TooLarge:
                return ProblemTypes.Problem(StatusCodes.Status413PayloadTooLarge, ProblemTypes.ImageTooLarge);
            case ImageSaveStatus.NotJpeg:
                return ProblemTypes.Problem(StatusCodes.Status400BadRequest, ProblemTypes.NotJpeg);
            case ImageSaveStatus.HashMismatch:
                return ProblemTypes.Problem(StatusCodes.Status400BadRequest, ProblemTypes.HashMismatch);
        }

        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO image (sha256, size, created_at) VALUES ({sha256}, {result.Size}, {time.GetUtcNow().ToUnixTimeMilliseconds()}) ON CONFLICT (sha256) DO NOTHING", ct);
        return inserted > 0 ? StatusCode(StatusCodes.Status201Created) : Ok();
    }

    private const string CacheControl = "private, max-age=31536000, immutable";

    private static EntityTagHeaderValue ETag(string sha256) => new($"\"{sha256}\"");
}
