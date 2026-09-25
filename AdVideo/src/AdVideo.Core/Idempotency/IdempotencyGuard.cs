namespace AdVideo.Core.Idempotency;

/// <summary>Quyết định cho một request tạo job có kèm <c>Idempotency-Key</c>.</summary>
public enum IdempotencyOutcome
{
    /// <summary>Key mới → tạo job.</summary>
    Create = 0,

    /// <summary>Key đã có và payload giống → trả job cũ, KHÔNG tạo mới.</summary>
    ReturnExisting = 1,

    /// <summary>Không có key → từ chối 400.</summary>
    RejectMissingKey = 2,

    /// <summary>Key đã có nhưng payload KHÁC → xung đột 409.</summary>
    /// <remarks>
    /// Đây là ca nguy hiểm: client dùng lại một key cũ cho một brief mới. Nếu âm thầm tạo job mới
    /// thì mất tác dụng chống trùng; nếu trả job cũ thì khách nhận video sai brief. Phải báo rõ.
    /// </remarks>
    RejectConflict = 3,
}

public sealed record IdempotencyDecision(
    IdempotencyOutcome Outcome,
    Guid? ExistingJobId = null,
    string? Reason = null)
{
    public bool ShouldCreate => Outcome == IdempotencyOutcome.Create;
}

/// <summary>
/// Quyết định tạo job mới hay trả job cũ. Thuần hàm.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao bắt buộc:</b> người dùng bấm hai lần, F5 giữa chừng, hoặc client retry vì timeout —
/// cả ba đều có thể nhân đôi một job tiêu tiền thật. Đây là một trong hai hàng rào chống thủng ví
/// (hàng rào kia là trần chi tiêu), và nó rẻ: một unique index + một phép so sánh hash.
/// </para>
/// <para>
/// <b>Vì sao so hash chứ không so toàn bộ payload:</b> brief JSON có thể vài KB, và so chuỗi lớn
/// trong một transaction dễ sai vì thứ tự trường. Hash cố định độ dài, so sánh rẻ, và lưu một
/// cột <c>RequestHash</c> trên job là đủ.
/// </para>
/// </remarks>
public static class IdempotencyGuard
{
    public static IdempotencyDecision Decide(string? idempotencyKey, Guid? existingJobId,
        string? existingRequestHash, string incomingRequestHash)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new IdempotencyDecision(
                IdempotencyOutcome.RejectMissingKey,
                Reason: "Thiếu header Idempotency-Key. Header này bắt buộc để một request gửi lại không tạo ra job thứ hai — " +
                        "dùng một chuỗi bất kỳ ổn định cho mỗi lần khách bấm nút, ví dụ UUID sinh ở phía client.");
        }

        if (existingJobId is null)
        {
            return new IdempotencyDecision(IdempotencyOutcome.Create);
        }

        // Job cũ tồn tại. So hash để biết đây là retry đúng nghĩa hay là key bị tái sử dụng sai.
        if (string.IsNullOrEmpty(existingRequestHash))
        {
            // Job cũ không có hash (dữ liệu từ trước khi thêm cột) → không kết luận được xung đột.
            // Trả job cũ là lựa chọn an toàn hơn tạo job mới: thà khách phải chủ động tạo lại
            // còn hơn âm thầm tiêu tiền lần hai.
            return new IdempotencyDecision(
                IdempotencyOutcome.ReturnExisting,
                existingJobId,
                "Job với key này đã tồn tại nhưng không có hash request để đối chiếu; trả job cũ.");
        }

        return string.Equals(existingRequestHash, incomingRequestHash, StringComparison.Ordinal)
            ? new IdempotencyDecision(IdempotencyOutcome.ReturnExisting, existingJobId)
            : new IdempotencyDecision(
                IdempotencyOutcome.RejectConflict,
                existingJobId,
                "Idempotency-Key này đã được dùng cho một request KHÁC. Mỗi lần tạo video mới phải dùng một key mới — " +
                "tái sử dụng key khiến hệ thống không phân biệt được 'gửi lại' với 'tạo cái khác'.");
    }
}
