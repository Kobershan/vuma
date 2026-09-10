using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Conversations;
using VumaRetail.Infrastructure.Persistence.Configurations;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Conversations;

internal sealed class ConversationConfiguration : EntityConfiguration<Conversation>
{
    protected override string Schema => Schemas.Conversations;
    protected override string TableName => "conversations";

    protected override void ConfigureEntity(EntityTypeBuilder<Conversation> builder)
    {
        builder.Property(x => x.ContactBindingId).IsRequired();
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.CurrentIntent).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.CompanyIdInPlay);
        builder.Property(x => x.LastActivityAt).IsRequired();
        builder.Property(x => x.EscalatedAt);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.ContactBindingId, x.LastActivityAt });
    }
}

internal sealed class ConversationTurnConfiguration : EntityConfiguration<ConversationTurn>
{
    protected override string Schema => Schemas.Conversations;
    protected override string TableName => "conversation_turns";

    protected override void ConfigureEntity(EntityTypeBuilder<ConversationTurn> builder)
    {
        builder.Property(x => x.ConversationId).IsRequired();
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Text).IsRequired().HasMaxLength(10000);
        builder.Property(x => x.ClassifiedIntent).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ExtractedEntities).HasMaxLength(4000);
        builder.Property(x => x.PhrasedFromResultId);
        builder.Property(x => x.HappenedAt).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ConversationId, x.HappenedAt });
    }
}

internal sealed class DocumentDeliveryTokenConfiguration : EntityConfiguration<DocumentDeliveryToken>
{
    protected override string Schema => Schemas.Conversations;
    protected override string TableName => "document_delivery_tokens";

    protected override void ConfigureEntity(EntityTypeBuilder<DocumentDeliveryToken> builder)
    {
        builder.Property(x => x.BindingId).IsRequired();
        builder.Property(x => x.DocumentReference).IsRequired().HasMaxLength(512);
        builder.Property(x => x.Token).IsRequired().HasMaxLength(64);
        builder.Property(x => x.IssuedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.Property(x => x.RevokedAt);
        builder.Property(x => x.FetchedAt);
        builder.HasIndex(x => new { x.TenantId, x.Token }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ExpiresAt });
    }
}
