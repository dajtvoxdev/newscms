using NewsCMS.Application.Ai;

namespace NewsCMS.Tests;

/// <summary>
/// Bộ tách phản hồi chat của AI. Đây là chỗ dễ vỡ nhất khi đổi model hoặc prompt:
/// model hay bọc JSON trong ```json ... ```, thêm lời dẫn, hoặc trả về văn bản
/// tự do — mọi trường hợp đó đều phải rơi về một kết quả dùng được, không ném lỗi.
/// </summary>
public class AiChatResponseParserTests
{
    [Fact]
    public void Parse_JsonThuan_TachDuBaPhan()
    {
        var raw = """
            {"reply":"Đã viết xong.","title":"Cà phê rang mộc","excerpt":"Bài giới thiệu ngắn.","body":"<p>Nội dung</p>"}
            """;

        var (result, structured) = AiChatResponseParser.Parse(raw, isProduct: false);

        Assert.True(structured);
        Assert.Equal("Đã viết xong.", result.AssistantText);
        Assert.Equal("Cà phê rang mộc", result.Title);
        Assert.Equal("Bài giới thiệu ngắn.", result.Excerpt);
        Assert.Equal("<p>Nội dung</p>", result.Body);
    }

    [Fact]
    public void Parse_JsonBocTrongCodeFence_VanTachDuoc()
    {
        var raw = """
            Đây là bản nháp:
            ```json
            {"reply":"Xong","title":"Tiêu đề","excerpt":"Tóm tắt","body":"<p>Body</p>"}
            ```
            Chúc bạn một ngày tốt lành!
            """;

        var (result, structured) = AiChatResponseParser.Parse(raw, isProduct: false);

        Assert.True(structured);
        Assert.Equal("Tiêu đề", result.Title);
        Assert.Equal("<p>Body</p>", result.Body);
    }

    [Fact]
    public void Parse_ThieuTruongReply_DungCauMacDinh()
    {
        var raw = """{"title":"T","excerpt":"E","body":"<p>B</p>"}""";

        var (result, structured) = AiChatResponseParser.Parse(raw, isProduct: false);

        Assert.True(structured);
        Assert.Equal("Đã cập nhật bản nháp.", result.AssistantText);
    }

    [Fact]
    public void Parse_KhongPhanBietHoaThuongTenTruong()
    {
        var raw = """{"Reply":"R","Title":"T","Excerpt":"E","Body":"<p>B</p>"}""";

        var (result, structured) = AiChatResponseParser.Parse(raw, isProduct: false);

        Assert.True(structured);
        Assert.Equal("R", result.AssistantText);
        Assert.Equal("T", result.Title);
    }

    [Fact]
    public void Parse_ChiCoBody_VanCoiLaStructured()
    {
        // Model bỏ tiêu đề/tóm tắt nhưng vẫn trả JSON: phần body dùng được, và
        // client ẩn nút Áp dụng của 2 phần rỗng nên không ghi đè dữ liệu cũ.
        var raw = """{"body":"<p>Chỉ có nội dung</p>"}""";

        var (result, structured) = AiChatResponseParser.Parse(raw, isProduct: false);

        Assert.True(structured);
        Assert.Equal("<p>Chỉ có nội dung</p>", result.Body);
        Assert.Empty(result.Title);
        Assert.Empty(result.Excerpt);
    }

    [Fact]
    public void Parse_VanBanTuDo_RoiVeBodyNguyenVan()
    {
        var raw = "Xin chào! Bạn muốn viết về chủ đề gì?";

        var (result, structured) = AiChatResponseParser.Parse(raw, isProduct: false);

        Assert.False(structured);
        Assert.Equal(raw, result.Body);
        Assert.Empty(result.Title);
        Assert.Contains("văn bản tự do", result.AssistantText);
    }

    [Fact]
    public void Parse_VanBanTuDo_TenSanPham_DoiCauThongBao()
    {
        var (result, structured) = AiChatResponseParser.Parse("nội dung thô", isProduct: true);

        Assert.False(structured);
        Assert.Contains("mô tả chi tiết", result.AssistantText);
        Assert.DoesNotContain("tiêu đề", result.AssistantText);
    }

    [Fact]
    public void Parse_JsonHong_RoiVeVanBanTuDo()
    {
        var raw = """{"title": "thiếu dấu ngoặc đóng""";

        var (result, structured) = AiChatResponseParser.Parse(raw, isProduct: false);

        Assert.False(structured);
        Assert.Equal(raw, result.Body);
    }

    [Fact]
    public void Parse_JsonLaMangKhongPhaiObject_RoiVeVanBanTuDo()
    {
        var raw = """["không phải object"]""";

        var (result, structured) = AiChatResponseParser.Parse(raw, isProduct: false);

        Assert.False(structured);
        Assert.Equal(raw, result.Body);
    }

    [Fact]
    public void Parse_ChuoiRong_KhongNemLoi()
    {
        var (result, structured) = AiChatResponseParser.Parse(string.Empty, isProduct: false);

        Assert.False(structured);
        Assert.Empty(result.Body);
        Assert.Empty(result.Title);
    }

    [Fact]
    public void Parse_JsonLongTrongVanBanCoNgoacNhon_TachDung()
    {
        // Lời dẫn chứa dấu { } — phải lấy từ { đầu tiên tới } cuối cùng.
        var raw = """Kết quả {như sau}: {"title":"T","excerpt":"E","body":"<p>B</p>"}""";

        var (result, structured) = AiChatResponseParser.Parse(raw, isProduct: false);

        Assert.True(structured);
        Assert.Equal("T", result.Title);
    }
}
