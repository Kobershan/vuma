using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Money"/> and <see cref="Quantity"/> to their column pairs, the same way every time.
/// </summary>
/// <remarks>
/// <para>
/// <c>CLAUDE.md</c> §7 rules 4 and 5 give both of these a fixed shape: money is
/// <c>decimal(18,4)</c> with an explicit currency, quantity is <c>decimal(18,6)</c> with a unit of
/// measure. Neither is ever a bare decimal, and neither is ever stored without its companion column —
/// a decimal with no currency beside it is how a multi-currency system quietly adds rands to dollars.
/// </para>
/// <para>
/// Mapped as complex properties rather than owned entities: both are value types with no identity of
/// their own, and an owned entity would give each one a key and a change-tracking entry it has no use
/// for. Stage 07 is the first stage with money on a table; the helpers exist here so that when it
/// arrives the scale, the currency length and the naming are already decided.
/// </para>
/// </remarks>
public static class ValueObjectMapping
{
    /// <summary>The PostgreSQL type for a monetary amount (§7 rule 4).</summary>
    public const string MoneyColumnType = "numeric(18,4)";

    /// <summary>The PostgreSQL type for a quantity (§7 rule 5).</summary>
    public const string QuantityColumnType = "numeric(18,6)";

    /// <summary>
    /// Maps a <see cref="Money"/> property to <c>{name}_amount</c> and <c>{name}_currency</c>.
    /// </summary>
    /// <typeparam name="TEntity">The owning entity.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="property">The money property, for example <c>line => line.UnitPrice</c>.</param>
    /// <param name="columnPrefix">The column name prefix, <c>snake_case</c>.</param>
    /// <param name="isRequired">Whether the amount is required.</param>
    public static void HasMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, Money>> property,
        string columnPrefix,
        bool isRequired = true)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnPrefix);

        builder.ComplexProperty(property, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName($"{columnPrefix}_amount")
                .HasColumnType(MoneyColumnType)
                .IsRequired(isRequired);

            money.Property(value => value.Currency)
                .HasColumnName($"{columnPrefix}_currency")
                .HasMaxLength(3)
                .IsFixedLength()
                .IsRequired(isRequired);
        });
    }

    /// <summary>
    /// Maps a <see cref="Quantity"/> property to <c>{name}_value</c> and <c>{name}_uom</c>.
    /// </summary>
    /// <typeparam name="TEntity">The owning entity.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="property">The quantity property, for example <c>line => line.Ordered</c>.</param>
    /// <param name="columnPrefix">The column name prefix, <c>snake_case</c>.</param>
    /// <param name="isRequired">Whether the quantity is required.</param>
    public static void HasQuantity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, Quantity>> property,
        string columnPrefix,
        bool isRequired = true)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnPrefix);

        builder.ComplexProperty(property, quantity =>
        {
            quantity.Property(value => value.Value)
                .HasColumnName($"{columnPrefix}_value")
                .HasColumnType(QuantityColumnType)
                .IsRequired(isRequired);

            quantity.Property(value => value.UnitOfMeasure)
                .HasColumnName($"{columnPrefix}_uom")
                .HasMaxLength(16)
                .IsRequired(isRequired);
        });
    }
}
