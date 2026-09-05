namespace NewsCMS.Web.Areas.Admin.Shared;

/// <summary>
/// Options for the _MediaPicker partial — a drop-in single-file picker for any admin form.
/// </summary>
public class MediaPickerOptions
{
    /// <summary>The model expression name that maps to the hidden input (e.g. "Input.FeaturedImageUrl").</summary>
    public required string For { get; init; }

    /// <summary>Current bound value (URL string). Pre-populates preview on render.</summary>
    public string? Value { get; init; }

    /// <summary>Accepted MIME kinds shown in the file-open dialog. Defaults to images.</summary>
    public string Accept { get; init; } = "image/*";

    /// <summary>Optional label text shown above the picker.</summary>
    public string? Label { get; init; }

    /// <summary>Optional folder id to pass when uploading.</summary>
    public Guid? FolderId { get; init; }
}
