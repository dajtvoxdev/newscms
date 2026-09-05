namespace NewsCMS.Shared.Constants;

/// <summary>
/// Tập hợp toàn bộ permission code dùng cho RBAC.
/// Quy ước: Module.Subject.Action (PascalCase, dot-separated).
/// </summary>
public static class Permissions
{
    public const string Prefix = "Permission";  // claim type

    public static class Identity
    {
        public const string ViewUser = "Identity.User.View";
        public const string CreateUser = "Identity.User.Create";
        public const string EditUser = "Identity.User.Edit";
        public const string DeleteUser = "Identity.User.Delete";
        public const string ViewRole = "Identity.Role.View";
        public const string ManageRole = "Identity.Role.Manage";
        public const string AssignPermission = "Identity.Permission.Assign";
    }

    public static class Content
    {
        public const string ViewPost = "Content.Post.View";
        public const string CreatePost = "Content.Post.Create";
        public const string EditPost = "Content.Post.Edit";
        public const string DeletePost = "Content.Post.Delete";
        public const string PublishPost = "Content.Post.Publish";
        public const string ManageCategory = "Content.Category.Manage";
        public const string ManageTag = "Content.Tag.Manage";
    }

    public static class Media
    {
        public const string View = "Media.View";
        public const string Upload = "Media.Upload";
        public const string Edit = "Media.Edit";
        public const string Delete = "Media.Delete";
        public const string ManageFolder = "Media.ManageFolder";
    }

    public static class Site
    {
        public const string ManageMenu = "Site.Menu.Manage";
        public const string ManageBanner = "Site.Banner.Manage";
        public const string EditSetting = "Site.Setting.Edit";
    }

    public static class Builder
    {
        public const string PageView = "Builder.Page.View";
        public const string PageCreate = "Builder.Page.Create";
        public const string PageEdit = "Builder.Page.Edit";
        public const string PageDelete = "Builder.Page.Delete";
        public const string PagePublish = "Builder.Page.Publish";
        public const string LayoutManage = "Builder.Layout.Manage";
        public const string BlockManage = "Builder.Block.Manage";
        /// <summary>Custom JS: chỉ root và admin site được tin cậy mới có quyền này.</summary>
        public const string CodeManage = "Builder.Code.Manage";
        /// <summary>Quản lý SiteTemplate — chỉ SuperAdmin (root).</summary>
        public const string TemplateManage = "Builder.Template.Manage";
        public const string RevisionRestore = "Builder.Revision.Restore";
    }

    public static class Seo
    {
        public const string EditMeta = "Seo.Meta.Edit";
        public const string ManageRedirect = "Seo.Redirect.Manage";
        public const string RebuildSitemap = "Seo.Sitemap.Rebuild";
        public const string RouteManage = "Seo.Route.Manage";
    }

    public static class Agent
    {
        public const string ApiKeyManage = "Agent.ApiKey.Manage";
    }

    public static class Form
    {
        public const string ManageBuilder = "Form.Builder.Manage";
        public const string ViewSubmission = "Form.Submission.View";
    }

    public static class Audit
    {
        public const string ViewLog = "Audit.Log.View";
    }

    public static class Analytics
    {
        public const string ViewDashboard = "Analytics.Dashboard.View";
    }

    public static class Engagement
    {
        public const string ViewCommitments = "Engagement.Commitments.View";
        public const string HideCommitments = "Engagement.Commitments.Hide";
    }

    public static class Ar
    {
        public const string ViewAr    = "Ar.Experience.View";
        public const string CreateAr  = "Ar.Experience.Create";
        public const string EditAr    = "Ar.Experience.Edit";
        public const string DeleteAr  = "Ar.Experience.Delete";
        public const string PublishAr = "Ar.Experience.Publish";
    }

    public static class Survey
    {
        public const string EditSetting = "Survey.Setting.Edit";
    }

    public static class Catalog
    {
        public const string ViewProduct = "Catalog.Product.View";
        public const string CreateProduct = "Catalog.Product.Create";
        public const string EditProduct = "Catalog.Product.Edit";
        public const string DeleteProduct = "Catalog.Product.Delete";
        public const string PublishProduct = "Catalog.Product.Publish";
        public const string ManageCategory = "Catalog.Category.Manage";
    }

    public static class Ai
    {
        public const string ManageConnection = "Ai.Connection.Manage";
        public const string ManageSkill = "Ai.Skill.Manage";
        public const string UseAssist = "Ai.Assist.Use";
    }

    public static class KeoBia
    {
        public const string ViewPlayers = "KeoBia.Player.View";
        public const string ManageMatches = "KeoBia.Match.Manage";
        public const string ImportSchedule = "KeoBia.Schedule.Import";
        public const string ViewBets = "KeoBia.Bet.View";
        public const string SendTelegram = "KeoBia.Telegram.Send";
        public const string ManageQuiz = "KeoBia.Quiz.Manage";
        public const string ManagePayments = "KeoBia.Payment.Manage";
    }

