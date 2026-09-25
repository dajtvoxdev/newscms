using AdVideo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).IsRequired().HasMaxLength(100);

        // Value là chuỗi cho mọi kiểu, kiểu thật nằm ở ValueType. Để nvarchar(max) vì
        // SettingValueType.Json cho phép cấu hình dạng cây (ví dụ bảng giá theo model).
        builder.Property(x => x.Value).IsRequired();

        // Description bắt buộc và có trần: đây là thứ người vận hành đọc lúc 2 giờ sáng để biết
        // chỉnh số này thì cái gì đổi. Không mô tả thì setting thành số ma.
        builder.Property(x => x.Description).IsRequired().HasMaxLength(500);

        builder.Property(x => x.MinValue).HasMaxLength(50);
        builder.Property(x => x.MaxValue).HasMaxLength(50);

        builder.HasIndex(x => x.Key)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_SystemSettings_Key");
    }
}
