using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Backup;
using VumaRetail.Application.Abstractions.Registry;

namespace VumaRetail.Infrastructure.Backup;

/// <summary>Routes company snapshots through the registry-selected database.</summary>
public sealed class CompanyAwareBackupEngine(
    string hostConnectionString,
    ICompanyContext company,
    ITenantContext tenant,
    ICompanyConnectionResolver resolver,
    ICompanyConnectionSecretStore secrets,
    PostgresBackupOptions options,
    ILoggerFactory loggerFactory) : IBackupEngine
{
    public async Task<long> DumpAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (company.CompanyId is not { } companyId)
        {
            return await Engine(hostConnectionString).DumpAsync(destination, cancellationToken)
                .ConfigureAwait(false);
        }

        CompanyConnection routing = await resolver.ResolveAsync(
            tenant.TenantId, companyId, CompanyAccessMode.Read, cancellationToken).ConfigureAwait(false);
        string connection = await secrets.ResolveAsync(routing.SecretReference, cancellationToken)
            .ConfigureAwait(false);
        return await Engine(connection).DumpAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    public Task RestoreAsync(Stream source, string targetConnectionString, CancellationToken cancellationToken = default)
        => Engine(hostConnectionString).RestoreAsync(source, targetConnectionString, cancellationToken);

    private PostgresBackupEngine Engine(string connection)
        => new(connection, options, loggerFactory.CreateLogger<PostgresBackupEngine>());
}
