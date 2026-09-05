using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsCMS.Domain.Entities.Ai;
using NewsCMS.Domain.Entities.Catalog;
using NewsCMS.Domain.Entities.Content;
using NewsCMS.Domain.Entities.Engagement;
using NewsCMS.Domain.Entities.Identity;
using NewsCMS.Domain.Entities.KeoBia;
using NewsCMS.Domain.Entities.Site;
using NewsCMS.Domain.Enums;
using NewsCMS.Infrastructure.Storage;
using NewsCMS.Shared.Constants;

namespace NewsCMS.Infrastructure.Persistence.Seed;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var userManager = sp.GetRequiredService<UserManager<AppUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<AppRole>>();

        // Seeder chạy ngoài request (chưa resolve site) → tắt scope để tự gán SiteId tường minh.
        db.BypassSiteScope = true;

        await db.Database.MigrateAsync();

        var existingCodes = await db.Permissions.Select(p => p.Code).ToListAsync();
        foreach (var (code, module, name) in Permissions.All())
        {
            if (!existingCodes.Contains(code))
                db.Permissions.Add(new Permission { Code = code, DisplayName = name, Module = module });
        }
        await db.SaveChangesAsync();

        // Quyền đã nghỉ cùng module Trang tĩnh cũ: dọn khỏi DB để không còn hiện
        // trong màn hình phân quyền (Roles/Edit group theo Module đọc từ DB).
        await RemoveRetiredPermissionAsync(db, "Site.Page.Manage");

        await EnsureRole(roleManager, "SuperAdmin", "Toàn quyền hệ thống", isSystem: true);
        await EnsureRole(roleManager, "Admin", "Quản trị viên site");
        await EnsureRole(roleManager, "Editor", "Biên tập viên");
        await EnsureRole(roleManager, "Author", "Cộng tác viên");

        var newsModule = await EnsureFeatureModule(db, "news", "Tin tức", "Quản lý bài viết, chuyên mục, tag và media", isSystem: true, sortOrder: 10);
        var productsModule = await EnsureFeatureModule(db, "products", "Sản phẩm", "Quản lý sản phẩm trong các site thương mại", isSystem: false, sortOrder: 20);
        var keoBiaModule = await EnsureFeatureModule(db, "keobia", "Kèo Bia 2026", "Mini-game dự đoán World Cup bằng cốc bia ảo", isSystem: false, sortOrder: 30);

        var defaultSite = await db.Sites.FirstOrDefaultAsync(x => x.Slug == "hailuunguoc");
        if (defaultSite == null)
        {
            defaultSite = new NewsCMS.Domain.Entities.Site.Site
            {
                Name = "Hải Lưu Ngược",
                Slug = "hailuunguoc",
                DefaultTheme = "HaiLuuNguoc",
                IsActive = true
            };
            db.Sites.Add(defaultSite);
            await db.SaveChangesAsync();
        }

        await EnsureSiteFeature(db, defaultSite.Id, newsModule.Id, true);
        await EnsureSiteFeature(db, defaultSite.Id, productsModule.Id, false);

        // Site thứ 2: Thế Giới Xe Điện Phú Phúc (theme PhuPhucYaka) — bật module sản phẩm.
        var phuphucSite = await db.Sites.FirstOrDefaultAsync(x => x.Slug == "phuphucyaka");
        if (phuphucSite == null)
        {
            phuphucSite = new NewsCMS.Domain.Entities.Site.Site
            {
                Name = "Thế Giới Xe Điện Phú Phúc",
                Slug = "phuphucyaka",
                DefaultTheme = "PhuPhucYaka",
                IsActive = true
            };
            db.Sites.Add(phuphucSite);
            await db.SaveChangesAsync();
        }

        await EnsureSiteFeature(db, phuphucSite.Id, newsModule.Id, false);
        await EnsureSiteFeature(db, phuphucSite.Id, productsModule.Id, true);

        var keoBiaSite = await db.Sites.FirstOrDefaultAsync(x => x.Slug == "keobia2026");
        if (keoBiaSite == null)
        {
            keoBiaSite = new NewsCMS.Domain.Entities.Site.Site
            {
                Name = "Kèo Bia 2026",
                Slug = "keobia2026",
                DefaultTheme = "KeoBia2026",
                IsActive = true
            };
            db.Sites.Add(keoBiaSite);
            await db.SaveChangesAsync();
        }

        await EnsureSiteFeature(db, keoBiaSite.Id, newsModule.Id, true);
        await EnsureSiteFeature(db, keoBiaSite.Id, productsModule.Id, false);
        await EnsureSiteFeature(db, keoBiaSite.Id, keoBiaModule.Id, true);

        // Map domain → site (resolver tra bảng này trước PrimaryDomain). Idempotent theo Host.
        await EnsureDomain(db, defaultSite.Id, "juiceandflower.io.vn", isPrimary: true);
        await EnsureDomain(db, phuphucSite.Id, "phuphucyaka.io.vn", isPrimary: true);

        var superRole = await roleManager.FindByNameAsync("SuperAdmin");
        if (superRole != null)
        {
            var allPerms = await db.Permissions.ToListAsync();
            var assigned = await db.RolePermissions.Where(x => x.RoleId == superRole.Id).Select(x => x.PermissionId).ToListAsync();
            foreach (var p in allPerms)
            {
                if (!assigned.Contains(p.Id))
                    db.RolePermissions.Add(new RolePermission { RoleId = superRole.Id, PermissionId = p.Id });
            }
            await db.SaveChangesAsync();
        }

        const string superEmail = "admin@newscms.local";
        var super = await userManager.FindByNameAsync("admin");
        if (super == null)
        {
            super = new AppUser
            {
                UserName = "admin",
                Email = superEmail,
                EmailConfirmed = true,
                FullName = "Super Admin",
                SiteId = null,
                IsActive = true
            };
            var createRes = await userManager.CreateAsync(super, "Admin@123");
            if (createRes.Succeeded)
                await userManager.AddToRoleAsync(super, "SuperAdmin");
        }
        else if (super.SiteId != null)
        {
            super.SiteId = null;
            await userManager.UpdateAsync(super);
        }

        var defaultAdmin = await userManager.FindByNameAsync("admin-hailuunguoc");
        if (defaultAdmin == null)
        {
            defaultAdmin = new AppUser
            {
                UserName = "admin-hailuunguoc",
                Email = "admin@hailuunguoc.local",
                EmailConfirmed = true,
                FullName = "Hải Lưu Ngược Admin",
                SiteId = defaultSite.Id,
                IsActive = true
            };
            var createRes = await userManager.CreateAsync(defaultAdmin, "Admin@123");
            if (createRes.Succeeded)
                await userManager.AddToRoleAsync(defaultAdmin, "Admin");
        }

        if (!await db.SiteSettings.IgnoreQueryFilters().AnyAsync(x => x.SiteId == defaultSite.Id))
        {
            db.SiteSettings.AddRange(
                new SiteSetting { SiteId = defaultSite.Id, Key = "Site.Name", Value = defaultSite.Name, Group = "general" },
                new SiteSetting { SiteId = defaultSite.Id, Key = "Site.Description", Value = "Trang tin tức demo", Group = "general" },
                new SiteSetting { SiteId = defaultSite.Id, Key = "Site.Logo", Value = "/img/logo.png", Group = "general" },
                new SiteSetting { SiteId = defaultSite.Id, Key = "Site.ActiveTheme", Value = defaultSite.DefaultTheme, Group = "theme" }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.SiteSettings.IgnoreQueryFilters().AnyAsync(x => x.SiteId == phuphucSite.Id))
        {
            db.SiteSettings.AddRange(
                new SiteSetting { SiteId = phuphucSite.Id, Key = "Site.Name", Value = phuphucSite.Name, Group = "general" },
                new SiteSetting { SiteId = phuphucSite.Id, Key = "Site.Description", Value = "Thế giới xe điện Phú Phúc", Group = "general" },
                new SiteSetting { SiteId = phuphucSite.Id, Key = "Site.Logo", Value = "/img/logo.png", Group = "general" },
                new SiteSetting { SiteId = phuphucSite.Id, Key = "Site.ActiveTheme", Value = phuphucSite.DefaultTheme, Group = "theme" }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.SiteSettings.IgnoreQueryFilters().AnyAsync(x => x.SiteId == keoBiaSite.Id))
        {
            db.SiteSettings.AddRange(
                new SiteSetting { SiteId = keoBiaSite.Id, Key = "Site.Name", Value = keoBiaSite.Name, Group = "general" },
                new SiteSetting { SiteId = keoBiaSite.Id, Key = "Site.Description", Value = "Microsite mini-game Kèo Bia 2026", Group = "general" },
                new SiteSetting { SiteId = keoBiaSite.Id, Key = "Site.Logo", Value = "/img/logo.png", Group = "general" },
                new SiteSetting { SiteId = keoBiaSite.Id, Key = "Site.ActiveTheme", Value = keoBiaSite.DefaultTheme, Group = "theme" }
            );
            await db.SaveChangesAsync();
        }

        await UpsertKeoBiaChangelog(db, keoBiaSite.Id, "v0.4", "Nộp bia tự động", "Cho phép người chơi nộp bia.", new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc));
        await UpsertKeoBiaChangelog(db, keoBiaSite.Id, "v0.5", "Nộp bia", "Người chơi có thể ghi nhận các lần nộp bia để bảng mất bia phản ánh đúng phần đã thanh toán.", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        await UpsertKeoBiaChangelog(db, keoBiaSite.Id, "v0.6", "Quiz mất bia", "Thêm phần hỏi đáp vui. Trả lời sai sẽ bị cộng bia phạt, trả lời đúng giúp giảm bia mất theo luật mini game.", new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));
        await UpsertKeoBiaChangelog(db, keoBiaSite.Id, "v0.7", "Dự đoán tỉ số", "Khi đặt kèo, người chơi có thể dự đoán thêm tỉ số 90 phút. Nếu dự đoán tỉ số sai sẽ mất thêm 1 cốc, không phụ thuộc kèo thắng/hòa/thua. Đoán đúng tỉ số sẽ được trừ bia mất theo tỉ lệ odds, làm tròn xuống và tối đa 3 cốc.", new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc));

        await EnsureKeoBiaMatches(db, keoBiaSite.Id);

        await EnsurePhuPhucYakaProducts(db, phuphucSite.Id, super?.Id);

        var storage = sp.GetRequiredService<IFileStorage>();
        await RemoveCommitmentSeeds(db, storage);

        await EnsureSiteTemplates(db);

        if (!await db.Categories.IgnoreQueryFilters().AnyAsync(x => x.SiteId == defaultSite.Id))
        {
            db.Categories.AddRange(
                new Category { SiteId = defaultSite.Id, Name = "Thời sự", Slug = "thoi-su", Order = 1, IsActive = true },
                new Category { SiteId = defaultSite.Id, Name = "Kinh doanh", Slug = "kinh-doanh", Order = 2, IsActive = true },
                new Category { SiteId = defaultSite.Id, Name = "Công nghệ", Slug = "cong-nghe", Order = 3, IsActive = true },
                new Category { SiteId = defaultSite.Id, Name = "Giải trí", Slug = "giai-tri", Order = 4, IsActive = true },
                new Category { SiteId = defaultSite.Id, Name = "Thể thao", Slug = "the-thao", Order = 5, IsActive = true }
            );
            await db.SaveChangesAsync();
        }

        // Seed AI prompt skills
        if (!await db.AiSkills.AnyAsync(x => x.Kind == AiSkillKind.Prompt))
        {
            db.AiSkills.AddRange(
                new AiSkill
                {
                    Key = AiTaskKeys.GenerateBody,
                    Name = "Sinh thân bài",
                    Description = "Tạo nội dung chi tiết cho bài viết",
                    Kind = AiSkillKind.Prompt,
                    IsActive = true,
                    SortOrder = 1,
                    SystemPrompt = "Bạn là một nhà báo chuyên nghiệp, viết nội dung chất lượng cao bằng tiếng Việt. Viết nội dung HTML ngữ nghĩa, rõ ràng, hấp dẫn.",
                    UserPromptTemplate = "Viết bài viết đầy đủ cho tiêu đề: {title}\n\n{styleInstruction}\n\nNgôn ngữ: {language}",
                    AllowStyled = true,
                    Targets = "post.body,product.description",
                    Temperature = 0.7,
                    MaxTokens = 4000
                },
                new AiSkill
                {
                    Key = AiTaskKeys.Summarize,
                    Name = "Tóm tắt",
                    Description = "Tạo bản tóm tắt ngắn gọn",
                    Kind = AiSkillKind.Prompt,
                    IsActive = true,
                    SortOrder = 2,
                    SystemPrompt = "Bạn là chuyên gia tóm tắt nội dung. Tóm tắt ngắn gọn, súc tích bằng tiếng Việt.",
                    UserPromptTemplate = "Tóm tắt nội dung sau thành 2-3 câu:\n\nTiêu đề: {title}\nNội dung: {content}\n\n{styleInstruction}",
                    AllowStyled = false,
                    Targets = "post.excerpt,product.short",
                    Temperature = 0.5,
                    MaxTokens = 500
                },
                new AiSkill
                {
                    Key = AiTaskKeys.SuggestTitle,
                    Name = "Gợi ý tiêu đề",
                    Description = "Gợi ý tiêu đề hấp dẫn cho bài viết",
                    Kind = AiSkillKind.Prompt,
                    IsActive = true,
                    SortOrder = 3,
                    SystemPrompt = "Bạn là chuyên gia SEO và copywriting. Gợi ý tiêu đề hấp dẫn, tối ưu SEO bằng tiếng Việt.",
                    UserPromptTemplate = "Gợi ý 3 tiêu đề hấp dẫn cho bài viết có nội dung:\n\n{content}\n\nChỉ trả về danh sách tiêu đề, mỗi dòng một tiêu đề.",
                    AllowStyled = false,
                    Targets = "post.title,product.name",
                    Temperature = 0.8,
                    MaxTokens = 300
                },
                new AiSkill
                {
                    Key = AiTaskKeys.Rewrite,
                    Name = "Viết lại",
                    Description = "Viết lại nội dung theo phong cách khác",
                    Kind = AiSkillKind.Prompt,
                    IsActive = true,
                    SortOrder = 4,
                    SystemPrompt = "Bạn là biên tập viên chuyên nghiệp. Viết lại nội dung cho rõ ràng, hấp dẫn hơn bằng tiếng Việt.",
                    UserPromptTemplate = "Viết lại nội dung sau:\n\n{content}\n\n{styleInstruction}\n\nNgôn ngữ: {language}",
                    AllowStyled = true,
                    Targets = "post.body,post.excerpt,product.description,product.short",
                    Temperature = 0.7,
                    MaxTokens = 4000
                }
            );
            await db.SaveChangesAsync();
        }

        var keoBiaAnalysisSkill = await db.AiSkills.FirstOrDefaultAsync(x => x.Key == AiTaskKeys.KeoBiaExpertAnalysis);
        if (keoBiaAnalysisSkill is null)
        {
            keoBiaAnalysisSkill = new AiSkill
            {
                Key = AiTaskKeys.KeoBiaExpertAnalysis
            };
            db.AiSkills.Add(keoBiaAnalysisSkill);
        }

        keoBiaAnalysisSkill.Name = "Phân tích chuyên sâu trận đấu Bia Vui";
        keoBiaAnalysisSkill.Description = "Phân tích chuyên sâu, có nguồn live, xác suất, chiến thuật, bối cảnh và value của một trận đấu cho giao diện Bia Vui";
        keoBiaAnalysisSkill.Kind = AiSkillKind.Prompt;
        keoBiaAnalysisSkill.IsActive = true;
        keoBiaAnalysisSkill.SortOrder = 20;
        keoBiaAnalysisSkill.SystemPrompt = """
            Bạn là chuyên gia phân tích bóng đá theo hướng khách quan, xác suất, dữ liệu và quản trị rủi ro.
            Luôn trả lời bằng tiếng Việt, plain text, không dùng markdown và không dùng HTML.
            Viết như một người thật đang nhận định trước trận: gãy gọn, có nhịp, nói thẳng điểm chính, không viết như báo cáo máy móc.
            Tránh các cụm sáo mòn kiểu "dựa trên phân tích", "có thể thấy rằng", "nhìn chung", "chuyên sâu" nếu không thật cần thiết.
            Mỗi mục nên ngắn, giàu chi tiết cụ thể; đừng kéo dài bằng cách liệt kê nguồn hoặc tên cầu thủ không liên quan.
            Nếu có công cụ Firecrawl hoặc 9Router khả dụng, bắt buộc search nhiều truy vấn liên quan và scrape các nguồn quan trọng trước khi kết luận.
            Không được chỉ lặp lại tỷ lệ cộng đồng hoặc dữ liệu AI đã lưu. Dữ liệu nội bộ chỉ là baseline, phải được kiểm chứng và điều chỉnh bằng nguồn live.
            Trọng tâm là phong độ gần đây, lực lượng/chấn thương/treo giò, đội hình dự kiến, tương quan chất lượng, chiến thuật, bối cảnh điểm số, sân bãi, thời tiết, độ cao, quãng nghỉ và di chuyển khi phù hợp.
            Ưu tiên nguồn chính thức hoặc uy tín như FIFA, liên đoàn, CLB/đội tuyển, Reuters, AP, ESPN, BBC Sport, The Analyst, Transfermarkt hoặc trang thống kê/tỷ lệ thị trường uy tín.
            Nếu có tỷ lệ thị trường/giá nhà cái (odds) từ nguồn web, chỉ dùng chúng để so sánh xác suất ngầm với xác suất ước lượng nhằm đánh giá value; không cổ vũ cá cược, không khuyến khích gỡ gạc, không bao giờ cam kết thắng.
            Luôn thể hiện độ bất định. Nếu thiếu nguồn live hoặc thiếu tỷ lệ thị trường/giá nhà cái, phải nói rõ là chưa đủ dữ liệu để kết luận sâu hơn.
            """;
        keoBiaAnalysisSkill.UserPromptTemplate = """
            Phân tích chuyên sâu trận đấu sau cho người xem phổ thông nhưng có chiều sâu chuyên gia.
            Nếu có công cụ Firecrawl hoặc 9Router, hãy:
            1) search ít nhất 3 nhóm thông tin: phong độ/lực lượng, chiến thuật/bối cảnh, tỷ lệ thị trường/giá nhà cái hoặc dự báo thị trường;
            2) scrape ít nhất 2 nguồn quan trọng nếu search trả về URL phù hợp;
            3) chỉ dùng dữ liệu nội bộ bên dưới làm baseline, không được kết luận chỉ từ baseline.

            Trả về phân tích plain text, không markdown, không HTML, khoảng 450-750 từ nếu đủ dữ liệu.
            Cấu trúc bắt buộc:
            Tổng quan:
            Lực lượng và phong độ:
            Chiến thuật:
            Bối cảnh World Cup 2026:
            Xác suất:
            Tỷ số dễ xảy ra:
            Value:
            Chốt nhận định:
            Lưu ý:
            Nguồn live đã kiểm tra:

            Quy tắc nội dung:
            - Nguồn live đã kiểm tra phải là mục cuối cùng, nêu ngắn 2-5 nguồn hoặc loại nguồn đã dùng; nếu tool lỗi hoặc nguồn yếu, nói rõ.
            - Tổng quan phải đi thẳng vào thế trận và lý do đáng chú ý nhất, không mở bài kiểu văn mẫu.
            - Xác suất phải ghi rõ thắng-hòa-thua của hai đội và tổng bằng 100%.
            - Trong mục Value, phải viết rõ cho người phổ thông: "Tỷ lệ thị trường/giá nhà cái (odds) là xác suất ngầm mà thị trường đang định giá, không phải xác suất chắc chắn." Sau đó mới so sánh với xác suất ước lượng.
            - Value chỉ được kết luận "có thể có value", "gần fair", "không có value rõ", hoặc "chưa đủ dữ liệu"; không được ra lệnh hành động.
            - Phải có ít nhất 3 luận điểm cụ thể, không được viết chung chung như "hai đội cân bằng" nếu chưa giải thích bằng dữ liệu.
            - Không nhắc đến đặt tiền, gỡ kèo, all-in, chắc thắng hoặc ngôn ngữ kích thích cược.
            - Cuối nội dung phải thêm đúng một dòng máy đọc được, không markdown: JSON_XAC_SUAT: {"home":38,"draw":28,"away":34,"homeLabel":"Đội nhà thắng","drawLabel":"Hòa","awayLabel":"Đội khách thắng"}
            - Dòng JSON_XAC_SUAT phải dùng số nguyên, tổng home + draw + away = 100, và label phải thay bằng đúng tên hai đội trong trận.

            Dữ liệu trận:
            {content}
            """;
        keoBiaAnalysisSkill.AllowStyled = false;
        keoBiaAnalysisSkill.Targets = "keobia.analysis";
        keoBiaAnalysisSkill.Temperature = 0.35;
        keoBiaAnalysisSkill.MaxTokens = 6000;
        keoBiaAnalysisSkill.UseTools = true;
        keoBiaAnalysisSkill.IsDeleted = false;
        keoBiaAnalysisSkill.DeletedAt = null;
        await db.SaveChangesAsync();

        var keoBiaCorrectScoreOddsSkill = await db.AiSkills.FirstOrDefaultAsync(x => x.Key == AiTaskKeys.KeoBiaCorrectScoreOdds);
        if (keoBiaCorrectScoreOddsSkill is null)
        {
            keoBiaCorrectScoreOddsSkill = new AiSkill { Key = AiTaskKeys.KeoBiaCorrectScoreOdds };
            db.AiSkills.Add(keoBiaCorrectScoreOddsSkill);
        }

        keoBiaCorrectScoreOddsSkill.Name = "Trích xuất odds tỉ số chính xác Bia Vui";
        keoBiaCorrectScoreOddsSkill.Description = "Tìm và trích xuất Correct Score odds thành JSON cho tính năng dự đoán tỉ số 90 phút.";
        keoBiaCorrectScoreOddsSkill.Kind = AiSkillKind.Prompt;
        keoBiaCorrectScoreOddsSkill.IsActive = true;
        keoBiaCorrectScoreOddsSkill.SortOrder = 21;
        keoBiaCorrectScoreOddsSkill.SystemPrompt = """
            Bạn là bộ trích xuất dữ liệu odds Correct Score. Chỉ trả JSON object hợp lệ, không markdown, không giải thích.
            Chỉ dùng odds thật từ dữ liệu web/tool/context. Không được bịa số.
            Key hợp lệ: tỉ số dạng "0-0", "1-0", ... hoặc "other", "home_other", "away_other", "draw_other".
            Bắt buộc cố gắng trả ít nhất 10 lựa chọn nếu nguồn có đủ dữ liệu. Nếu chỉ tìm được ít tỉ số cụ thể, bắt buộc thêm "other" từ nhóm/odds cao nhất có trong nguồn.
            Value là decimal odds dạng số.
            Nếu không tìm được odds thật, trả {}.
            """;
        keoBiaCorrectScoreOddsSkill.UserPromptTemplate = """
            Trích xuất Correct Score / Tỉ số chính xác odds cho trận sau, tính theo 90 phút chính thức.
            Cố gắng lấy range rộng nhất có thể, tối thiểu 10 lựa chọn nếu nguồn có: 0-0, 1-0, 0-1, 1-1, 2-0, 0-2, 2-1, 1-2, 2-2, 3-0, 0-3, 3-1, 1-3, 3-2, 2-3, 3-3, 4-0, 0-4, 4-1, 1-4, 4-2, 2-4.
            Nếu nguồn có nhóm khác, map thành other, home_other, away_other, draw_other. Nếu chỉ có vài tỉ số cụ thể và có odds nhóm còn lại, bắt buộc thêm other/home_other/away_other/draw_other.

            Dữ liệu trận và kết quả web search:
            {content}
            """;
        keoBiaCorrectScoreOddsSkill.AllowStyled = false;
        keoBiaCorrectScoreOddsSkill.Targets = "keobia.correct_score_odds";
        keoBiaCorrectScoreOddsSkill.Temperature = 0;
        keoBiaCorrectScoreOddsSkill.MaxTokens = 1200;
        keoBiaCorrectScoreOddsSkill.UseTools = true;
        keoBiaCorrectScoreOddsSkill.IsDeleted = false;
        keoBiaCorrectScoreOddsSkill.DeletedAt = null;
        await db.SaveChangesAsync();

        var firecrawlToolSkill = await db.AiSkills.FirstOrDefaultAsync(x => x.Key == "tool_firecrawl_search");
        if (firecrawlToolSkill is null)
        {
            firecrawlToolSkill = new AiSkill
            {
                Key = "tool_firecrawl_search"
            };
            db.AiSkills.Add(firecrawlToolSkill);
        }

        firecrawlToolSkill.Name = "Firecrawl Search cho phân tích trận";
        firecrawlToolSkill.Description = "Tìm kiếm tin mới nhất trên web để bổ trợ phân tích trận đấu. Cần cấu hình Firecrawl API key trước khi bật hoạt động.";
        firecrawlToolSkill.Kind = AiSkillKind.Tool;
        firecrawlToolSkill.ToolType = "firecrawl_search";
        firecrawlToolSkill.BaseUrl = "https://api.firecrawl.dev";
        firecrawlToolSkill.ConfigJson = """{"defaultIncludeContent":true,"maxResults":6,"maxMarkdownCharsPerResult":3000}""";
        firecrawlToolSkill.IsActive = !string.IsNullOrWhiteSpace(firecrawlToolSkill.ApiKeyEncrypted);
        firecrawlToolSkill.SortOrder = 21;
        firecrawlToolSkill.IsDeleted = false;
        firecrawlToolSkill.DeletedAt = null;
        await db.SaveChangesAsync();

        var firecrawlScrapeToolSkill = await db.AiSkills.FirstOrDefaultAsync(x => x.Key == "tool_firecrawl_scrape");
        if (firecrawlScrapeToolSkill is null)
        {
            firecrawlScrapeToolSkill = new AiSkill
            {
                Key = "tool_firecrawl_scrape"
            };
            db.AiSkills.Add(firecrawlScrapeToolSkill);
        }

        firecrawlScrapeToolSkill.Name = "Firecrawl Scrape cho phân tích trận";
        firecrawlScrapeToolSkill.Description = "Scrape chi tiết một URL đã tìm thấy để đọc markdown sạch trước khi AI kết luận phân tích trận. Cần cấu hình Firecrawl API key trước khi bật hoạt động.";
        firecrawlScrapeToolSkill.Kind = AiSkillKind.Tool;
        firecrawlScrapeToolSkill.ToolType = "firecrawl_scrape";
        firecrawlScrapeToolSkill.BaseUrl = "https://api.firecrawl.dev";
        firecrawlScrapeToolSkill.ConfigJson = """{"maxMarkdownChars":8000}""";
        firecrawlScrapeToolSkill.IsActive = !string.IsNullOrWhiteSpace(firecrawlScrapeToolSkill.ApiKeyEncrypted);
        firecrawlScrapeToolSkill.SortOrder = 22;
        firecrawlScrapeToolSkill.IsDeleted = false;
        firecrawlScrapeToolSkill.DeletedAt = null;
        await db.SaveChangesAsync();

        var ninerouterSearchSkill = await db.AiSkills.FirstOrDefaultAsync(x => x.Key == "tool_ninerouter_search");
        if (ninerouterSearchSkill is null)
        {
            ninerouterSearchSkill = new AiSkill
            {
                Key = "tool_ninerouter_search",
                BaseUrl = null,
                ConfigJson = """{"provider":"serper","maxResults":6,"maxMarkdownCharsPerResult":3000}"""
            };
            db.AiSkills.Add(ninerouterSearchSkill);
        }

        ninerouterSearchSkill.Name = "9Router Search cho phân tích trận";
        ninerouterSearchSkill.Description = "Tìm kiếm tin mới nhất trên web qua 9Router (Tavily, Exa, Brave, Serper, Perplexity,...). Backup khi Firecrawl hết credits.";
        ninerouterSearchSkill.Kind = AiSkillKind.Tool;
        ninerouterSearchSkill.ToolType = "ninerouter_search";
        ninerouterSearchSkill.IsActive = !string.IsNullOrWhiteSpace(ninerouterSearchSkill.ApiKeyEncrypted) || !string.IsNullOrWhiteSpace(ninerouterSearchSkill.BaseUrl);
        ninerouterSearchSkill.SortOrder = 23;
        ninerouterSearchSkill.IsDeleted = false;
        ninerouterSearchSkill.DeletedAt = null;
        await db.SaveChangesAsync();

        var ninerouterFetchSkill = await db.AiSkills.FirstOrDefaultAsync(x => x.Key == "tool_ninerouter_fetch");
        if (ninerouterFetchSkill is null)
        {
            ninerouterFetchSkill = new AiSkill
            {
                Key = "tool_ninerouter_fetch",
                BaseUrl = null,
                ConfigJson = """{"provider":"jina-reader","maxMarkdownChars":8000}"""
            };
            db.AiSkills.Add(ninerouterFetchSkill);
        }

        ninerouterFetchSkill.Name = "9Router Fetch cho phân tích trận";
        ninerouterFetchSkill.Description = "Fetch chi tiết URL qua 9Router (Jina Reader, Firecrawl, Tavily, Exa). Backup khi Firecrawl hết credits.";
        ninerouterFetchSkill.Kind = AiSkillKind.Tool;
        ninerouterFetchSkill.ToolType = "ninerouter_fetch";
        ninerouterFetchSkill.IsActive = !string.IsNullOrWhiteSpace(ninerouterFetchSkill.ApiKeyEncrypted) || !string.IsNullOrWhiteSpace(ninerouterFetchSkill.BaseUrl);
        ninerouterFetchSkill.SortOrder = 24;
        ninerouterFetchSkill.IsDeleted = false;
        ninerouterFetchSkill.DeletedAt = null;
        await db.SaveChangesAsync();

        var imageGenerateSkill = await db.AiSkills.FirstOrDefaultAsync(x => x.Key == "tool_image_generate");
        if (imageGenerateSkill is null)
        {
            imageGenerateSkill = new AiSkill
            {
                Key = "tool_image_generate",
                BaseUrl = "http://localhost:20128",
                ConfigJson = """{"model":"cx/gpt-5.5-image","size":"auto","quality":"auto","background":"auto","imageDetail":"high","outputFormat":"png","timeoutSeconds":240}"""
            };
            db.AiSkills.Add(imageGenerateSkill);
        }

        imageGenerateSkill.Name = "Image Generate cho Telegram chat";
        imageGenerateSkill.Description = "Tạo ảnh từ prompt qua endpoint /v1/images/generations và trả IMAGE_URL để bot gửi ảnh trực tiếp về Telegram.";
        imageGenerateSkill.Kind = AiSkillKind.Tool;
        imageGenerateSkill.ToolType = "image_generate";
        imageGenerateSkill.IsActive = !string.IsNullOrWhiteSpace(imageGenerateSkill.ApiKeyEncrypted);
        imageGenerateSkill.SortOrder = 25;
        imageGenerateSkill.IsDeleted = false;
        imageGenerateSkill.DeletedAt = null;
        await db.SaveChangesAsync();
    }

    private static async Task EnsureKeoBiaMatches(AppDbContext db, Guid siteId)
    {
        if (await db.KeoBiaMatches.IgnoreQueryFilters().AnyAsync(x => x.SiteId == siteId))
            return;

        db.KeoBiaMatches.AddRange(
            new KeoBiaMatch
            {
                SiteId = siteId,
                ExternalId = "arg-vie",
                Stage = "Bảng A",
                HomeName = "Argentina",
                HomeCode = "ARG",
                HomePrimary = "#87c2ff",
                HomeSecondary = "#1b6bff",
                AwayName = "Việt Nam",
                AwayCode = "VIE",
                AwayPrimary = "#ffcc75",
                AwaySecondary = "#ff7a00",
                KickoffAt = new DateTime(2026, 6, 12, 20, 0, 0),
                Venue = "Rose Bowl, Los Angeles",
                IsHot = true,
                HotLabel = "Cầu đinh",
                DefaultCups = 1,
                BaseHomeWeight = 51,
                BaseDrawWeight = 18,
                BaseAwayWeight = 31,
                AiHome = 57,
                AiDraw = 17,
                AiAway = 26,
                AiSummary = "Argentina nhỉnh hơn nhưng Việt Nam có 20 phút đầu rất khó chịu nếu giữ được pressing."
            },
            new KeoBiaMatch
            {
                SiteId = siteId,
                ExternalId = "bra-jpn",
                Stage = "Bảng B",
                HomeName = "Brazil",
                HomeCode = "BRA",
                HomePrimary = "#9be15d",
                HomeSecondary = "#00a950",
                AwayName = "Japan",
                AwayCode = "JPN",
                AwayPrimary = "#ff7f89",
                AwaySecondary = "#ff3054",
                KickoffAt = new DateTime(2026, 6, 12, 23, 30, 0),
                Venue = "Seattle Stadium",
                DefaultCups = 1,
                BaseHomeWeight = 46,
                BaseDrawWeight = 20,
                BaseAwayWeight = 34,
                AiHome = 44,
                AiDraw = 21,
                AiAway = 35,
                AiSummary = "Brazil được đánh giá cao hơn về bóng hai, nhưng Nhật Bản có cửa ăn điểm nhờ chuyển trạng thái rất gọn."
            },
            new KeoBiaMatch
            {
                SiteId = siteId,
                ExternalId = "fra-usa",
                Stage = "Bảng C",
                HomeName = "France",
                HomeCode = "FRA",
                HomePrimary = "#7cb8ff",
                HomeSecondary = "#215dff",
                AwayName = "USA",
                AwayCode = "USA",
                AwayPrimary = "#ff9d8f",
                AwaySecondary = "#ff4d62",
                KickoffAt = new DateTime(2026, 6, 13, 2, 0, 0),
                Venue = "Dallas Dome",
                DefaultCups = 1,
                BaseHomeWeight = 49,
                BaseDrawWeight = 16,
                BaseAwayWeight = 35,
                AiHome = 53,
                AiDraw = 18,
                AiAway = 29,
                AiSummary = "Pháp khó giữ sạch lưới vì đội hình Mỹ rất dày sức, nhưng khâu dứt điểm vẫn vượt trội."
            },
            new KeoBiaMatch
            {
                SiteId = siteId,
                ExternalId = "esp-mar",
                Stage = "Bảng D",
                HomeName = "Spain",
                HomeCode = "ESP",
                HomePrimary = "#ffb347",
                HomeSecondary = "#ff5d4d",
                AwayName = "Morocco",
                AwayCode = "MAR",
                AwayPrimary = "#86d39e",
                AwaySecondary = "#14824f",
                KickoffAt = new DateTime(2026, 6, 13, 4, 30, 0),
                Venue = "Mexico City Arena",
                DefaultCups = 1,
                BaseHomeWeight = 42,
                BaseDrawWeight = 23,
                BaseAwayWeight = 35,
                AiHome = 48,
                AiDraw = 22,
                AiAway = 30,
                AiSummary = "Kèo chia đôi rõ: cộng đồng thích Morocco, AI ưu tiên khả năng giữ bóng của Spain."
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task EnsurePhuPhucYakaProducts(AppDbContext db, Guid siteId, Guid? uploaderId)
    {
        var now = DateTime.UtcNow;
        var category = await db.ProductCategories.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.SiteId == siteId && x.Slug == "xe-dien-yaka");

        if (category == null)
        {
            category = await db.ProductCategories.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Slug == "xe-dien-yaka");
            if (category == null)
            {
                category = new ProductCategory
                {
                    SiteId = siteId,
                    Name = "Xe điện Yaka",
                    Slug = "xe-dien-yaka",
                    Description = "Các dòng xe máy điện Yaka Bike chính hãng.",
                    Order = 1,
                    IsActive = true
                };
                db.ProductCategories.Add(category);
            }
            else
            {
                category.SiteId = siteId;
                category.IsDeleted = false;
                category.DeletedAt = null;
            }
            await db.SaveChangesAsync();
        }

        var fallbackUploaderId = uploaderId ?? await db.Users.Select(x => x.Id).FirstOrDefaultAsync();
        var specs = PhuPhucYakaProductSpecs();
        foreach (var spec in specs)
        {
            var media = await EnsureProductMedia(db, siteId, fallbackUploaderId, spec, now);
            var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Sku == spec.Sku);
            if (product == null)
            {
                product = new Product
                {
                    SiteId = siteId,
                    Name = spec.Name,
                    Slug = spec.Slug,
                    Sku = spec.Sku,
                    ShortDescription = spec.ShortDescription,
                    Description = PhuPhucYakaProductDescription,
                    Price = spec.Price,
                    SalePrice = spec.SalePrice,
                    IsTrackingStock = false,
                    StockQuantity = spec.StockQuantity,
                    Status = ProductStatus.Published,
                    PublishedAt = now,
                    IsFeatured = spec.IsFeatured,
                    ProductCategoryId = category.Id,
                    ThumbnailMediaId = media.Id
                };
                db.Products.Add(product);
                await db.SaveChangesAsync();
            }
            else
            {
                product.SiteId = siteId;
                product.ProductCategoryId = category.Id;
                product.ThumbnailMediaId = media.Id;
                product.Status = ProductStatus.Published;
                product.PublishedAt ??= now;
                product.IsDeleted = false;
                product.DeletedAt = null;
                product.UpdatedAt = now;
                await db.SaveChangesAsync();
            }

            var hasImage = await db.ProductImages.AnyAsync(x => x.ProductId == product.Id && x.Url == spec.ImagePath);
            if (!hasImage)
            {
                db.ProductImages.Add(new ProductImage
                {
                    ProductId = product.Id,
                    Url = spec.ImagePath,
                    AltText = spec.Name,
                    SortOrder = 0
                });
                await db.SaveChangesAsync();
            }
        }
    }

    private static async Task<Media> EnsureProductMedia(AppDbContext db, Guid siteId, Guid uploaderId, PhuPhucYakaProductSpec spec, DateTime now)
    {
        var media = await db.Medias.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == spec.MediaId);
        if (media == null)
        {
            media = new Media
            {
                Id = spec.MediaId,
                SiteId = siteId,
                FileName = spec.FileName,
                StorageKey = spec.StorageKey,
                FilePath = spec.ImagePath,
                MimeType = "image/png",
                Kind = "image",
                Size = 0,
                Title = spec.Name,
                AltText = spec.Name,
                UploadedById = uploaderId,
                CreatedAt = now
            };
            db.Medias.Add(media);
        }
        else
        {
            media.SiteId = siteId;
            media.Title = spec.Name;
            media.AltText = spec.Name;
            media.IsDeleted = false;
            media.DeletedAt = null;
            media.UpdatedAt = now;
        }
        await db.SaveChangesAsync();
        return media;
    }

    private static IReadOnlyList<PhuPhucYakaProductSpec> PhuPhucYakaProductSpecs() =>
    [
        new("Lavara Elegant", "lavara-elegant", "YK-ELEGANT", "Đỉnh cao thiết kế phong cách Ý, động cơ điện thế hệ mới, êm ái và tiết kiệm.", 20990000, null, 12, true, Guid.Parse("22220000-0000-0000-0000-000000000001"), "lavara-elegant.png"),
        new("Lavara GX Sport", "lavara-gx-sport", "YK-GXSPORT", "Phiên bản thể thao mạnh mẽ, khung gầm chắc chắn, vận hành bền bỉ.", 18900000, null, 9, true, Guid.Parse("22220000-0000-0000-0000-000000000002"), "lavara-gx-sport.png"),
        new("Lavara Sweet", "lavara-sweet", "YK-SWEET", "Dáng xe thanh lịch, nhỏ gọn, phù hợp di chuyển trong phố.", 15500000, null, 15, true, Guid.Parse("22220000-0000-0000-0000-000000000003"), "lavara-sweet.png"),
        new("Lavara Pro Max", "lavara-pro-max", "YK-PROMAX", "Phiên bản cao cấp nhất, quãng đường xa, đầy đủ tiện ích thông minh.", 22500000, 21500000, 6, false, Guid.Parse("22220000-0000-0000-0000-000000000004"), "lavara-elegant.png"),
        new("Lavara City", "lavara-city", "YK-CITY", "Lựa chọn kinh tế cho học sinh, sinh viên, tiết kiệm tối đa.", 14200000, 13500000, 20, false, Guid.Parse("22220000-0000-0000-0000-000000000005"), "lavara-sweet.png")
    ];

    private const string PhuPhucYakaProductDescription = "<p>Lấy cảm hứng từ phong cách Ý tinh tế, dòng xe điện Yaka Bike kết hợp động cơ điện thế hệ mới thân thiện môi trường, mang đến trải nghiệm di chuyển êm ái, sang trọng cho nhịp sống đô thị.</p><h3>Thiết kế tinh tế</h3><p>Đường cong mềm mại, lớp sơn nhám cao cấp cùng chi tiết mạ chrome sáng bóng. Cụm đèn pha LED chiếu sáng mạnh mẽ, yên xe bọc da PU êm ái.</p><h3>Động cơ mạnh mẽ</h3><p>Khối động cơ điện độc quyền công suất lớn, bứt tốc mượt mà, chống nước chuẩn IP67, leo dốc dễ dàng và hoàn toàn không xả thải.</p><h3>Tiện ích thông minh</h3><p>Màn hình LCD đa thông tin, khóa Smartkey chống trộm, cốp xe rộng rãi và cổng sạc USB tiện lợi.</p>";

    private sealed record PhuPhucYakaProductSpec(
        string Name,
        string Slug,
        string Sku,
        string ShortDescription,
        decimal Price,
        decimal? SalePrice,
        int StockQuantity,
        bool IsFeatured,
        Guid MediaId,
        string FileName)
    {
        public string StorageKey => $"uploads/products/2026/06/{FileName}";
        public string ImagePath => $"/uploads/products/2026/06/{FileName}";
    }

    private static async Task UpsertKeoBiaChangelog(AppDbContext db, Guid siteId, string version, string title, string content, DateTime createdAt)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            IF EXISTS (SELECT 1 FROM [KeoBiaChangelogs] WHERE [SiteId] = {siteId} AND [Version] = {version})
                UPDATE [KeoBiaChangelogs]
                SET [Title] = {title}, [Content] = {content}, [CreatedAt] = {createdAt}
                WHERE [SiteId] = {siteId} AND [Version] = {version}
            ELSE
                INSERT INTO [KeoBiaChangelogs] ([Id], [SiteId], [Version], [Title], [Content], [CreatedAt])
                VALUES (NEWID(), {siteId}, {version}, {title}, {content}, {createdAt})
            """);
    }

    private static async Task EnsureRole(RoleManager<AppRole> rm, string name, string desc, bool isSystem = false)
    {
        if (!await rm.RoleExistsAsync(name))
            await rm.CreateAsync(new AppRole { Name = name, Description = desc, IsSystem = isSystem });
    }

    private static async Task EnsureDomain(AppDbContext db, Guid siteId, string host, bool isPrimary)
    {
        host = host.Trim().ToLowerInvariant();
        var exists = await db.SiteDomains.AnyAsync(x => x.Host == host);
        if (exists) return;

        db.SiteDomains.Add(new SiteDomain { SiteId = siteId, Host = host, IsPrimary = isPrimary });
        await db.SaveChangesAsync();
    }

    private static async Task<FeatureModule> EnsureFeatureModule(
        AppDbContext db,
        string code,
        string name,
        string description,
        bool isSystem,
        int sortOrder)
    {
        var module = await db.FeatureModules.FirstOrDefaultAsync(x => x.Code == code);
        if (module != null)
            return module;

        module = new FeatureModule
        {
            Code = code,
            Name = name,
            Description = description,
            IsSystem = isSystem,
            SortOrder = sortOrder
        };
        db.FeatureModules.Add(module);
        await db.SaveChangesAsync();
        return module;
    }

    private static async Task EnsureSiteFeature(AppDbContext db, Guid siteId, Guid featureModuleId, bool isEnabled)
    {
        var siteFeature = await db.SiteFeatureModules.FindAsync(siteId, featureModuleId);
        if (siteFeature == null)
        {
            db.SiteFeatureModules.Add(new SiteFeatureModule
            {
                SiteId = siteId,
                FeatureModuleId = featureModuleId,
                IsEnabled = isEnabled,
                EnabledAt = isEnabled ? DateTime.UtcNow : null
            });
            await db.SaveChangesAsync();
            return;
        }

        siteFeature.IsEnabled = isEnabled;
        siteFeature.EnabledAt = isEnabled ? siteFeature.EnabledAt ?? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
    }

    /* ─── Site templates cho "Tạo site từ mẫu" (Phase 6) — idempotent theo Key ─── */
    private static async Task EnsureSiteTemplates(AppDbContext db)
    {
        // Mẫu 1: Landing sản phẩm.
        var landingSpec = """
        {
          "SiteName": "", "SiteSlug": "", "PrimaryDomain": null,
          "DefaultTheme": "Universal", "DefaultCulture": "vi", "SupportedCultures": "vi",
          "DesignTokens": [
            { "Group": "Color", "Key": "brand-500", "Value": "#0d7c66", "SortOrder": 1 },
            { "Group": "Color", "Key": "brand-600", "Value": "#0a6352", "SortOrder": 2 },
            { "Group": "Color", "Key": "ink-900", "Value": "#0f172a", "SortOrder": 3 },
            { "Group": "Color", "Key": "ink-500", "Value": "#64748b", "SortOrder": 4 },
            { "Group": "Font", "Key": "display", "Value": "\\"Be Vietnam Pro\\", system-ui, sans-serif", "SortOrder": 1 },
            { "Group": "Radius", "Key": "card", "Value": "14px", "SortOrder": 1 }
          ],
          "Layouts": [
            { "Key": "shell-default", "Name": "Shell mặc định", "Kind": "Shell", "IsDefault": true,
              "CompiledHtml": "<!DOCTYPE html><html lang=\\"vi\\"><head></head><body><header style=\\"display:flex;align-items:center;justify-content:space-between;padding:16px 24px;background:var(--color-ink-900);color:#fff;font-family:var(--font-display)\\"><div data-nc-site-name style=\\"font-weight:800;font-size:1.15rem\\"></div><nav style=\\"display:flex;gap:20px;font-size:.9rem\\"><a href=\\"/\\" style=\\"color:#e2e8f0;text-decoration:none\\">Trang chủ</a><a href=\\"/gioi-thieu\\" style=\\"color:#e2e8f0;text-decoration:none\\">Giới thiệu</a><a href=\\"/lien-he\\" style=\\"color:#e2e8f0;text-decoration:none\\">Liên hệ</a></nav></header><main data-nc-body></main><footer style=\\"padding:32px 24px;background:var(--color-ink-900);color:#94a3b8;text-align:center;font-size:.85rem\\"><p style=\\"margin:0\\">&copy; 2026 <span data-nc-site-name></span>. Mọi quyền được bảo vệ.</p></footer></body></html>",
              "CompiledCss": null, "CustomJs": null }
          ],
          "Pages": [
            { "Title": "Trang chủ", "Slug": "home", "Kind": "Landing", "LayoutKey": "shell-default",
              "CompiledHtml": "<section style=\\"padding:80px 20px;text-align:center;font-family:var(--font-display)\\"><h1 style=\\"font-size:clamp(2rem,5vw,3.5rem);font-weight:800;color:var(--color-ink-900);margin:0 0 16px\\">Sản phẩm của bạn xứng đáng được thấy</h1><p style=\\"font-size:1.15rem;color:var(--color-ink-500);max-width:36rem;margin:0 auto 32px\\">Trang landing mẫu — kéo thả để chỉnh sửa trong Builder.</p><a href=\\"#\\" style=\\"display:inline-block;padding:16px 40px;background:var(--color-brand-500);color:#fff;border-radius:var(--radius-card);text-decoration:none;font-weight:700\\">Bắt đầu ngay</a></section><section style=\\"padding:60px 20px;display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:24px;max-width:1080px;margin:0 auto\\"><div style=\\"padding:24px;border:1px solid #e2e8f0;border-radius:var(--radius-card)\\"><h3 style=\\"margin:0 0 8px;font-weight:700\\">Tính năng 1</h3><p style=\\"margin:0;color:var(--color-ink-500);font-size:.9rem\\">Mô tả ngắn.</p></div><div style=\\"padding:24px;border:1px solid #e2e8f0;border-radius:var(--radius-card)\\"><h3 style=\\"margin:0 0 8px;font-weight:700\\">Tính năng 2</h3><p style=\\"margin:0;color:var(--color-ink-500);font-size:.9rem\\">Mô tả ngắn.</p></div><div style=\\"padding:24px;border:1px solid #e2e8f0;border-radius:var(--radius-card)\\"><h3 style=\\"margin:0 0 8px;font-weight:700\\">Tính năng 3</h3><p style=\\"margin:0;color:var(--color-ink-500);font-size:.9rem\\">Mô tả ngắn.</p></div></section><section style=\\"padding:60px 20px;background:var(--color-ink-900);color:#fff;text-align:center\\"><h2 style=\\"font-size:1.75rem;font-weight:700;margin:0 0 12px\\">Sẵn sàng bắt đầu?</h2><p style=\\"opacity:.8;margin:0 0 24px\\">Liên hệ để được tư vấn.</p><a href=\\"/lien-he\\" style=\\"display:inline-block;padding:14px 36px;background:var(--color-brand-500);color:#fff;border-radius:var(--radius-card);text-decoration:none;font-weight:600\\">Liên hệ ngay</a></section>" },
            { "Title": "Giới thiệu", "Slug": "gioi-thieu", "Kind": "Static", "LayoutKey": "shell-default",
              "CompiledHtml": "<section style=\\"padding:64px 20px;max-width:720px;margin:0 auto;font-family:var(--font-display)\\"><h1 style=\\"font-size:2rem;font-weight:800\\">Về chúng tôi</h1><p style=\\"color:var(--color-ink-500);line-height:1.7\\">Trang giới thiệu mẫu. Sửa nội dung này trong Builder.</p></section>" },
            { "Title": "Liên hệ", "Slug": "lien-he", "Kind": "Static", "LayoutKey": "shell-default",
              "CompiledHtml": "<section style=\\"padding:64px 20px;max-width:720px;margin:0 auto;font-family:var(--font-display)\\"><h1 style=\\"font-size:2rem;font-weight:800\\">Liên hệ</h1><p style=\\"color:var(--color-ink-500)\\">Email: contact@example.com · Hotline: 1900 xxxx</p></section>" }
          ],
          "Categories": [],
          "Menus": [],
          "Settings": [ { "Key": "Site.Description", "Value": "Landing page giới thiệu sản phẩm", "Group": "general" } ]
        }
        """;

        // Mẫu 2: Trang tin tức.
        var newsSpec = """
        {
          "SiteName": "", "SiteSlug": "", "PrimaryDomain": null,
          "DefaultTheme": "Universal", "DefaultCulture": "vi", "SupportedCultures": "vi",
          "DesignTokens": [
            { "Group": "Color", "Key": "brand-500", "Value": "#b91c1c", "SortOrder": 1 },
            { "Group": "Color", "Key": "ink-900", "Value": "#111827", "SortOrder": 2 },
            { "Group": "Font", "Key": "display", "Value": "Georgia, \\"Times New Roman\\", serif", "SortOrder": 1 }
          ],
          "Layouts": [
            { "Key": "shell-default", "Name": "Shell mặc định", "Kind": "Shell", "IsDefault": true,
              "CompiledHtml": "<!DOCTYPE html><html lang=\\"vi\\"><head></head><body><header style=\\"padding:20px 24px;border-bottom:3px solid var(--color-brand-500);font-family:var(--font-display)\\"><div data-nc-site-name style=\\"font-size:1.6rem;font-weight:800;letter-spacing:-.02em\\"></div></header><main data-nc-body></main><footer style=\\"padding:24px;background:#f9fafb;color:#6b7280;text-align:center;font-size:.85rem\\">&copy; 2026 <span data-nc-site-name></span></footer></body></html>",
              "CompiledCss": null, "CustomJs": null }
          ],
          "Pages": [
            { "Title": "Trang chủ", "Slug": "home", "Kind": "Landing", "LayoutKey": "shell-default",
              "CompiledHtml": "<section style=\\"padding:40px 20px;max-width:1080px;margin:0 auto;font-family:var(--font-display)\\"><h1 style=\\"font-size:2rem;font-weight:800;margin:0 0 8px\\">Tin tức mới nhất</h1><div data-nc-block=\\"post-list\\" data-nc-props='{\\"count\\":6}'></div></section>" },
            { "Title": "Giới thiệu", "Slug": "gioi-thieu", "Kind": "Static", "LayoutKey": "shell-default",
              "CompiledHtml": "<section style=\\"padding:48px 20px;max-width:720px;margin:0 auto;font-family:var(--font-display)\\"><h1 style=\\"font-size:1.8rem;font-weight:800\\">Về chúng tôi</h1><p style=\\"color:#4b5563;line-height:1.7\\">Trang tin tức mẫu với dynamic block post-list.</p></section>" }
          ],
          "Categories": [
            { "Name": "Thời sự", "Slug": "thoi-su", "Type": "Post", "Order": 1, "Description": null },
            { "Name": "Kinh doanh", "Slug": "kinh-doanh", "Type": "Post", "Order": 2, "Description": null },
            { "Name": "Công nghệ", "Slug": "cong-nghe", "Type": "Post", "Order": 3, "Description": null }
          ],
          "Menus": [
            { "Name": "Main menu", "Location": "header",
              "Items": [
                { "Title": "Trang chủ", "Url": "/", "Order": 1, "Target": null },
                { "Title": "Thời sự", "Url": "/thoi-su", "Order": 2, "Target": null },
                { "Title": "Kinh doanh", "Url": "/kinh-doanh", "Order": 3, "Target": null },
                { "Title": "Công nghệ", "Url": "/cong-nghe", "Order": 4, "Target": null }
              ] }
          ],
          "Settings": [ { "Key": "Site.Description", "Value": "Báo điện tử", "Group": "general" } ]
        }
        """;

        // Mẫu 3: Trang doanh nghiệp. Bố cục nghiêm túc, xanh navy, có trang dịch vụ + liên hệ.
        var corporateSpec = """
        {
          "SiteName": "",
          "SiteSlug": "",
          "PrimaryDomain": null,
          "DefaultTheme": "Universal",
          "DefaultCulture": "vi",
          "SupportedCultures": "vi",
          "DesignTokens": [
            { "Group": "Color", "Key": "brand-500", "Value": "#1e3a8a", "SortOrder": 1 },
            { "Group": "Color", "Key": "brand-600", "Value": "#1e40af", "SortOrder": 2 },
            { "Group": "Color", "Key": "accent-500", "Value": "#0ea5e9", "SortOrder": 3 },
            { "Group": "Color", "Key": "ink-900", "Value": "#0f172a", "SortOrder": 4 },
            { "Group": "Color", "Key": "ink-500", "Value": "#64748b", "SortOrder": 5 },
            { "Group": "Color", "Key": "surface", "Value": "#f8fafc", "SortOrder": 6 },
            { "Group": "Font", "Key": "display", "Value": "\"Be Vietnam Pro\", system-ui, sans-serif", "SortOrder": 1 },
            { "Group": "Radius", "Key": "card", "Value": "10px", "SortOrder": 1 },
            { "Group": "Space", "Key": "section", "Value": "clamp(3.5rem, 2.5rem + 4vw, 6rem)", "SortOrder": 1 }
          ],
          "Layouts": [
            {
              "Key": "shell-default",
              "Name": "Shell doanh nghiệp",
              "Kind": "Shell",
              "IsDefault": true,
              "CompiledHtml": "<!DOCTYPE html><html lang=\"vi\"><head></head><body style=\"margin:0;background:#fff;font-family:var(--font-display)\"><header style=\"display:flex;align-items:center;justify-content:space-between;padding:18px 24px;border-bottom:1px solid #e2e8f0\"><div data-nc-site-name style=\"font-weight:800;font-size:1.1rem;color:var(--color-brand-500)\"></div><nav style=\"display:flex;gap:24px;font-size:.9rem\"><a href=\"/\" style=\"color:var(--color-ink-900);text-decoration:none\">Trang chủ</a><a href=\"/dich-vu\" style=\"color:var(--color-ink-900);text-decoration:none\">Dịch vụ</a><a href=\"/gioi-thieu\" style=\"color:var(--color-ink-900);text-decoration:none\">Về chúng tôi</a><a href=\"/lien-he\" style=\"color:#fff;background:var(--color-brand-500);padding:8px 18px;border-radius:var(--radius-card);text-decoration:none\">Liên hệ</a></nav></header><main data-nc-body></main><footer style=\"padding:40px 24px;background:var(--color-ink-900);color:#94a3b8;font-size:.85rem\"><div style=\"max-width:1080px;margin:0 auto;display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:32px\"><div><div data-nc-site-name style=\"color:#fff;font-weight:700;margin-bottom:8px\"></div><p style=\"margin:0;line-height:1.6\">Đối tác tin cậy cho doanh nghiệp của bạn.</p></div><div><p style=\"color:#fff;font-weight:600;margin:0 0 8px\">Liên hệ</p><p style=\"margin:0;line-height:1.8\">Email: contact@example.com<br>Hotline: 1900 xxxx</p></div></div><p style=\"max-width:1080px;margin:32px auto 0;padding-top:20px;border-top:1px solid #1e293b\">&copy; 2026 <span data-nc-site-name></span>. Mọi quyền được bảo lưu.</p></footer></body></html>",
              "CompiledCss": null,
              "CustomJs": null
            }
          ],
          "Pages": [
            {
              "Title": "Trang chủ",
              "Slug": "home",
              "Kind": "Landing",
              "LayoutKey": "shell-default",
              "CompiledHtml": "<section style=\"padding:var(--space-section) 24px;background:linear-gradient(135deg,var(--color-brand-500),var(--color-brand-600));color:#fff\"><div style=\"max-width:1080px;margin:0 auto;display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:40px;align-items:center\"><div><h1 style=\"font-size:clamp(2rem,4vw,3rem);font-weight:800;line-height:1.15;margin:0 0 16px\">Giải pháp toàn diện cho doanh nghiệp</h1><p style=\"font-size:1.05rem;opacity:.9;line-height:1.7;margin:0 0 28px\">Chúng tôi đồng hành cùng doanh nghiệp trong chuyển đổi số, tối ưu vận hành và tăng trưởng bền vững.</p><a href=\"/lien-he\" style=\"display:inline-block;padding:14px 34px;background:#fff;color:var(--color-brand-500);border-radius:var(--radius-card);text-decoration:none;font-weight:700\">Nhận tư vấn miễn phí</a></div><div style=\"background:rgba(255,255,255,.1);border-radius:var(--radius-card);padding:32px;text-align:center\"><p style=\"font-size:2.5rem;font-weight:800;margin:0\">15+</p><p style=\"margin:4px 0 20px;opacity:.85\">Năm kinh nghiệm</p><p style=\"font-size:2.5rem;font-weight:800;margin:0\">500+</p><p style=\"margin:4px 0 0;opacity:.85\">Khách hàng tin dùng</p></div></div></section><section style=\"padding:var(--space-section) 24px;background:var(--color-surface)\"><div style=\"max-width:1080px;margin:0 auto\"><h2 style=\"font-size:1.85rem;font-weight:800;text-align:center;color:var(--color-ink-900);margin:0 0 12px\">Lĩnh vực hoạt động</h2><p style=\"text-align:center;color:var(--color-ink-500);margin:0 0 40px\">Ba trụ cột dịch vụ chính của chúng tôi.</p><div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:24px\"><div style=\"background:#fff;padding:28px;border:1px solid #e2e8f0;border-radius:var(--radius-card)\"><div style=\"width:44px;height:44px;border-radius:var(--radius-card);background:var(--color-accent-500);margin-bottom:16px\"></div><h3 style=\"margin:0 0 10px;font-size:1.1rem;font-weight:700;color:var(--color-ink-900)\">Tư vấn chiến lược</h3><p style=\"margin:0;color:var(--color-ink-500);line-height:1.65;font-size:.92rem\">Phân tích hiện trạng và xây dựng lộ trình phát triển phù hợp với nguồn lực.</p></div><div style=\"background:#fff;padding:28px;border:1px solid #e2e8f0;border-radius:var(--radius-card)\"><div style=\"width:44px;height:44px;border-radius:var(--radius-card);background:var(--color-accent-500);margin-bottom:16px\"></div><h3 style=\"margin:0 0 10px;font-size:1.1rem;font-weight:700;color:var(--color-ink-900)\">Triển khai công nghệ</h3><p style=\"margin:0;color:var(--color-ink-500);line-height:1.65;font-size:.92rem\">Xây dựng và tích hợp hệ thống theo đúng nhu cầu vận hành thực tế.</p></div><div style=\"background:#fff;padding:28px;border:1px solid #e2e8f0;border-radius:var(--radius-card)\"><div style=\"width:44px;height:44px;border-radius:var(--radius-card);background:var(--color-accent-500);margin-bottom:16px\"></div><h3 style=\"margin:0 0 10px;font-size:1.1rem;font-weight:700;color:var(--color-ink-900)\">Vận hành &amp; hỗ trợ</h3><p style=\"margin:0;color:var(--color-ink-500);line-height:1.65;font-size:.92rem\">Đồng hành lâu dài với đội ngũ hỗ trợ kỹ thuật chuyên trách.</p></div></div></div></section><section style=\"padding:var(--space-section) 24px\"><div style=\"max-width:860px;margin:0 auto;text-align:center\"><h2 style=\"font-size:1.6rem;font-weight:800;color:var(--color-ink-900);margin:0 0 14px\">Bắt đầu cuộc trò chuyện</h2><p style=\"color:var(--color-ink-500);line-height:1.7;margin:0 0 26px\">Để lại thông tin, đội ngũ của chúng tôi sẽ liên hệ trong vòng 24 giờ làm việc.</p><a href=\"/lien-he\" style=\"display:inline-block;padding:14px 34px;background:var(--color-brand-500);color:#fff;border-radius:var(--radius-card);text-decoration:none;font-weight:700\">Liên hệ ngay</a></div></section>",
              "CompiledCss": null,
              "CustomJs": null
            },
            {
              "Title": "Dịch vụ",
              "Slug": "dich-vu",
              "Kind": "Static",
              "LayoutKey": "shell-default",
              "CompiledHtml": "<section style=\"padding:var(--space-section) 24px\"><div style=\"max-width:900px;margin:0 auto\"><h1 style=\"font-size:2rem;font-weight:800;color:var(--color-ink-900);margin:0 0 12px\">Dịch vụ</h1><p style=\"color:var(--color-ink-500);line-height:1.7;margin:0 0 36px\">Danh mục dịch vụ được thiết kế theo từng giai đoạn phát triển của doanh nghiệp.</p><div style=\"display:grid;gap:20px\"><div style=\"padding:24px;border-left:3px solid var(--color-brand-500);background:var(--color-surface);border-radius:0 var(--radius-card) var(--radius-card) 0\"><h3 style=\"margin:0 0 8px;font-weight:700;color:var(--color-ink-900)\">Khảo sát &amp; đánh giá</h3><p style=\"margin:0;color:var(--color-ink-500);line-height:1.65\">Rà soát quy trình hiện tại, xác định điểm nghẽn và cơ hội cải thiện.</p></div><div style=\"padding:24px;border-left:3px solid var(--color-brand-500);background:var(--color-surface);border-radius:0 var(--radius-card) var(--radius-card) 0\"><h3 style=\"margin:0 0 8px;font-weight:700;color:var(--color-ink-900)\">Thiết kế giải pháp</h3><p style=\"margin:0;color:var(--color-ink-500);line-height:1.65\">Đề xuất phương án kèm lộ trình, chi phí và chỉ số đo lường rõ ràng.</p></div><div style=\"padding:24px;border-left:3px solid var(--color-brand-500);background:var(--color-surface);border-radius:0 var(--radius-card) var(--radius-card) 0\"><h3 style=\"margin:0 0 8px;font-weight:700;color:var(--color-ink-900)\">Triển khai &amp; bàn giao</h3><p style=\"margin:0;color:var(--color-ink-500);line-height:1.65\">Thực thi theo từng giai đoạn, đào tạo đội ngũ và bàn giao tài liệu đầy đủ.</p></div></div></div></section>",
              "CompiledCss": null,
              "CustomJs": null
            },
            {
              "Title": "Về chúng tôi",
              "Slug": "gioi-thieu",
              "Kind": "Static",
              "LayoutKey": "shell-default",
              "CompiledHtml": "<section style=\"padding:var(--space-section) 24px\"><div style=\"max-width:760px;margin:0 auto\"><h1 style=\"font-size:2rem;font-weight:800;color:var(--color-ink-900);margin:0 0 16px\">Về chúng tôi</h1><p style=\"color:var(--color-ink-500);line-height:1.8;margin:0 0 16px\">Được thành lập với mục tiêu giúp doanh nghiệp Việt Nam tiếp cận công nghệ một cách thực tế và hiệu quả, chúng tôi tập trung vào những giải pháp tạo ra kết quả đo lường được.</p><p style=\"color:var(--color-ink-500);line-height:1.8;margin:0\">Thay thế nội dung này bằng câu chuyện thật của doanh nghiệp bạn trong màn hình Builder.</p></div></section>",
              "CompiledCss": null,
              "CustomJs": null
            },
            {
              "Title": "Liên hệ",
              "Slug": "lien-he",
              "Kind": "Static",
              "LayoutKey": "shell-default",
              "CompiledHtml": "<section style=\"padding:var(--space-section) 24px\"><div style=\"max-width:640px;margin:0 auto\"><h1 style=\"font-size:2rem;font-weight:800;color:var(--color-ink-900);margin:0 0 12px\">Liên hệ</h1><p style=\"color:var(--color-ink-500);line-height:1.7;margin:0 0 28px\">Chúng tôi sẵn sàng lắng nghe nhu cầu của bạn.</p><div style=\"padding:28px;background:var(--color-surface);border-radius:var(--radius-card);line-height:2\"><p style=\"margin:0;color:var(--color-ink-900)\"><strong>Email:</strong> contact@example.com</p><p style=\"margin:0;color:var(--color-ink-900)\"><strong>Hotline:</strong> 1900 xxxx</p><p style=\"margin:0;color:var(--color-ink-900)\"><strong>Địa chỉ:</strong> Cập nhật địa chỉ trong Builder</p><p style=\"margin:0;color:var(--color-ink-900)\"><strong>Giờ làm việc:</strong> 8:00 - 17:30, Thứ 2 - Thứ 6</p></div></div></section>",
              "CompiledCss": null,
              "CustomJs": null
            }
          ],
          "Categories": [
            { "Name": "Tin công ty", "Slug": "tin-cong-ty", "Description": "Thông tin hoạt động của doanh nghiệp", "Type": "Post", "Order": 1 }
          ],
          "Menus": [
            {
              "Name": "Menu chính",
              "Location": "header",
              "Items": [
                { "Title": "Trang chủ", "Url": "/", "Order": 1, "Target": "_self" },
                { "Title": "Dịch vụ", "Url": "/dich-vu", "Order": 2, "Target": "_self" },
                { "Title": "Về chúng tôi", "Url": "/gioi-thieu", "Order": 3, "Target": "_self" },
                { "Title": "Liên hệ", "Url": "/lien-he", "Order": 4, "Target": "_self" }
              ]
            }
          ],
          "Settings": [
            { "Key": "Site.Description", "Value": "Trang thông tin doanh nghiệp", "Group": "general" }
          ]
        }
        """;


        if (!await db.SiteTemplates.AnyAsync(t => t.Key == "product-landing"))
            db.SiteTemplates.Add(new Domain.Entities.Builder.SiteTemplate
            {
                Key = "product-landing",
                Name = "Landing sản phẩm",
                Description = "Hero + feature grid + CTA + footer. Dùng cho trang giới thiệu sản phẩm/dịch vụ.",
                SpecJson = landingSpec
            });

        if (!await db.SiteTemplates.AnyAsync(t => t.Key == "news-site"))
            db.SiteTemplates.Add(new Domain.Entities.Builder.SiteTemplate
            {
                Key = "news-site",
                Name = "Trang tin tức",
                Description = "Trang chủ với dynamic block post-list + 3 chuyên mục mẫu + menu.",
                SpecJson = newsSpec
            });

        if (!await db.SiteTemplates.AnyAsync(t => t.Key == "corporate"))
            db.SiteTemplates.Add(new Domain.Entities.Builder.SiteTemplate
            {
                Key = "corporate",
                Name = "Trang doanh nghiệp",
                Description = "4 trang (chủ, dịch vụ, giới thiệu, liên hệ) + menu + chuyên mục tin công ty. Tông navy nghiêm túc.",
                SpecJson = corporateSpec
            });

        await db.SaveChangesAsync();
    }

    /* ─── Remove legacy demo commitment seeds; keep real signatures ─── */
    private static async Task RemoveCommitmentSeeds(AppDbContext db, IFileStorage storage)
    {
        var seeds = await db.Commitments
            .IgnoreQueryFilters()
            .Where(x => x.SignerKey.StartsWith("seed-")
                && x.IpHash.StartsWith("seed-")
                && x.UserAgent == "Seed/1.0")
            .ToListAsync();

        if (seeds.Count == 0) return;

        foreach (var seed in seeds)
        {
            var key = ExtractStorageKey(seed.SignatureUrl);
            if (!string.IsNullOrEmpty(key))
            {
                try { await storage.MoveToTrashAsync(key); } catch { /* keep DB cleanup best-effort */ }
            }
        }

        db.Commitments.RemoveRange(seeds);
        await db.SaveChangesAsync();
    }

    /// <summary>Gỡ một quyền đã nghỉ khỏi DB (kèm link vai trò) — dùng cho permission bị bỏ trong code.</summary>
    private static async Task RemoveRetiredPermissionAsync(AppDbContext db, string code)
    {
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
        if (perm == null) return;

        var links = await db.RolePermissions.Where(rp => rp.PermissionId == perm.Id).ToListAsync();
        db.RolePermissions.RemoveRange(links);
        db.Permissions.Remove(perm);
        await db.SaveChangesAsync();
    }

    private static string? ExtractStorageKey(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        const string marker = "/uploads/";
        var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return url.Substring(idx + marker.Length);
    }
}
