using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.Ai;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class AiSkillConfiguration : IEntityTypeConfiguration<AiSkill>
{
    public void Configure(EntityTypeBuilder<AiSkill> builder)
    {
        builder.ToTable("AiSkills");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Key).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Property(x => x.Key).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.SystemPrompt).HasMaxLength(5000);
        builder.Property(x => x.UserPromptTemplate).HasMaxLength(5000);
        builder.Property(x => x.Targets).HasMaxLength(500);
        builder.Property(x => x.ToolType).HasMaxLength(100);
        builder.Property(x => x.BaseUrl).HasMaxLength(500);
        builder.Property(x => x.ApiKeyEncrypted).HasMaxLength(2000);
        builder.Property(x => x.ConfigJson).HasMaxLength(5000);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
