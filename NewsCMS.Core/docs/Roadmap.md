# Lộ trình phát triển NewsCMS.Core

| Phase | Mục tiêu | Sản phẩm bàn giao | Ước tính |
|-------|----------|-------------------|----------|
| 0 - Setup | Solution + CI/CD + code style | Build OK, repo Git, README | 3-5 ngày |
| 1 - Identity + Admin shell | Identity + RBAC + Layout AdminLTE/Tabler | Login, dashboard, CRUD User/Role/Permission | 1-2 tuần |
| 2 - Content + Media | Post, Category, Tag, Media library, rich editor | Đăng bài đầy đủ qua admin | 2 tuần |
| 3 - Site builder | Menu, Banner, Page tĩnh, Site settings | Cấu hình toàn site từ admin | 1 tuần |
| 4 - SEO + Form | SeoMeta, sitemap.xml, redirect, form builder | SEO-ready + form liên hệ động | 1 tuần |
| 5 - Theme + Default landing | View expander, ThemeStartup, theme Bootstrap | Public site chạy được | 1-2 tuần |
| 6 - Hardening + Release v1 | Cache, log, rate limit, Docker, CI/CD, docs | Bộ core v1.0 release | 1 tuần |

## Definition of Done mỗi phase

- Có unit test cho service chính (coverage ≥ 60%).
- Có migration EF Core + seed demo.
- Có tài liệu module (markdown trong `/docs`).
- Pass code review, không warning, pass Roslyn analyzers.

## Backlog mở rộng (sau v1)

- Multi-tenant trong cùng instance.
- Multi-language content (vi/en).
- Comment system (built-in hoặc Disqus).
- Newsletter / email subscribe + Hangfire job gửi định kỳ.
- Search index Lucene/Meilisearch thay LIKE.
- WebHook + REST API public cho mobile app.
- Admin SPA bằng Blazor Server (thay Razor Pages, tuỳ chọn).
