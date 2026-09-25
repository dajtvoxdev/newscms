using AdVideo.Core.Enums;

namespace AdVideo.Api.Contracts;

/// <summary>Request đã kiểm và đã quy về kiểu trong miền. Chỉ tồn tại khi không còn lỗi nào.</summary>
public sealed record ValidatedRequest(
    string Prompt,
    string? ProductName,
    string Script,
    int DurationSeconds,
    AspectRatio AspectRatio,
    VideoTier Tier,
    bool HasPerson,
    bool WantsSoundEffects,
    IReadOnlyList<string> ProductImages,
    IReadOnlyList<string> SceneReferences,
    string? TalentImageUrl,
    string? ConsentRef,
    string? ForcedProvider,
    decimal? MaxCostUsd,
    string? VoiceProfileId,
    double VoiceSpeed,
    string? CallbackUrl);

/// <summary>
/// Kiểm request tạo job. Trả về <b>mọi</b> lỗi cùng lúc.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao gom hết lỗi thay vì dừng ở lỗi đầu tiên.</b> Một request sai ba chỗ mà mỗi lần gọi
/// chỉ báo một chỗ là ba vòng thử — và mỗi vòng, người tích hợp lại phải đoán xem còn gì nữa.
/// </para>
/// <para>
/// <b>Thông báo lỗi viết cho người đọc, bằng tiếng Việt, nói rõ phải sửa gì.</b> "Trường
/// aspect_ratio không hợp lệ" là vô dụng; "aspect_ratio chỉ nhận 9:16, 1:1 hoặc 16:9" thì sửa được ngay.
/// </para>
/// </remarks>
public static class CreateAdVideoRequestValidator
{
    /// <summary>Giới hạn thời lượng theo tài liệu thiết kế.</summary>
    public const int MinDurationSeconds = 6;
    public const int MaxDurationSeconds = 180;

    /// <summary>Trần số ảnh nhận vào, để một request không kéo về hàng trăm file.</summary>
    public const int MaxProductImages = 10;

