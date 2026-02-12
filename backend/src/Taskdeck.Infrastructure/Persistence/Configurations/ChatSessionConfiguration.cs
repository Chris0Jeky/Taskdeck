using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.ToTable("ChatSessions");

        builder.HasKey(cs => cs.Id);

        builder.Property(cs => cs.Id)
            .ValueGeneratedNever();

        builder.Property(cs => cs.UserId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cs => cs.BoardId)
            .HasMaxLength(100);

        builder.Property(cs => cs.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(cs => cs.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(cs => cs.CreatedAt)
            .IsRequired();

        builder.Property(cs => cs.UpdatedAt)
            .IsRequired();

        builder.HasMany(cs => cs.Messages)
            .WithOne(m => m.Session)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(cs => cs.UserId);
        builder.HasIndex(cs => cs.BoardId);
        builder.HasIndex(cs => cs.Status);
        builder.HasIndex(cs => cs.CreatedAt);
    }
}
