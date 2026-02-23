using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class CardCommentMentionConfiguration : IEntityTypeConfiguration<CardCommentMention>
{
    public void Configure(EntityTypeBuilder<CardCommentMention> builder)
    {
        builder.ToTable("CardCommentMentions");

        builder.HasKey(mention => mention.Id);

        builder.Property(mention => mention.Id)
            .ValueGeneratedNever();

        builder.Property(mention => mention.CardCommentId)
            .IsRequired();

        builder.Property(mention => mention.MentionedUserId)
            .IsRequired();

        builder.Property(mention => mention.MentionedUsername)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(mention => mention.CreatedAt)
            .IsRequired();

        builder.Property(mention => mention.UpdatedAt)
            .IsRequired();

        builder.HasOne(mention => mention.CardComment)
            .WithMany(comment => comment.Mentions)
            .HasForeignKey(mention => mention.CardCommentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(mention => mention.MentionedUser)
            .WithMany()
            .HasForeignKey(mention => mention.MentionedUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(mention => mention.CardCommentId);
        builder.HasIndex(mention => mention.MentionedUserId);
        builder.HasIndex(mention => new { mention.CardCommentId, mention.MentionedUserId }).IsUnique();
    }
}
