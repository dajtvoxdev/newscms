namespace AdVideo.Core.Enums;

/// <summary>Loại asset. Một job đẻ ra 5–10 asset, phần lớn là trung gian và bị xoá sau 7 ngày.</summary>
public enum AssetKind
{
    /// <summary>Ảnh khách upload.</summary>
    ProductImage = 0,

    /// <summary>Ảnh neo dùng để chống trôi hình khi nối frame (Sprint 3).</summary>
    ReferenceImage = 1,

    /// <summary>File giọng đọc + mốc thời gian.</summary>
    VoiceAudio = 2,

    /// <summary>Clip thô do provider trả về, một cái mỗi shot.</summary>
    ShotClip = 3,

    /// <summary>Track tiếng động gốc của provider — giữ lại để QC bước 9 chạy VAD.</summary>
    NativeAudio = 4,

    /// <summary>Nhạc nền. (Sprint 2)</summary>
    MusicBed = 5,

    /// <summary>Video cuối giao khách.</summary>
    FinalVideo = 6,

    /// <summary>Ảnh xem trước cho UI.</summary>
    Thumbnail = 7,

    /// <summary>Phụ đề .srt sinh từ mốc thời gian TTS.</summary>
    Subtitle = 8,
}
