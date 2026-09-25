using AdVideo.Core.Enums;
using FluentAssertions;

namespace AdVideo.Core.Tests.Enums;

/// <summary>
/// Trạng thái job: đã kết thúc hẳn chưa, và đang chờ ai.
/// </summary>
/// <remarks>
/// <c>IsTerminal</c> là thứ worker hỏi trước khi nhận lại một job. Trả nhầm true cho một trạng
/// thái đang chạy thì job bị bỏ giữa chừng; trả nhầm false cho một trạng thái đã xong thì worker
/// chạy lại một job đã giao khách — và chạy lại nghĩa là gọi provider lần nữa.
/// </remarks>
public class JobStatusExtensionsTests
{
    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public void Ba_trang_thai_ket_thuc_han(JobStatus status)
    {
        status.IsTerminal().Should().BeTrue();
    }

    [Fact]
    public void Moi_trang_thai_con_lai_deu_con_chay_tiep_duoc()
    {
        // Viết theo kiểu quét cả enum chứ không liệt kê tay: thêm một bước vào pipeline mà quên
        // xét ở đây thì test đỏ ngay, thay vì đợi một job bị worker bỏ quên.
        var running = Enum.GetValues<JobStatus>()
            .Where(s => s is not (JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled));

        running.Should().OnlyContain(s => !s.IsTerminal());
        running.Should().HaveCountGreaterThan(5, "pipeline 9 bước phải có nhiều hơn 5 trạng thái đang chạy");
    }

    [Fact]
    public void Chi_cho_duyet_la_dang_cho_nguoi()
    {
        // UI phải hiển thị khác đi: "đang xử lý" với một job thật ra đang đợi khách bấm nút là
        // cách chắc chắn để job đó nằm im vài ngày.
        JobStatus.AwaitingApproval.NeedsHuman().Should().BeTrue();

        Enum.GetValues<JobStatus>()
            .Where(s => s != JobStatus.AwaitingApproval)
            .Should().OnlyContain(s => !s.NeedsHuman());
    }

    [Fact]
    public void Trang_thai_la_khong_duoc_coi_la_da_ket_thuc()
    {
        // Giá trị rác từ DB (cột int) không được phép làm worker bỏ job. Coi là "chưa xong" thì
        // hậu quả xấu nhất là một job kẹt hiện rõ trong hàng đợi — nhìn thấy được, sửa được.
        ((JobStatus)999).IsTerminal().Should().BeFalse();
        ((JobStatus)999).NeedsHuman().Should().BeFalse();
    }
}
