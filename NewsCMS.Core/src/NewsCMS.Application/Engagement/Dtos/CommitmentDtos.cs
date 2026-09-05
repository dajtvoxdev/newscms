namespace NewsCMS.Application.Engagement.Dtos;

public sealed record CommitmentCreateDto(
    string DisplayName,
    string Message,
    string SignatureKind,
    string? SignatureBase64,
    Stream? UploadedImageStream,
    string? UploadedImageFileName,
    long? UploadedImageSize,
    string? UploadedImageContentType
);

public sealed record CommitmentWallItemDto(
    Guid Id,
    string DisplayName,
    string Message,
    string SignatureUrl,
    DateTime CreatedAt
);

public sealed record CommitmentAdminItemDto(
    Guid Id,
    string DisplayName,
    string Message,
    string SignatureUrl,
    string SignatureKind,
    string IpHash,
    bool IsHidden,
    DateTime CreatedAt
);