    /// <summary>Tất cả permission có sẵn - dùng khi seed DB.</summary>
    public static IEnumerable<(string Code, string Module, string DisplayName)> All()
    {
        yield return (Identity.ViewUser, "Identity", "Xem người dùng");
        yield return (Identity.CreateUser, "Identity", "Tạo người dùng");
        yield return (Identity.EditUser, "Identity", "Sửa người dùng");
        yield return (Identity.DeleteUser, "Identity", "Xoá người dùng");
        yield return (Identity.ViewRole, "Identity", "Xem vai trò");
        yield return (Identity.ManageRole, "Identity", "Quản lý vai trò");
        yield return (Identity.AssignPermission, "Identity", "Gán quyền cho vai trò");

        yield return (Content.ViewPost, "Content", "Xem bài viết");
        yield return (Content.CreatePost, "Content", "Tạo bài viết");
        yield return (Content.EditPost, "Content", "Sửa bài viết");
        yield return (Content.DeletePost, "Content", "Xoá bài viết");
        yield return (Content.PublishPost, "Content", "Xuất bản bài viết");
        yield return (Content.ManageCategory, "Content", "Quản lý chuyên mục");
        yield return (Content.ManageTag, "Content", "Quản lý tag");

        yield return (Media.View, "Media", "Xem thư viện media");
        yield return (Media.Upload, "Media", "Tải lên media");
        yield return (Media.Edit, "Media", "Sửa thông tin media");
        yield return (Media.Delete, "Media", "Xoá media");
        yield return (Media.ManageFolder, "Media", "Quản lý thư mục media");

        yield return (Site.ManageMenu, "Site", "Quản lý menu");
        yield return (Site.ManageBanner, "Site", "Quản lý banner");
        yield return (Site.EditSetting, "Site", "Sửa cấu hình site");

        yield return (Seo.EditMeta, "Seo", "Sửa meta SEO");
        yield return (Seo.ManageRedirect, "Seo", "Quản lý redirect");
        yield return (Seo.RebuildSitemap, "Seo", "Rebuild sitemap");

        yield return (Form.ManageBuilder, "Form", "Quản lý form");
        yield return (Form.ViewSubmission, "Form", "Xem submission");

        yield return (Audit.ViewLog, "Audit", "Xem audit log");

        yield return (Analytics.ViewDashboard, "Analytics", "Xem thống kê truy cập");

        yield return (Engagement.ViewCommitments, "Engagement", "Xem danh sách cam kết");
        yield return (Engagement.HideCommitments, "Engagement", "Ẩn / hiện cam kết");

        yield return (Ar.ViewAr,    "Ar", "Xem trải nghiệm AR");
        yield return (Ar.CreateAr,  "Ar", "Tạo trải nghiệm AR");
        yield return (Ar.EditAr,    "Ar", "Sửa trải nghiệm AR");
        yield return (Ar.DeleteAr,  "Ar", "Xoá trải nghiệm AR");
        yield return (Ar.PublishAr, "Ar", "Xuất bản trải nghiệm AR");

        yield return (Survey.EditSetting, "Survey", "Cấu hình khảo sát nhận thức");

        yield return (Catalog.ViewProduct,    "Catalog", "Xem sản phẩm");
        yield return (Catalog.CreateProduct,  "Catalog", "Tạo sản phẩm");
        yield return (Catalog.EditProduct,    "Catalog", "Sửa sản phẩm");
        yield return (Catalog.DeleteProduct,  "Catalog", "Xoá sản phẩm");
        yield return (Catalog.PublishProduct, "Catalog", "Xuất bản sản phẩm");
        yield return (Catalog.ManageCategory, "Catalog", "Quản lý chuyên mục sản phẩm");

        yield return (Ai.ManageConnection, "Ai", "Quản lý kết nối AI");
        yield return (Ai.ManageSkill, "Ai", "Quản lý kỹ năng AI");
        yield return (Ai.UseAssist, "Ai", "Sử dụng trợ lý AI");

        yield return (KeoBia.ViewPlayers, "KeoBia", "Xem tracking người chơi Kèo Bia");
        yield return (KeoBia.ManageMatches, "KeoBia", "Quản lý lịch và kết quả Kèo Bia");
        yield return (KeoBia.ImportSchedule, "KeoBia", "Đồng bộ lịch thi đấu Bia Vui");
        yield return (KeoBia.ViewBets, "KeoBia", "Xem hoạt động đặt kèo Kèo Bia");
        yield return (KeoBia.SendTelegram, "KeoBia", "Gửi tin nhắn Telegram nhóm");
        yield return (KeoBia.ManageQuiz, "KeoBia", "Quản lý hỏi đáp nhanh");
        yield return (KeoBia.ManagePayments, "KeoBia", "Quản lý giao dịch nộp cốc bia và gói lạc");

        // Builder (Site Builder / theme Universal)
        yield return (Builder.PageView, "Builder", "Xem trang builder");
        yield return (Builder.PageCreate, "Builder", "Tạo trang builder");
        yield return (Builder.PageEdit, "Builder", "Sửa trang builder");
        yield return (Builder.PageDelete, "Builder", "Xoá trang builder");
        yield return (Builder.PagePublish, "Builder", "Xuất bản trang builder");
        yield return (Builder.LayoutManage, "Builder", "Quản lý layout builder");
        yield return (Builder.BlockManage, "Builder", "Quản lý khối builder");
        yield return (Builder.CodeManage, "Builder", "Quản lý custom JS/CSS builder");
        yield return (Builder.TemplateManage, "Builder", "Quản lý mẫu site (root)");
        yield return (Builder.RevisionRestore, "Builder", "Khôi phục revision trang");

        yield return (Seo.RouteManage, "Seo", "Quản lý route SEO");
        yield return (Agent.ApiKeyManage, "Agent", "Quản lý API key cho agent/MCP");
    }
}
