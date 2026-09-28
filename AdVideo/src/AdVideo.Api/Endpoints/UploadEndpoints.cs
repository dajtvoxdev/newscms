using System.Security.Cryptography;
using AdVideo.Api.Auth;
using AdVideo.Core.Entities;
using AdVideo.Core.Enums;
using AdVideo.Core.Media;
using AdVideo.Core.Storage;
using AdVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AdVideo.Api.Endpoints;

/// <summary>Phản hồi của <c>POST /v1/uploads</c>. Dùng <see cref="AssetId"/> trong <c>assets.product_image_ids</c>.</summary>
public sealed record UploadResponse(Guid AssetId, string ContentType, long SizeBytes, string Sha256, bool Reused);

/// <summary>
/// Khách tải ảnh sản phẩm thẳng vào kho của hệ thống (MinIO, bucket <c>adv-uploads</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao cần cửa này khi brief đã nhận URL.</b> URL của khách có thể là link tạm, link cần
/// đăng nhập, hoặc chết ngay sau khi job vào hàng đợi — job fail ở bước 1 vài phút sau khi khách
/// đã rời màn hình. Tải lên trước thì ảnh nằm trong kho của mình từ lúc bấm nút, và app (NewsCMS)
/// không phải mở một đường công khai tới thư viện ảnh của nó.
/// </para>
/// <para>
/// <b>Trùng nội dung thì trả asset cũ</b> (theo SHA-256, trong cùng tenant): người dùng tải lại cùng
/// một ảnh sản phẩm cho mỗi video là thói quen, không phải lỗi, và mỗi lần một bản sao là bucket
/// "giữ lâu dài" phình theo số video chứ không theo số ảnh.
/// </para>
/// </remarks>
public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/uploads", UploadAsync)
            .RequireAuthorization(AuthPolicies.Tenant)

            // Xác thực bằng header, không cookie — không có gì cho CSRF. Thiếu dòng này thì .NET 8
            // đòi middleware antiforgery cho mọi endpoint nhận form.
            .DisableAntiforgery()
            .WithName("UploadProductImage")
            .WithSummary("Tải một ảnh sản phẩm (JPEG/PNG/WebP, tối đa 15 MB). Trả asset_id dùng trong assets.product_image_ids.");

        return app;
    }

    private static async Task<IResult> UploadAsync(
        HttpContext http,
        IFormFile? file,
        AdVideoDbContext db,
        IStorageService storage,
        CancellationToken cancellationToken)
    {
        Guid tenantId = http.User.GetTenantId();

        if (file is null || file.Length == 0)
        {
            return ApiProblem.Unprocessable(http, "Thiếu file ảnh", ["Gửi multipart/form-data với trường \"file\"."]);
        }

        if (file.Length > ProductImageFormat.MaxBytes)
        {
            return ApiProblem.Unprocessable(
                http,
                "Ảnh quá lớn",
                [$"Ảnh nặng {file.Length / 1024 / 1024} MB, tối đa {ProductImageFormat.MaxBytes / 1024 / 1024} MB. Nén hoặc thu nhỏ rồi tải lại."]);
        }

        byte[] bytes;

        using (var buffer = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        DetectedImage? image = ProductImageFormat.Detect(bytes);

        if (image is null)
        {
            return ApiProblem.Unprocessable(
                http,
                "Không phải ảnh dùng được",
                ["Chỉ nhận JPEG, PNG hoặc WebP (kiểm theo nội dung file, không theo đuôi). Ảnh HEIC từ iPhone cần chuyển sang JPEG trước."]);
        }

        string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        // Global filter ghim tenant: trùng SHA ở tenant khác không bao giờ khớp — hai khách tải cùng
        // một ảnh stock vẫn là hai asset, không ai "mượn" được object của người khác.
        MediaAsset? existing = await db.MediaAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Kind == AssetKind.ProductImage && a.JobId == null && a.ChecksumSha256 == sha256,
                cancellationToken);

        if (existing is not null && await storage.ExistsAsync(existing.Bucket, existing.ObjectKey, cancellationToken))
        {
            return Results.Ok(new UploadResponse(existing.Id, existing.ContentType, existing.SizeBytes, sha256, Reused: true));
        }

        var asset = new MediaAsset
        {
            TenantId = tenantId,
            JobId = null,
            Kind = AssetKind.ProductImage,
            Bucket = Buckets.ForKind(AssetKind.ProductImage),
            ObjectKey = string.Empty,
            ContentType = image.ContentType,
            SizeBytes = bytes.LongLength,
            ChecksumSha256 = sha256,
        };

        asset.ObjectKey = IStorageService.BuildKey(tenantId, null, AssetKind.ProductImage, $"{asset.Id:N}{image.Extension}");

        using (var content = new MemoryStream(bytes, writable: false))
        {
            await storage.UploadAsync(asset.Bucket, asset.ObjectKey, content, image.ContentType, cancellationToken);
        }

        // Ghi DB SAU khi file đã nằm trong kho: hàng DB trỏ tới object không tồn tại là thứ tệ hơn
        // một object mồ côi (object mồ côi chỉ tốn chỗ, hàng mồ côi làm job fail).
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/v1/uploads/{asset.Id}",
            new UploadResponse(asset.Id, asset.ContentType, asset.SizeBytes, sha256, Reused: false));
    }
}
