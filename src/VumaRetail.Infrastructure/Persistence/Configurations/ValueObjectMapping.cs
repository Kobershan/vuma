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

    // There is deliberately no HasMoney overload for an optional Money? (ADR-067). EF Core 9 cannot
    // configure a complex property as optional, and reaching a nullable value type's fields needs the
    // two-hop expression `value!.Value.Amount`, which ComplexPropertyBuilder.Property refuses to
    // parse — it throws at model-building time, not at compile time, which is how the first attempt
    // shipped a solution that built cleanly and could not construct its own DbContext. An entity with
    // an optional monetary amount stores the {name}_amount / {name}_currency pair as plain properties
    // and exposes a computed Money? accessor; VumaRetail.Domain.Finance.JournalLine is the worked
    // example. HasAddress below solves the same EF limitation the other way, with an owned type,
    // because an address has no arithmetic to preserve.

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

    /// <summary>
    /// Maps an <see cref="Address"/> property to <c>{prefix}_line1</c>, <c>{prefix}_line2</c>,
    /// <c>{prefix}_city</c>, <c>{prefix}_region</c>, <c>{prefix}_postal_code</c> and
    /// <c>{prefix}_country_code</c> (Stage 06, ADR-037).
    /// </summary>
    /// <typeparam name="TEntity">The owning entity.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="property">The address property, for example <c>store => store.Address</c>.</param>
    /// <param name="columnPrefix">The column name prefix, <c>snake_case</c>.</param>
    /// <param name="isRequired">
    /// Whether every field is required. <c>false</c> leaves every column nullable, which is how an
    /// entity with an optional address (a store that has not recorded one yet) represents "none" —
    /// every <c>{prefix}_*</c> column is null on that row.
    /// </param>
    public static void HasAddress<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, Address?>> property,
        string columnPrefix,
        bool isRequired = true)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnPrefix);

        // An owned type (table-split onto the same table) rather than a complex property. EF Core 9
        // refuses to configure a complex property as optional ("call IsRequired()" —
        // https://github.com/dotnet/efcore/issues/31376), and an optional address is exactly what
        // Stage 06 needs: a store or a partner with no address recorded yet is a row with every
        // {prefix}_* column null, not an empty address. OwnsOne with no separate ToTable call maps to
        // the owner's own table by default, so the column shape is identical to what ComplexProperty
        // would have produced.
        builder.OwnsOne(property, address =>
        {
            address.Property(value => value.Line1)
                .HasColumnName($"{columnPrefix}_line1")
                .HasMaxLength(256)
                .IsRequired(isRequired);

            address.Property(value => value.Line2)
                .HasColumnName($"{columnPrefix}_line2")
                .HasMaxLength(256);

            address.Property(value => value.City)
                .HasColumnName($"{columnPrefix}_city")
                .HasMaxLength(128)
                .IsRequired(isRequired);

            address.Property(value => value.Region)
                .HasColumnName($"{columnPrefix}_region")
                .HasMaxLength(128);

            address.Property(value => value.PostalCode)
                .HasColumnName($"{columnPrefix}_postal_code")
                .HasMaxLength(16);

            address.Property(value => value.CountryCode)
                .HasColumnName($"{columnPrefix}_country_code")
                .HasMaxLength(2)
                .IsFixedLength()
                .IsRequired(isRequired);
        });

        builder.Navigation(property).IsRequired(isRequired);
    }
}
