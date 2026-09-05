namespace NewsCMS.Domain.Enums;

/// <summary>Vai trò của một layout trong cấu trúc trang Universal.</summary>
public enum LayoutKind
{
    /// <summary>Khung trang đầy đủ (html/head/body) bọc nội dung page.</summary>
    Shell = 0,
    Header = 1,
    Footer = 2,
    Sidebar = 3
}
