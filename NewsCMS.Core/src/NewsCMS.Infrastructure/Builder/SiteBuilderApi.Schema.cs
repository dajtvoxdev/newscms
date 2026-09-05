namespace NewsCMS.Infrastructure.Builder;

/// <summary>
/// JSON Schema tự mô tả của SiteSpec. Tách riêng khỏi SiteBuilderApi.cs vì đây là hằng tài liệu
/// dài, không phải logic — agent gọi GetSchemaAsync đọc chuỗi này để biết dựng payload ApplyAsync.
/// </summary>
public sealed partial class SiteBuilderApi
{
    private const string SpecJsonSchema = """
    {
      "type": "object",
      "description": "Khai báo toàn bộ nội dung site. Áp dụng idempotent: gọi lại cùng một spec không tạo bản ghi trùng. Không xoá thứ nằm ngoài spec.",
      "properties": {
        "siteName": { "type": "string" },
        "siteSlug": { "type": "string", "description": "Chỉ để đối chiếu — ApplyAsync không đổi slug site." },
        "primaryDomain": { "type": ["string", "null"], "description": "Chỉ để đối chiếu — ApplyAsync không gán domain." },
        "defaultTheme": { "type": "string" },
        "defaultCulture": { "type": "string", "examples": ["vi", "en"] },
        "supportedCultures": { "type": "string", "description": "CSV, ví dụ \"vi,en\"." },
        "layouts": {
          "type": "array",
          "description": "Khoá tự nhiên: key.",
          "items": {
            "type": "object",
            "required": ["key", "name", "kind"],
            "properties": {
              "key": { "type": "string" },
              "name": { "type": "string" },
              "kind": { "type": "string", "enum": ["Shell", "Header", "Footer", "Sidebar"] },
              "compiledHtml": { "type": ["string", "null"], "description": "Layout Shell phải chứa một phần tử có thuộc tính data-nc-body — nội dung page được thế vào đó." },
              "compiledCss": { "type": ["string", "null"] },
              "customCss": { "type": ["string", "null"], "description": "CSS viết tay của layout — renderer nối SAU compiledCss nên luôn thắng. Shell builder trực quan chỉ ghi compiledCss, viết ở đây thì lần lưu builder sau không xoá mất." },
              "customJs": { "type": ["string", "null"] },
              "isDefault": { "type": "boolean" }
            }
          }
        },
        "pages": {
          "type": "array",
          "description": "Khoá tự nhiên: slug. Slug \"home\"/\"index\"/rỗng ánh xạ tới path \"/\". Page tạo qua ApplyAsync được publish ngay.",
          "items": {
            "type": "object",
            "required": ["title", "slug"],
            "properties": {
              "title": { "type": "string" },
              "slug": { "type": "string" },
              "compiledHtml": { "type": ["string", "null"], "description": "HTML được sanitize server-side: thẻ script và thuộc tính on* bị loại bỏ. Dùng customJs cho JavaScript." },
              "compiledCss": { "type": ["string", "null"] },
              "customCss": { "type": ["string", "null"], "description": "CSS tay nối SAU compiledCss nên luôn thắng khi trùng selector." },
              "customJs": { "type": ["string", "null"], "description": "Cần quyền Builder.Code.Manage." },
              "kind": { "type": "string", "enum": ["Landing", "Static", "CategoryTemplate", "PostTemplate", "ProductTemplate", "ArchiveTemplate", "SystemError"] },
              "layoutKey": { "type": ["string", "null"], "description": "Khớp layouts[].key. Không khớp thì dùng layout mặc định và trả về warning." },
              "parentSlug": { "type": ["string", "null"], "description": "Slug của trang cha (trang listing). Dùng cho template chi tiết." },
              "isDefaultTemplate": { "type": "boolean", "description": "Đặt làm template mặc định cho loại trang/nội dung này." }
            }
          }
        },
        "categories": {
          "type": "array",
          "description": "Khoá tự nhiên: slug.",
          "items": {
            "type": "object",
            "required": ["name", "slug"],
            "properties": {
              "name": { "type": "string" },
              "slug": { "type": "string" },
              "description": { "type": ["string", "null"] },
              "type": { "type": "string", "enum": ["Post", "Product", "Page"] },
              "order": { "type": "integer" },
              "templatePageSlug": { "type": ["string", "null"], "description": "Slug của trang builder dùng làm template listing cho chuyên mục này." }
            }
          }
        },
        "menus": {
          "type": "array",
          "description": "Khoá tự nhiên: location. Danh sách item được thay trọn bộ khi có khác biệt.",
          "items": {
            "type": "object",
            "required": ["name", "location"],
            "properties": {
              "name": { "type": "string" },
              "location": { "type": "string", "examples": ["header", "footer", "sidebar"] },
              "items": {
                "type": "array",
                "items": {
                  "type": "object",
                  "required": ["title", "url"],
                  "properties": {
                    "title": { "type": "string" },
                    "url": { "type": "string" },
                    "order": { "type": "integer" },
                    "target": { "type": ["string", "null"], "examples": ["_self", "_blank"] }
                  }
                }
              }
            }
          }
        },
        "designTokens": {
          "type": "array",
          "description": "Khoá tự nhiên: group + key. Mỗi token sinh ra cả CSS variable (--color-brand-500) lẫn utility Tailwind (bg-brand-500) từ cùng một nguồn.",
          "items": {
            "type": "object",
            "required": ["group", "key", "value"],
            "properties": {
              "group": { "type": "string", "enum": ["Color", "Font", "Space", "Radius", "Shadow"] },
              "key": { "type": "string", "examples": ["brand-500", "display", "card"] },
              "value": { "type": "string", "examples": ["#0d7c66", "14px"] },
              "sortOrder": { "type": "integer" }
            }
          }
        },
        "settings": {
          "type": "array",
          "description": "Khoá tự nhiên: key.",
          "items": {
            "type": "object",
            "required": ["key"],
            "properties": {
              "key": { "type": "string" },
              "value": { "type": ["string", "null"] },
              "group": { "type": "string", "examples": ["general", "seo", "builder.code"] }
            }
          }
        }
      }
    }
    """;
}
