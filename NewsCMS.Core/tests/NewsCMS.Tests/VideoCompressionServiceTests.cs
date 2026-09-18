using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Infrastructure.Content;
using NewsCMS.Infrastructure.Persistence;
using NewsCMS.Infrastructure.Storage;

namespace NewsCMS.Tests;

/// <summary>
/// Hợp đồng của VideoCompressionService: chỉ nén .mp4 lớn hơn ngưỡng, không bao giờ tệ hơn gốc,
/// không throw khi môi trường thiếu ffmpeg. ffmpeg path trỏ tới cài đặt thật trong appsettings
/// (máy dev/prod Windows), test skip nén thật nếu binary không tồn tại.
/// </summary>
public sealed class VideoCompressionServiceTests
{
    private static (VideoCompressionService Svc, AppDbContext Db, string Root) Create(
        Dictionary<string, string?>? overrides = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ncms-vcomp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settings = new Dictionary<string, string?>
        {
            ["Storage:FfmpegPath"] = @"C:\ffmpeg\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe",
            ["Storage:VideoCompression:MinSizeMb"] = "1",
            ["Storage:VideoCompression:TimeoutMinutes"] = "2"
        };
        if (overrides is not null)
            foreach (var kv in overrides) settings[kv.Key] = kv.Value;

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"vcomp-{Guid.NewGuid():N}").Options;
        var db = new AppDbContext(options);
        var storage = new LocalFileStorage(root);
        var env = new FakeHostEnvironment { ContentRootPath = root };
        var svc = new VideoCompressionService(db, storage, cfg, env, NullLogger<VideoCompressionService>.Instance);
        return (svc, db, root);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task BoQua_MediaKhongTonTai()
    {
        var (svc, _, _) = Create();
        var result = await svc.CompressAsync(Guid.NewGuid());
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task BoQua_KhongPhaiVideo()
    {
        var (svc, db, _) = Create();
        var media = new Media
        {
            FileName = "doc.pdf", StorageKey = "documents/2026/01/x.pdf",
            FilePath = "/uploads/documents/2026/01/x.pdf",
            MimeType = "application/pdf", Kind = "document", Size = 99 * 1024 * 1024
        };
        db.Medias.Add(media);
        await db.SaveChangesAsync();

        var result = await svc.CompressAsync(media.Id);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task BoQua_KhongPhaiMp4()
    {
        var (svc, db, _) = Create();
        var media = new Media
        {
            FileName = "clip.mov", StorageKey = "videos/2026/01/x.mov",
            FilePath = "/uploads/videos/2026/01/x.mov",
            MimeType = "video/quicktime", Kind = "video", Size = 99 * 1024 * 1024
        };
        db.Medias.Add(media);
        await db.SaveChangesAsync();

        var result = await svc.CompressAsync(media.Id);
        Assert.False(result.Succeeded);
        Assert.Contains(".mp4", result.Error);
    }

    [Fact]
    public async Task BoQua_FfmpegChuaCauHinh()
    {
        var (svc, db, _) = Create(new() { ["Storage:FfmpegPath"] = "" });
        var media = new Media
        {
            FileName = "clip.mp4", StorageKey = "videos/2026/01/x.mp4",
            FilePath = "/uploads/videos/2026/01/x.mp4",
            MimeType = "video/mp4", Kind = "video", Size = 99 * 1024 * 1024
        };
        db.Medias.Add(media);
        await db.SaveChangesAsync();

        var result = await svc.CompressAsync(media.Id);
        Assert.False(result.Succeeded);
        Assert.Contains("ffmpeg", result.Error);
    }

    [Fact]
    public async Task BoQua_FileNhoHonNguong()
    {
        var (svc, db, root) = Create();
        // Ghi file thật nhỏ hơn ngưỡng 1MB để tới được nhánh check size (trước ffmpeg).
        var dir = Path.Combine(root, "videos", "2026", "01");
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "x.mp4"), new byte[1024]);

        var media = new Media
        {
            FileName = "x.mp4", StorageKey = "videos/2026/01/x.mp4",
            FilePath = "/uploads/videos/2026/01/x.mp4",
            MimeType = "video/mp4", Kind = "video", Size = 1024
        };
        db.Medias.Add(media);
        await db.SaveChangesAsync();

        var result = await svc.CompressAsync(media.Id);
        Assert.False(result.Succeeded);
        Assert.Contains("ngưỡng", result.Error);
    }

    [Fact]
    public async Task TimThayMedia_KhiSiteIdKhacGuidEmpty()
    {
        // Regression: production từng không nén được video nào vì Media có SiteId THẬT còn
        // AppDbContext.CurrentSiteId = Guid.Empty (service chạy ngoài HTTP request) — global
        // query filter SiteId khớp 0 dòng nên CompressAsync luôn báo "Media không tồn tại".
        // Test cũ không bắt được vì để mặc định SiteId = Guid.Empty ở CẢ HAI vế nên khớp nhầm.
        var (svc, db, root) = Create();

        var dir = Path.Combine(root, "videos", "2026", "09");
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "real.mp4"), new byte[1024]);

        var media = new Media
        {
            SiteId = Guid.NewGuid(),   // site thật, KHÁC Guid.Empty
            FileName = "real.mp4", StorageKey = "videos/2026/09/real.mp4",
            FilePath = "/uploads/videos/2026/09/real.mp4",
            MimeType = "video/mp4", Kind = "video", Size = 1024
        };
        db.Medias.Add(media);
        await db.SaveChangesAsync();

        var result = await svc.CompressAsync(media.Id);

        // Không được fail ở bước tra media — phải đi tiếp tới check ngưỡng dung lượng.
        Assert.Contains("ngưỡng", result.Error);
        Assert.DoesNotContain("không tồn tại", result.Error);
    }
}
