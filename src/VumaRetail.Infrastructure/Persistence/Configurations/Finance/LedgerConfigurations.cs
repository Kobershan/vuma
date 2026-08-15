using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Finance;

/// <summary><c>finance.journals</c> — the immutable general ledger header (ADR-016, CLAUDE.md §7 rule 7).</summary>
internal sealed class JournalConfiguration : EntityConfiguration<Journal>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "journals";

    protected override void ConfigureEntity(EntityTypeBuilder<Journal> builder)
    {
        builder.Property(journal => journal.AccountingPeriodId).IsRequired();
        builder.Property(journal => journal.JournalNumber).IsRequired().HasMaxLength(32);
        builder.Property(journal => journal.PostedAt).IsRequired();
        builder.Property(journal => journal.PostedBy).IsRequired().HasMaxLength(128);
        builder.Property(journal => journal.SourceModule).IsRequired().HasMaxLength(32);
        builder.Property(journal => journal.SourceEventType).IsRequired().HasMaxLength(64);
        builder.Property(journal => journal.SourceReference).IsRequired().HasMaxLength(128);
        builder.Property(journal => journal.Narration).IsRequired().HasMaxLength(512);
        builder.Property(journal => journal.ReversalOfJournalId);

        builder.HasIndex(journal => new { journal.TenantId, journal.JournalNumber })
            .IsUnique()
            .HasDatabaseName("ux_journals_tenant_id_journal_number");

        builder.HasIndex(journal => journal.AccountingPeriodId)
            .HasDatabaseName("ix_journals_accounting_period_id");

        builder.HasIndex(journal => journal.ReversalOfJournalId)
            .HasDatabaseName("ix_journals_reversal_of_journal_id")
            .HasFilter("reversal_of_journal_id IS NOT NULL");

        // Backing-field navigation (ADR-066): Journal.Lines is populated once at Post() and never
        // added to afterwards, so this is the parent side of a plain one-to-many, not an owned
        // collection — the line has its own identity, audit columns and sync stamp.
        builder.HasMany(journal => journal.Lines)
            .WithOne()
            .HasForeignKey(line => line.JournalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(journal => journal.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>finance.journal_lines</c>.</summary>
internal sealed class JournalLineConfiguration : EntityConfiguration<JournalLine>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "journal_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<JournalLine> builder)
    {
        builder.Property(line => line.JournalId).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.AccountId).IsRequired();
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);

        builder.HasMoney(line => line.Debit, "debit");
        builder.HasMoney(line => line.Credit, "credit");

        builder.Property(line => line.DepartmentId);
        builder.Property(line => line.CostCentreId);
        builder.Property(line => line.ProjectId);
        builder.Property(line => line.ChannelId);
        builder.Property(line => line.EmployeeId);

        builder.HasIndex(line => line.JournalId)
            .HasDatabaseName("ix_journal_lines_journal_id");

        // The trial balance and account-balance queries both group by account across every journal.
        builder.HasIndex(line => line.AccountId)
            .HasDatabaseName("ix_journal_lines_account_id");

        // A journal line carries a debit or a credit, never both and never neither (JournalLine.Create).
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_journal_lines_exactly_one_side",
            "((debit_amount IS NOT NULL)::int + (credit_amount IS NOT NULL)::int) = 1"));
    }
}
