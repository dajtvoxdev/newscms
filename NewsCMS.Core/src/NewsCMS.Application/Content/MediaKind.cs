namespace NewsCMS.Application.Content;

public enum MediaKind
{
    Image,
    Video,
    Audio,
    Document,
    Other
}

public static class MediaKindResolver
{
    public static MediaKind Resolve(string mimeType, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (mimeType.StartsWith("image/") || IsImageExtension(ext))
            return MediaKind.Image;
        if (mimeType.StartsWith("video/") || IsVideoExtension(ext))
            return MediaKind.Video;
        if (mimeType.StartsWith("audio/") || IsAudioExtension(ext))
            return MediaKind.Audio;
        if (IsDocumentExtension(ext))
            return MediaKind.Document;

        return MediaKind.Other;
    }

    public static string ToSubFolder(MediaKind kind) => kind switch
    {
        MediaKind.Image => "images",
        MediaKind.Video => "videos",
        MediaKind.Audio => "audio",
        MediaKind.Document => "documents",
        _ => "general"
    };

    private static bool IsImageExtension(string ext) => ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".svg" or ".bmp";
    private static bool IsVideoExtension(string ext) => ext is ".mp4" or ".webm" or ".ogv" or ".mov" or ".avi" or ".mkv";
    private static bool IsAudioExtension(string ext) => ext is ".mp3" or ".wav" or ".ogg" or ".m4a" or ".aac" or ".flac";
    private static bool IsDocumentExtension(string ext) => ext is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".rtf" or ".zip" or ".mind";
}
