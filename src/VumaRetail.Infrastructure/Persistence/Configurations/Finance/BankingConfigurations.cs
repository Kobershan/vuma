using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Finance;

/// <summary><c>finance.bank_accounts</c>.</summary>
internal sealed class BankAccountConfiguration : EntityConfiguration<BankAccount>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "bank_accounts";

    protected override void ConfigureEntity(EntityTypeBuilder<BankAccount> builder)
    {
        builder.Property(account => account.GlAccountId).IsRequired();
        builder.Property(account => account.Name).IsRequired().HasMaxLength(128);
        builder.Property(account => account.AccountNumber).IsRequired().HasMaxLength(64);
        builder.Property(account => account.Currency).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(account => account.IsActive).IsRequired();

        // One bank account per GL control account — BankAccount's own remarks.
        builder.HasIndex(account => account.GlAccountId)
            .IsUnique()
            .HasDatabaseName("ux_bank_accounts_gl_account_id")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>finance.bank_statement_lines</c> — a matched line is the reconciliation.</summary>
internal sealed class BankStatementLineConfiguration : EntityConfiguration<BankStatementLine>
{
    protected override string Schema => Schemas.Finance;

    protected override string TableName => "bank_statement_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<BankStatementLine> builder)
    {
        builder.Property(line => line.BankAccountId).IsRequired();
        builder.Property(line => line.TransactionDate).IsRequired();
        builder.Property(line => line.Description).IsRequired().HasMaxLength(256);
        builder.Property(line => line.ExternalReference).IsRequired().HasMaxLength(128);
        builder.Property(line => line.MatchedJournalLineId);
        builder.Property(line => line.MatchedAt);
        builder.Property(line => line.MatchedBy).HasMaxLength(128);

        builder.HasMoney(line => line.Amount, "amount");

        // The batch import's own de-duplication check (ImportBankStatementLinesCommandHandler).
        builder.HasIndex(line => new { line.BankAccountId, line.ExternalReference })
            .IsUnique()
            .HasDatabaseName("ux_bank_statement_lines_bank_account_id_external_reference")
            .HasFilter("deleted_at IS NULL");

        // The reconciled-balance query sums matched lines for one account.
        builder.HasIndex(line => new { line.BankAccountId, line.MatchedJournalLineId })
            .HasDatabaseName("ix_bank_statement_lines_bank_account_id_matched");
    }
}
