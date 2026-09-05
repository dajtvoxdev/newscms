using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using NewsCMS.Application.Builder;
using NewsCMS.Infrastructure.Site;

namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// Duyệt CompiledHtml của page, tìm các phần tử data-nc-block="{key}" và thay nội dung bằng
/// HTML render từ IDynamicBlock tương ứng. Block không nhận diện → giữ nguyên placeholder
/// (fallback an toàn, không làm vỡ trang). Cache kết quả theo
/// (siteId, blockKey, culture, propsHash, entityId) — vế cuối chỉ có mặt với khối
/// <see cref="BlockDescriptor.EntityScoped"/>, xem chú thích tại chỗ dựng key.
/// </summary>
public sealed class DynamicBlockRenderer
{
    private readonly IDynamicBlockRegistry _registry;
    private readonly IMemoryCache _cache;
    private readonly SiteCacheSignal _signal;

    public DynamicBlockRenderer(IDynamicBlockRegistry registry, IMemoryCache cache, SiteCacheSignal signal)
    {
        _registry = registry;
        _cache = cache;
        _signal = signal;
    }

    /// <summary>
    /// Thay thế tất cả placeholder dynamic block trong HTML. Trả HTML đã render xong.
    /// </summary>
    /// <param name="route">
    /// Entity của URL hiện tại khi HTML này là một TEMPLATE chi tiết (bài viết / sản phẩm /
    /// chuyên mục). Null với trang thường — khối cần entity phải tự hiện placeholder.
    /// </param>
    public async Task<string> RenderAsync(
        string html, Guid siteId, string culture,
        RouteContext? route = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(html)) return html;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Tìm tất cả phần tử có attribute data-nc-block.
        var nodes = doc.DocumentNode.SelectNodes("//*[@data-nc-block]");
        if (nodes is null || nodes.Count == 0) return html;

        foreach (var node in nodes)
        {
            var blockKey = node.GetAttributeValue("data-nc-block", "");
            if (string.IsNullOrWhiteSpace(blockKey)) continue;

            // DeEntitizeValue, KHÔNG GetAttributeValue: serializer lưu JSON thành {&quot;k&quot;:...},
            // đọc thô sẽ parse hỏng và block âm thầm rơi về props mặc định.
            var propsJson = node.Attributes["data-nc-props"]?.DeEntitizeValue;

            // Tra block TRƯỚC khi dựng cache key: chỉ Descriptor mới biết khối có đọc entity
            // hay không, mà điều đó quyết định key. Registry là một dictionary lookup nên rẻ.
            var block = _registry.Get(blockKey);

            // Cache key: block render result phụ thuộc site + culture + props.
            var propsHash = !string.IsNullOrEmpty(propsJson)
                ? Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
                    System.Text.Encoding.UTF8.GetBytes(propsJson)), 0, 8).ToLowerInvariant()
                : "_";

            // …và với khối đọc DynamicBlockContext.Route (EntityScoped) thì phụ thuộc cả entity
            // đang render: cùng một template chi tiết phục vụ MỌI bài viết với props y hệt nhau,
            // nên thiếu vế này là mọi bài hiện nội dung của bài render đầu tiên suốt 5 phút.
            // Khối không đọc entity giữ "_" để vẫn dùng chung một entry cho mọi URL.
            var entityKey = route is not null && block?.Descriptor.EntityScoped == true
                ? route.EntityId.ToString("N")
                : "_";

            var cacheKey = $"dynblock:{siteId}:{blockKey}:{culture}:{propsHash}:{entityKey}";

            string rendered;
            if (_cache.TryGetValue<string>(cacheKey, out var cached))
            {
                rendered = cached;
            }
            else
            {
                if (block is null)
                {
                    // Fallback: giữ nguyên nội dung placeholder, thêm comment để debug.
                    rendered = $"<!-- unknown dynamic block: {System.Net.WebUtility.HtmlEncode(blockKey)} -->" +
                               (node.InnerHtml ?? "");
                }
                else
                {
                    try
                    {
                        var ctx = new DynamicBlockContext(siteId, culture, propsJson, route);
                        rendered = await block.RenderAsync(ctx, ct);
                    }
                    catch
                    {
                        // Block lỗi → fallback an toàn, không vỡ trang.
                        rendered = $"<!-- error rendering block: {System.Net.WebUtility.HtmlEncode(blockKey)} -->";
                    }
                }

                // Cache ngắn hạn (5 phút) — invalidate khi entity nguồn đổi qua signal.
                using var entry = _cache.CreateEntry(cacheKey);
                entry.ExpirationTokens.Add(_signal.Token);
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                entry.Value = rendered;
            }

            // Thay inner HTML của placeholder bằng rendered content.
            node.InnerHtml = rendered;
        }

        return doc.DocumentNode.OuterHtml;
    }
}
