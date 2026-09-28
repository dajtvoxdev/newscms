using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AdVideo.Tests.Infrastructure;

/// <summary>
/// Lưu mọi cột <c>decimal</c> thành <c>REAL</c> khi chạy trên Sqlite — chỉ dùng cho host test API.
/// </summary>
/// <remarks>
/// <para>
/// Sqlite không có kiểu decimal nên EF từ chối dịch <c>SUM</c> trên cột decimal. API có đúng một
/// truy vấn như vậy (tổng chi tiêu hôm nay, để áp trần ngày) và nó chạy ở MỌI request tạo job, nên
/// không có nó thì không test được endpoint nào ghi job.
/// </para>
/// <para>
/// <b>Không dùng trong <see cref="PipelineTestHost"/>.</b> Test pipeline so tiền đến từng xu, và đi
/// qua <c>double</c> là mất đúng độ chính xác đó. Test API chỉ cần trần ngày chạy được, không cần
/// so số lẻ.
/// </para>
/// </remarks>
public sealed class SqliteDecimalModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entity.GetProperties()
                         .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                property.SetProviderClrType(typeof(double));
            }
        }
    }
}
