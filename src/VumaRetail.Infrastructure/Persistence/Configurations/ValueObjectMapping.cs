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

    // There is deliberately no nullable overload of HasMoney above.
    //
    // A nullable Money property (an approval policy's optional threshold, Stage 05) was first mapped
    // as an optional ComplexProperty here. EF Core 9's relational providers do not support that —
    // every shape tried (nested properties marked optional, the complex property alone marked
    // optional) fails at model-build time with "Configuring the complex property … as optional is not
    // supported, call 'IsRequired()'" (dotnet/efcore#31376).
    //
    // A module with a nullable money property maps it as two ordinary nullable properties
    // ({Name}Value: decimal?, {Name}Currency: string?) on the entity — private, if the public API
    // should stay a single Money? computed from the pair — configured with two plain
    // builder.Property<T>("{Name}Value") calls rather than this helper. See
    // ApprovalPolicy.ThresholdAmount / ApprovalRequest.Amount for the pattern, and revisit once the
    // upstream issue ships.

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
