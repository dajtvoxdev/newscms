using AdVideo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

public sealed class VoiceProfileConfiguration : IEntityTypeConfiguration<VoiceProfile>
{
    public void Configure(EntityTypeBuilder<VoiceProfile> builder)
    {
        builder.ToTable("VoiceProfiles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Provider).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ProviderVoiceId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.PreviewUrl).HasMaxLength(1000);
        builder.Property(x => x.SampleObjectKey).HasMaxLength(500);
        builder.Property(x => x.ConsentStatement).HasMaxLength(2000);
        builder.Property(x => x.ConsentedBy).HasMaxLength(200);

        // Danh sách chọn giọng của một tenant: preset (TenantId null) + giọng của tenant, theo thứ tự.
        builder.HasIndex(x => new { x.TenantId, x.SortOrder });

        // Một voice id của một engine chỉ là MỘT giọng có sẵn — nhập hai lần từ thư viện thì hiện
        // hai dòng giống hệt trong danh sách chọn.
        builder.HasIndex(x => new { x.Provider, x.ProviderVoiceId, x.TenantId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_VoiceProfiles_Provider_Voice_Tenant");
    }
}
