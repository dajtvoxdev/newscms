using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsCMS.Domain.Entities.KeoBia;

namespace NewsCMS.Infrastructure.Persistence.Configurations;

public class KeoBiaPlayerConfiguration : IEntityTypeConfiguration<KeoBiaPlayer>
{
    public void Configure(EntityTypeBuilder<KeoBiaPlayer> b)
    {
        b.ToTable("KeoBiaPlayers");
        b.HasKey(x => x.Id);
        b.Property(x => x.PublicKey).IsRequired().HasMaxLength(80);
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(80);
        b.Property(x => x.AvatarUrl).HasColumnType("nvarchar(max)");
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(600);
        b.Property(x => x.TelegramUsername).HasMaxLength(64);
        b.Property(x => x.TelegramFirstName).HasMaxLength(128);
        b.Property(x => x.TelegramPhotoUrl).HasColumnType("nvarchar(max)");
        b.Property(x => x.PenaltyCups).HasDefaultValue(0);
        b.HasIndex(x => new { x.SiteId, x.PublicKey }).IsUnique();
        b.HasIndex(x => new { x.SiteId, x.TelegramUserId })
            .IsUnique()
            .HasFilter("[TelegramUserId] IS NOT NULL");
        b.HasMany(x => x.Bets).WithOne(x => x.Player)
            .HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.ChatMessages).WithOne(x => x.Player)
            .HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class KeoBiaMatchConfiguration : IEntityTypeConfiguration<KeoBiaMatch>
{
    public void Configure(EntityTypeBuilder<KeoBiaMatch> b)
    {
        b.ToTable("KeoBiaMatches");
        b.HasKey(x => x.Id);
        b.Property(x => x.ExternalId).IsRequired().HasMaxLength(120);
        b.Property(x => x.Stage).IsRequired().HasMaxLength(120);
        b.Property(x => x.HomeName).IsRequired().HasMaxLength(160);
        b.Property(x => x.HomeCode).IsRequired().HasMaxLength(12);
        b.Property(x => x.HomePrimary).IsRequired().HasMaxLength(24);
        b.Property(x => x.HomeSecondary).IsRequired().HasMaxLength(24);
        b.Property(x => x.AwayName).IsRequired().HasMaxLength(160);
        b.Property(x => x.AwayCode).IsRequired().HasMaxLength(12);
        b.Property(x => x.AwayPrimary).IsRequired().HasMaxLength(24);
        b.Property(x => x.AwaySecondary).IsRequired().HasMaxLength(24);
        b.Property(x => x.Venue).IsRequired().HasMaxLength(220);
        b.Property(x => x.HotLabel).HasMaxLength(80);
        b.Property(x => x.Status).IsRequired().HasMaxLength(24);
        b.Property(x => x.ResultChoice).HasMaxLength(12);
        b.Property(x => x.AiSummary).HasMaxLength(1200);
        b.Property(x => x.AiAnalysisContent).HasColumnType("nvarchar(max)");
        b.Property(x => x.AiAnalysisProbabilityJson).HasMaxLength(2000);
        b.Property(x => x.AiAnalysisSource).HasMaxLength(200);
        b.Property(x => x.CorrectScoreOddsJson).HasColumnType("nvarchar(max)");
        b.HasIndex(x => new { x.SiteId, x.ExternalId }).IsUnique();
        b.HasMany(x => x.Bets).WithOne(x => x.Match)
            .HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class KeoBiaBetConfiguration : IEntityTypeConfiguration<KeoBiaBet>
{
    public void Configure(EntityTypeBuilder<KeoBiaBet> b)
    {
        b.ToTable("KeoBiaBets");
        b.HasKey(x => x.Id);
        b.Property(x => x.Choice).IsRequired().HasMaxLength(12);
        b.Property(x => x.CorrectScoreOdds).HasColumnType("decimal(6,2)");
        b.HasIndex(x => new { x.SiteId, x.PlayerId, x.MatchId }).IsUnique();
        b.HasIndex(x => new { x.SiteId, x.MatchId, x.CreatedAt });
        b.HasIndex(x => new { x.SiteId, x.PlayerId, x.CreatedAt });
    }
}

public class KeoBiaActivityConfiguration : IEntityTypeConfiguration<KeoBiaActivity>
{
    public void Configure(EntityTypeBuilder<KeoBiaActivity> b)
    {
        b.ToTable("KeoBiaActivities");
        b.HasKey(x => x.Id);
        b.Property(x => x.ActivityType).IsRequired().HasMaxLength(24);
        b.Property(x => x.PlayerName).IsRequired().HasMaxLength(80);
        b.Property(x => x.AvatarUrl).HasColumnType("nvarchar(max)");
        b.Property(x => x.Text).IsRequired().HasMaxLength(400);
        b.Property(x => x.Badge).IsRequired().HasMaxLength(40);
        b.Property(x => x.Choice).HasMaxLength(12);
        b.Property(x => x.ChoiceLabel).HasMaxLength(80);
        b.HasIndex(x => new { x.SiteId, x.CreatedAt });
        b.HasIndex(x => new { x.SiteId, x.ActivityType, x.CreatedAt });
        b.HasIndex(x => new { x.SiteId, x.PlayerId, x.CreatedAt });
        b.HasIndex(x => new { x.SiteId, x.MatchId, x.CreatedAt });
    }
}

public class KeoBiaChatMessageConfiguration : IEntityTypeConfiguration<KeoBiaChatMessage>
{
    public void Configure(EntityTypeBuilder<KeoBiaChatMessage> b)
    {
        b.ToTable("KeoBiaChatMessages");
        b.HasKey(x => x.Id);
        b.Property(x => x.PlayerName).IsRequired().HasMaxLength(80);
        b.Property(x => x.AvatarUrl).HasColumnType("nvarchar(max)");
        b.Property(x => x.Message).HasMaxLength(600);
        b.Property(x => x.ImageUrl).HasColumnType("nvarchar(max)");
        b.HasIndex(x => new { x.SiteId, x.CreatedAt });
        b.HasIndex(x => new { x.SiteId, x.PlayerId, x.CreatedAt });
    }
}

public class KeoBiaImportJobConfiguration : IEntityTypeConfiguration<KeoBiaImportJob>
{
    public void Configure(EntityTypeBuilder<KeoBiaImportJob> b)
    {
        b.ToTable("KeoBiaImportJobs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Source).IsRequired().HasMaxLength(160);
        b.Property(x => x.ErrorLog).HasColumnType("nvarchar(max)");
    }
}

public class KeoBiaChangelogConfiguration : IEntityTypeConfiguration<KeoBiaChangelog>
{
    public void Configure(EntityTypeBuilder<KeoBiaChangelog> b)
    {
        b.ToTable("KeoBiaChangelogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Version).IsRequired().HasMaxLength(20);
        b.Property(x => x.Title).IsRequired().HasMaxLength(200);
        b.Property(x => x.Content).IsRequired().HasMaxLength(2000);
        b.HasIndex(x => new { x.SiteId, x.CreatedAt });
    }
}

public class KeoBiaCupLogConfiguration : IEntityTypeConfiguration<KeoBiaCupLog>
{
    public void Configure(EntityTypeBuilder<KeoBiaCupLog> b)
    {
        b.ToTable("KeoBiaCupLogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.ChangeType).IsRequired().HasMaxLength(40);
        b.Property(x => x.Reason).IsRequired().HasMaxLength(400);
        b.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.PlayerId, x.CreatedAt });
        b.HasIndex(x => new { x.SiteId, x.CreatedAt });
    }
}

public class KeoBiaQuestionConfiguration : IEntityTypeConfiguration<KeoBiaQuestion>
{
    public void Configure(EntityTypeBuilder<KeoBiaQuestion> b)
    {
        b.ToTable("KeoBiaQuestions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Text).IsRequired().HasMaxLength(400);
        b.Property(x => x.ChoicesJson).IsRequired().HasColumnType("nvarchar(max)");
        b.Property(x => x.PenaltyCups).HasDefaultValue(0);
        b.Property(x => x.CorrectChoiceKey).HasMaxLength(16);
        b.Property(x => x.Status).IsRequired().HasMaxLength(24);
        b.Property(x => x.TelegramChatId).HasMaxLength(64);
        b.HasMany(x => x.Votes).WithOne(x => x.Question)
            .HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.SiteId, x.Status, x.CreatedAt });
    }
}

public class KeoBiaQuestionVoteConfiguration : IEntityTypeConfiguration<KeoBiaQuestionVote>
{
    public void Configure(EntityTypeBuilder<KeoBiaQuestionVote> b)
    {
        b.ToTable("KeoBiaQuestionVotes");
        b.HasKey(x => x.Id);
        b.Property(x => x.ChoiceKey).IsRequired().HasMaxLength(16);
        b.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SiteId, x.QuestionId, x.PlayerId }).IsUnique();
        b.HasIndex(x => new { x.SiteId, x.QuestionId });
    }
}

public class KeoBiaBeerPaymentConfiguration : IEntityTypeConfiguration<KeoBiaBeerPayment>
{
    public void Configure(EntityTypeBuilder<KeoBiaBeerPayment> b)
    {
        b.ToTable("KeoBiaBeerPayments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(40);
        b.Property(x => x.Status).IsRequired().HasMaxLength(20);
        b.Property(x => x.QrUrl).IsRequired().HasColumnType("nvarchar(max)");
        b.Property(x => x.TransferContent).IsRequired().HasMaxLength(120);
        b.Property(x => x.ProviderTransactionId).HasMaxLength(120);
        b.Property(x => x.ProviderPayloadJson).HasColumnType("nvarchar(max)");
        b.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SiteId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.SiteId, x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.SiteId, x.TransferContent });
    }
}
