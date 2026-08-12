using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Configurations;

namespace VumaRetail.IntegrationTests.Harness;

/// <summary>
/// An entity that exists only to be mapped, so the mapping rules can be checked against real
/// PostgreSQL before any module depends on them.
/// </summary>
/// <remarks>
/// <para>
/// <c>CLAUDE.md</c> §7 rules 4 and 5 fix the shape of money and quantity columns, and Stage 07 is the
/// first stage with either on a real table. Waiting until then to find out whether
/// <see cref="ValueObjectMapping"/> produces <c>numeric(18,4)</c> in an actual database would mean
/// discovering it inside the finance stage, where it is expensive to change.
/// </para>
/// <para>
/// It goes through the ordinary <see cref="EntityConfiguration{TEntity}"/> base, so this also proves
/// the mandatory columns, the filters and the concurrency token on a type the platform module did not
/// hand-tune. It is never in a migration and never reaches a real database.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class MappingProbe : Entity
{
    private MappingProbe(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    private MappingProbe()
    {
    }

    /// <summary>A monetary amount, to check §7 rule 4 reaches the database intact.</summary>
    public Money Price { get; private set; } = Money.Zero("ZAR");

    /// <summary>A quantity, to check §7 rule 5 reaches the database intact.</summary>
    public Quantity Counted { get; private set; } = Quantity.Zero("EA");

    /// <summary>A label, so a test can tell two probes apart.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>Creates a probe row.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="label">A label to identify the row.</param>
    /// <param name="price">The monetary amount to round-trip.</param>
    /// <param name="counted">The quantity to round-trip.</param>
    /// <param name="storeId">The owning store, if any.</param>
    public static MappingProbe Create(Guid tenantId, string label, Money price, Quantity counted, Guid? storeId = null)
        => new(tenantId, storeId) { Label = label, Price = price, Counted = counted };

    /// <summary>Changes the label, so a test has something ordinary to update.</summary>
    /// <param name="label">The new label.</param>
    public void Relabel(string label) => Label = label;

    /// <summary>Changes the price, so a test has a value-object column to update.</summary>
    /// <param name="price">The new price.</param>
    public void Reprice(Money price) => Price = price;
}

/// <summary>Maps <see cref="MappingProbe"/> through exactly the base configuration a module would use.</summary>
internal sealed class MappingProbeConfiguration : EntityConfiguration<MappingProbe>
{
    protected override string Schema => Schemas.Platform;

    protected override string TableName => "mapping_probes";

    protected override void ConfigureEntity(EntityTypeBuilder<MappingProbe> builder)
    {
        builder.Property(probe => probe.Label)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasMoney(probe => probe.Price, "price");
        builder.HasQuantity(probe => probe.Counted, "counted");
    }
}
