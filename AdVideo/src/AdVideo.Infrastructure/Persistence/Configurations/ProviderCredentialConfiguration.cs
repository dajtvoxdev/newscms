using AdVideo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdVideo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Cấu hình bảng credential. Converter mã hoá cho <c>EncryptedApiKey</c> KHÔNG gắn ở đây mà ở
/// <see cref="AdVideoDbContext.OnModelCreating"/> — configuration được EF dựng bằng constructor
/// không tham số nên không lấy được <see cref="IApiKeyProtector"/>.
/// </summary>
public sealed class ProviderCredentialConfiguration : IEntityTypeConfiguration<ProviderCredential>
{
    public void Configure(EntityTypeBuilder<ProviderCredential> builder)
    {
        builder.ToTable("ProviderCredentials");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).IsRequired().HasMaxLength(50);
        builder.Property(x => x.ModelId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.EndpointUrl).HasMaxLength(500);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.DailyCostLimitUsd).HasPrecision(18, 6);

        // Ciphertext để nvarchar(max): Data Protection sinh chuỗi dài hơn plaintext nhiều lần và
        // độ dài đó phụ thuộc phiên bản thuật toán. Đặt trần ở đây là hẹn một ngày bị cắt mất
        // đuôi ciphertext, mà cắt đuôi thì không giải mã được và key coi như mất.
        builder.Property(x => x.EncryptedApiKey).IsRequired();

        // Manifest capability là JSON đọc lúc chạy (D10) — schema của nó đổi theo provider nên
        // không tách cột.
        builder.Property(x => x.CapabilityJson).IsRequired();

        // Một provider + model + loại + phạm vi chỉ có đúng một credential sống. TenantId null
        // (scope System) tham gia index được vì SQL Server coi các NULL là bằng nhau trong unique
        // index — đúng cái ta cần: chỉ một dòng System cho mỗi tổ hợp.
        builder.HasIndex(x => new { x.Provider, x.ModelId, x.Category, x.TenantId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_ProviderCredentials_Provider_Model_Category_Tenant");

        // Chọn provider lúc chạy: lọc theo loại, còn sống, rồi sắp theo Priority.
        builder.HasIndex(x => new { x.Category, x.IsActive, x.Priority })
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_ProviderCredentials_Category_Active_Priority");
    }
}
