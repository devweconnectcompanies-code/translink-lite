using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransLink.Lite.Domain.Entities;

namespace TransLink.Lite.Infrastructure.Persistence.Configurations;

public sealed class TranslationSessionConfiguration : IEntityTypeConfiguration<TranslationSession>
{
    public void Configure(EntityTypeBuilder<TranslationSession> builder)
    {
        builder.ToTable("TranslationSessions");

        builder.HasKey(session => session.Id);

        builder.Property(session => session.UserId)
            .IsRequired();

        builder.Property(session => session.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(session => session.SourceLanguage)
            .HasMaxLength(35)
            .IsRequired();

        builder.Property(session => session.TargetLanguage)
            .HasMaxLength(35)
            .IsRequired();

        builder.Property(session => session.Status)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(session => session.CreatedAt)
            .IsRequired();

        builder.HasIndex(session => session.UserId);

        builder.HasOne(session => session.User)
            .WithMany(user => user.TranslationSessions)
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