    public static (ValidatedRequest? Request, IDictionary<string, string[]> Errors) Validate(
        CreateAdVideoRequest? request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void AddError(string field, string message)
        {
            if (!errors.TryGetValue(field, out List<string>? list))
            {
                list = [];
                errors[field] = list;
            }

            list.Add(message);
        }

        if (request is null)
        {
            AddError("$", "Thân request rỗng hoặc không phải JSON hợp lệ.");

            return (null, ToArrays(errors));
        }

        // --- brief ---
        string prompt = request.Brief?.Prompt?.Trim() ?? string.Empty;

        if (prompt.Length == 0)
        {
            AddError("brief.prompt", "Bắt buộc. Mô tả bằng tiếng Việt thường điều muốn quảng cáo.");
        }
        else if (prompt.Length > 4000)
        {
            AddError("brief.prompt", "Tối đa 4000 ký tự.");
        }

        int duration = request.Brief?.DurationSeconds ?? 8;

        if (duration is < MinDurationSeconds or > MaxDurationSeconds)
        {
            AddError(
                "brief.duration_seconds",
                $"Phải trong khoảng {MinDurationSeconds}–{MaxDurationSeconds} giây. Bỏ trống thì mặc định 8 giây.");
        }

        AspectRatio aspectRatio = AspectRatio.Portrait9x16;
        string? aspectRaw = request.Brief?.AspectRatio;

        if (!string.IsNullOrWhiteSpace(aspectRaw) && !TryParseAspectRatio(aspectRaw, out aspectRatio))
        {
            AddError("brief.aspect_ratio", "Chỉ nhận \"9:16\", \"1:1\" hoặc \"16:9\". Bỏ trống thì mặc định 9:16.");
        }

        string language = request.Brief?.Language?.Trim() ?? "vi";

        if (!string.Equals(language, "vi", StringComparison.OrdinalIgnoreCase))
        {
            // Không phải giới hạn tạm thời: toàn bộ hệ thống TTS, từ điển phát âm và quy tắc vẽ
            // chữ đều dựng quanh tiếng Việt. Nhận "en" rồi đọc bằng giọng Việt là một kết quả tệ
            // giao cho khách mà không báo trước.
            AddError("brief.language", "Sprint này chỉ hỗ trợ \"vi\".");
        }

        // --- assets ---
        IReadOnlyList<string> productImages = NormalizeUrls(request.Assets?.ProductImages);

        if (productImages.Count == 0)
        {
            AddError("assets.product_images", "Bắt buộc ít nhất một ảnh sản phẩm (URL http/https).");
        }
        else if (productImages.Count > MaxProductImages)
        {
            AddError("assets.product_images", $"Tối đa {MaxProductImages} ảnh.");
        }

        foreach (string url in productImages.Where(u => !IsHttpUrl(u)))
        {
            AddError("assets.product_images", $"\"{url}\" không phải URL http/https.");
        }

        string? talentImage = request.Assets?.Talent?.ImageUrl?.Trim();
        string? consentRef = request.Assets?.Talent?.ConsentRef?.Trim();

        if (!string.IsNullOrEmpty(talentImage))
        {
            if (!IsHttpUrl(talentImage))
            {
                AddError("assets.talent.image_url", "Không phải URL http/https.");
            }

            if (string.IsNullOrEmpty(consentRef))
            {
                AddError(
                    "assets.talent.consent_ref",
                    "Bắt buộc khi có ảnh người thật. Đây là bằng chứng được phép dùng hình ảnh người đó — " +
                    "thiếu nó thì hệ thống không được phép render.");
            }
        }

        // --- voice ---
        string script = request.Voice?.Script?.Trim() ?? string.Empty;

        if (script.Length == 0)
        {
            AddError(
                "voice.script",
                "Bắt buộc ở sprint này. Bước LLM tự viết lời thoại thuộc sprint sau, nên lời đọc phải do người gửi cung cấp.");
        }
        else if (script.Length > 5000)
        {
            AddError("voice.script", "Tối đa 5000 ký tự.");
        }

        double speed = request.Voice?.Speed ?? 1.0;

        if (speed is < 0.5 or > 2.0)
        {
            AddError("voice.speed", "Phải trong khoảng 0.5–2.0.");
        }

        // --- audio ---
        bool wantsSfx = false;
        string? nativeSound = request.Audio?.NativeSound?.Trim();

        if (!string.IsNullOrEmpty(nativeSound))
        {
            switch (nativeSound.ToLowerInvariant())
            {
                case "off":
                    break;

                case "sfx_only":
                case "full":
                    // "full" được nhận nhưng hành xử như "sfx_only": giữ nguyên thoại của model
                    // trong khi đã có voice-over là hai lớp tiếng chồng nhau (D3).
                    wantsSfx = true;
                    break;

                default:
                    AddError("audio.native_sound", "Chỉ nhận \"off\", \"sfx_only\" hoặc \"full\".");
                    break;
            }
        }

        // --- options ---
        VideoTier tier = VideoTier.Standard;
        string? quality = request.Options?.Quality?.Trim();

        if (!string.IsNullOrEmpty(quality) && !TryParseTier(quality, out tier))
        {
            AddError("options.quality", "Chỉ nhận \"draft\", \"standard\" hoặc \"premium\".");
        }

        decimal? maxCost = request.Options?.MaxCostUsd;

        if (maxCost is <= 0)
        {
            AddError("options.max_cost_usd", "Phải lớn hơn 0, hoặc bỏ trống để dùng trần của hệ thống.");
        }

        string? callbackUrl = request.CallbackUrl?.Trim();

        if (!string.IsNullOrEmpty(callbackUrl) && !IsHttpUrl(callbackUrl))
        {
            AddError("callback_url", "Không phải URL http/https.");
        }

        if (errors.Count > 0)
        {
            return (null, ToArrays(errors));
        }

        return (
            new ValidatedRequest(
                prompt,
                request.Brief?.ProductName?.Trim(),
                script,
                duration,
                aspectRatio,
                tier,
                request.Options?.HasPerson ?? !string.IsNullOrEmpty(talentImage),
                wantsSfx,
                productImages,
                NormalizeUrls(request.Assets?.SceneReference),
                string.IsNullOrEmpty(talentImage) ? null : talentImage,
                string.IsNullOrEmpty(consentRef) ? null : consentRef,
                string.IsNullOrWhiteSpace(request.Options?.Provider) ? null : request.Options.Provider.Trim(),
                maxCost,
                string.IsNullOrWhiteSpace(request.Voice?.VoiceProfileId) ? null : request.Voice.VoiceProfileId.Trim(),
                speed,
                string.IsNullOrEmpty(callbackUrl) ? null : callbackUrl),
            ToArrays(errors));
    }

    public static bool TryParseAspectRatio(string value, out AspectRatio ratio)
    {
        switch (value.Trim())
        {
            case "9:16":
                ratio = AspectRatio.Portrait9x16;
                return true;

            case "1:1":
                ratio = AspectRatio.Square1x1;
                return true;

            case "16:9":
                ratio = AspectRatio.Landscape16x9;
                return true;

            default:
                ratio = AspectRatio.Portrait9x16;
                return false;
        }
    }

    public static bool TryParseTier(string value, out VideoTier tier)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "draft":
                tier = VideoTier.Draft;
                return true;

            case "standard":
                tier = VideoTier.Standard;
                return true;

            case "premium":
                tier = VideoTier.Premium;
                return true;

            default:
                tier = VideoTier.Standard;
                return false;
        }
    }

    private static IReadOnlyList<string> NormalizeUrls(IList<string>? urls) =>
        urls is null
            ? []
            : urls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToList();

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static IDictionary<string, string[]> ToArrays(Dictionary<string, List<string>> errors) =>
        errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal);
}
