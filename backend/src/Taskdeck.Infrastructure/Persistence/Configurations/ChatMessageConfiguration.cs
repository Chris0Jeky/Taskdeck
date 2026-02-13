using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");

        builder.HasKey(cm => cm.Id);

        builder.Property(cm => cm.Id)
            .ValueGeneratedNever();

        builder.Property(cm => cm.SessionId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cm => cm.Role)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(cm => cm.Content)
            .IsRequired();

        builder.Property(cm => cm.MessageType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(cm => cm.ProposalId)
            .HasMaxLength(100);

        builder.Property(cm => cm.TokenUsage);

        builder.Property(cm => cm.CreatedAt)
            .IsRequired();

        builder.Property(cm => cm.UpdatedAt)
            .IsRequired();

        builder.HasIndex(cm => cm.SessionId);
        builder.HasIndex(cm => cm.ProposalId);
        builder.HasIndex(cm => cm.CreatedAt);
    }
}
