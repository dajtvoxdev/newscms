namespace AdVideo.Core.Enums;

/// <summary>Vòng đời một shot. Tách khỏi <see cref="JobStatus"/> vì shot có retry riêng.</summary>
public enum ShotStatus
{
    Pending = 0,

    /// <summary>Đã gửi provider, đang chờ. Provider video là async nên trạng thái này chiếm phần lớn thời gian job.</summary>
    Rendering = 1,

    /// <summary>Có clip, đã tải về object storage.</summary>
    Rendered = 2,

    /// <summary>Hết lượt retry. Job sẽ fail theo Luật 1 — không ghép video thiếu shot.</summary>
    Failed = 3,

    /// <summary>Đã render lại. Giữ để đếm số lần regenerate — dữ liệu cho Sprint 6.</summary>
    Regenerated = 4,
}
