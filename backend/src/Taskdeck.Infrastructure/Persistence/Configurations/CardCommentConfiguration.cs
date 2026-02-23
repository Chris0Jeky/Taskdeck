using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public class CardCommentConfiguration : IEntityTypeConfiguration<CardComment>
{
    public void Configure(EntityTypeBuilder<CardComment> builder)
    {
        builder.ToTable("CardComments");

        builder.HasKey(comment => comment.Id);

        builder.Property(comment => comment.Id)
            .ValueGeneratedNever();

        builder.Property(comment => comment.CardId)
            .IsRequired();

        builder.Property(comment => comment.BoardId)
            .IsRequired();

        builder.Property(comment => comment.AuthorUserId)
            .IsRequired();

        builder.Property(comment => comment.ParentCommentId);

        builder.Property(comment => comment.Content)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(comment => comment.IsDeleted)
            .IsRequired();

        builder.Property(comment => comment.DeletedAt);

        builder.Property(comment => comment.EditedAt);

        builder.Property(comment => comment.CreatedAt)
            .IsRequired();

        builder.Property(comment => comment.UpdatedAt)
            .IsRequired();

        builder.HasOne(comment => comment.Card)
            .WithMany()
            .HasForeignKey(comment => comment.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(comment => comment.AuthorUser)
            .WithMany()
            .HasForeignKey(comment => comment.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(comment => comment.ParentComment)
            .WithMany(comment => comment.Replies)
            .HasForeignKey(comment => comment.ParentCommentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(comment => comment.Mentions)
            .WithOne(mention => mention.CardComment)
            .HasForeignKey(mention => mention.CardCommentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(comment => comment.CardId);
        builder.HasIndex(comment => comment.BoardId);
        builder.HasIndex(comment => comment.AuthorUserId);
        builder.HasIndex(comment => comment.ParentCommentId);
        builder.HasIndex(comment => new { comment.CardId, comment.CreatedAt });
    }
}
