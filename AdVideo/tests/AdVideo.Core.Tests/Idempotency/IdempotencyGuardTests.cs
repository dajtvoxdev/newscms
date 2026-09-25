using AdVideo.Core.Idempotency;
using FluentAssertions;

namespace AdVideo.Core.Tests.Idempotency;

/// <summary>
/// Hàng rào chống thủng ví thứ hai (cùng <see cref="Core.Costing.CostEstimator"/>).
/// </summary>
/// <remarks>
/// Ba kịch bản đời thật đẻ ra bộ test này: khách bấm nút hai lần, khách F5 giữa lúc chờ, và client
/// tự retry vì timeout. Cả ba đều là "cùng một request gửi hai lần", và cả ba đều tiêu tiền thật
/// lần thứ hai nếu không có lớp này.
/// </remarks>
public class IdempotencyGuardTests
{
    private const string Hash = "sha256:aaa";
    private static readonly Guid ExistingJob = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Thieu_key_thi_tu_choi_va_chi_luon_cach_sua(string? key)
    {
        // Từ chối mà không nói cách sửa thì client sẽ sửa bằng cách bỏ header đi — tức là bỏ luôn
        // lớp chống trùng. Lý do ở đây là phần việc chính của nhánh này, không phải phần trang trí.
        var decision = IdempotencyGuard.Decide(key, existingJobId: null, existingRequestHash: null, Hash);

        decision.Outcome.Should().Be(IdempotencyOutcome.RejectMissingKey);
        decision.ShouldCreate.Should().BeFalse();
        decision.ExistingJobId.Should().BeNull();
        decision.Reason.Should().Contain("Idempotency-Key").And.Contain("UUID");
    }

    [Fact]
    public void Key_moi_thi_tao_job()
    {
        var decision = IdempotencyGuard.Decide("key-1", existingJobId: null, existingRequestHash: null, Hash);

        decision.Outcome.Should().Be(IdempotencyOutcome.Create);
        decision.ShouldCreate.Should().BeTrue();
        decision.ExistingJobId.Should().BeNull();
    }

    [Fact]
    public void Gui_lai_dung_request_cu_thi_tra_job_cu_chu_khong_tieu_tien_lan_hai()
    {
        var decision = IdempotencyGuard.Decide("key-1", ExistingJob, Hash, Hash);

        decision.Outcome.Should().Be(IdempotencyOutcome.ReturnExisting);
        decision.ShouldCreate.Should().BeFalse();
        decision.ExistingJobId.Should().Be(ExistingJob);
    }

    [Fact]
    public void Dung_lai_key_cu_cho_brief_moi_thi_bao_xung_dot_chu_khong_doan_y()
    {
        // Hai lối xử lý "dễ" đều sai: tạo job mới thì lớp chống trùng vô nghĩa, trả job cũ thì khách
        // nhận video của brief khác. Chỉ còn cách báo lỗi.
        var decision = IdempotencyGuard.Decide("key-1", ExistingJob, Hash, "sha256:bbb");

        decision.Outcome.Should().Be(IdempotencyOutcome.RejectConflict);
        decision.ShouldCreate.Should().BeFalse();
        decision.ExistingJobId.Should().Be(ExistingJob, "409 phải chỉ được job đang chiếm key đó");
        decision.Reason.Should().Contain("đã được dùng cho một request KHÁC");
    }

    [Fact]
    public void So_hash_phan_biet_hoa_thuong()
    {
        // Hash hex chỉ khác nhau ở chữ hoa/thường thì gần như chắc chắn là lỗi sinh hash ở đâu đó.
        // Coi là giống nhau sẽ giấu lỗi đó đi và trả về một job không phải của request này.
        IdempotencyGuard.Decide("key-1", ExistingJob, "sha256:AAA", "sha256:aaa")
            .Outcome.Should().Be(IdempotencyOutcome.RejectConflict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Job_cu_khong_co_hash_thi_tra_job_cu_chu_khong_tao_moi(string? existingHash)
    {
        // Dữ liệu có từ trước khi thêm cột RequestHash. Không kết luận được, nên chọn phương án
        // không tiêu tiền: khách chủ động tạo lại vẫn rẻ hơn một job trùng tự chạy.
        var decision = IdempotencyGuard.Decide("key-1", ExistingJob, existingHash, Hash);

        decision.Outcome.Should().Be(IdempotencyOutcome.ReturnExisting);
        decision.ExistingJobId.Should().Be(ExistingJob);
        decision.Reason.Should().Contain("không có hash");
    }

    [Fact]
    public void Khong_co_key_thi_khong_xet_gi_them()
    {
        // Thiếu key được xét TRƯỚC mọi thứ khác: có job cũ hay không cũng không cứu được một
        // request không có cách nào nhận diện.
        IdempotencyGuard.Decide(null, ExistingJob, Hash, Hash)
            .Outcome.Should().Be(IdempotencyOutcome.RejectMissingKey);
    }

    [Fact]
    public void ShouldCreate_chi_dung_o_dung_mot_ket_qua()
    {
        // ShouldCreate là thứ code gọi thật sự rẽ nhánh theo. Nếu có ngày nó true ở một outcome
        // thứ hai thì một trong các nhánh từ chối ở trên sẽ âm thầm tạo job.
        var outcomes = Enum.GetValues<IdempotencyOutcome>()
            .Select(o => new IdempotencyDecision(o))
            .Where(d => d.ShouldCreate)
            .Select(d => d.Outcome);

        outcomes.Should().Equal(IdempotencyOutcome.Create);
    }
}
