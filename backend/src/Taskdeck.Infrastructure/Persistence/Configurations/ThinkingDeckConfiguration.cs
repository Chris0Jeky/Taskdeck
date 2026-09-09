using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Infrastructure.Persistence.Configurations;

public sealed class ThinkingDeckConfiguration : IEntityTypeConfiguration<ThinkingDeck>
{
    public void Configure(EntityTypeBuilder<ThinkingDeck> builder)
    {
        builder.ToTable("ThinkingDecks");
        builder.HasKey(deck => deck.CardId);
        builder.Property(deck => deck.Revision).IsConcurrencyToken();
        builder.Property(deck => deck.LayersJson).IsRequired();
        builder.HasOne<Card>().WithOne().HasForeignKey<ThinkingDeck>(deck => deck.CardId).OnDelete(DeleteBehavior.Cascade);
    }
}
