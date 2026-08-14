using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using VumaRetail.Application.Abstractions.Backup;

namespace VumaRetail.Infrastructure.Backup;

/// <summary>Where the PostgreSQL client tools are and how long they get.</summary>
public sealed class PostgresBackupOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Vuma:Backup:Postgres";

    /// <summary>
    /// The directory holding <c>pg_dump</c> and <c>pg_restore</c>, or empty to use <c>PATH</c>.
    /// </summary>
    /// <remarks>
    /// Set by the Stage 31 installer, which knows where it put PostgreSQL. Empty is right for a
    /// developer machine and for CI, where the tools are on <c>PATH</c> already.
    /// </remarks>
    public string ToolDirectory { get; set; } = string.Empty;

    /// <summary>How long a dump or restore may run before it is killed.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(2);
}

/// <summary>
/// Takes and restores PostgreSQL snapshots with <c>pg_dump</c> and <c>pg_restore</c>.
/// </summary>
/// <remarks>
/// <para>
/// The custom format (<c>-Fc</c>), not plain SQL. It is compressed, it restores with parallelism,
/// and — the reason that matters here — <c>pg_restore --clean --if-exists</c> can drop and recreate
/// objects, which is what makes a restore over an existing database work rather than collide with
/// every table it finds.
/// </para>
/// <para>
/// The password goes in the child process's environment as <c>PGPASSWORD</c>, never on the command
/// line. A command line is world-readable in <c>/proc</c> and in every process listing on the box.
/// </para>
/// <para>
/// <c>stderr</c> is captured and put on the exception. The tools report a missing extension, a
/// version mismatch or a permission problem there and nowhere else, and a backup failure that says
/// only "exit code 1" is a support call that goes nowhere.
/// </para>
/// </remarks>
/// <param name="connectionString">The database to dump.</param>
/// <param name="options">Where the tools are and how long they get.</param>
/// <param name="logger">Where the run summaries go.</param>
public sealed class PostgresBackupEngine(
    string connectionString,
    PostgresBackupOptions options,
    ILogger<PostgresBackupEngine> logger) : IBackupEngine
{
    /// <inheritdoc />
    public async Task<long> DumpAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        NpgsqlConnectionStringBuilder source = new(connectionString);

        long written = await RunAsync(
            "pg_dump",
            [
                "--host", source.Host ?? "localhost",
                "--port", source.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--username", source.Username ?? string.Empty,
                "--dbname", source.Database ?? string.Empty,
                "--format", "custom",
                "--no-owner",
                "--no-privileges",
                "--no-password",
            ],
            source.Password,
            destination,
            input: null,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Dumped {Database} — {ByteCount} bytes", source.Database, written);

        return written;
    }

    /// <inheritdoc />
    public async Task RestoreAsync(
        Stream source,
        string targetConnectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetConnectionString);

        NpgsqlConnectionStringBuilder target = new(targetConnectionString);

        await RunAsync(
            "pg_restore",
            [
                "--host", target.Host ?? "localhost",
                "--port", target.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--username", target.Username ?? string.Empty,
                "--dbname", target.Database ?? string.Empty,
                // --clean --if-exists so a restore over a database that already has objects replaces
                // them instead of failing on the first collision. This is the "new box, run restore"
                // path in R4, and also the path a drill takes over a scratch database.
                "--clean",
                "--if-exists",
                "--no-owner",
                "--no-privileges",
                "--no-password",
                "--exit-on-error",
            ],
            target.Password,
            output: null,
            input: source,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Restored into {Database}", target.Database);
    }

    private async Task<long> RunAsync(
        string tool,
        string[] arguments,
        string? password,
        Stream? output,
        Stream? input,
        CancellationToken cancellationToken)
    {
        string executable = string.IsNullOrWhiteSpace(options.ToolDirectory)
            ? tool
            : Path.Combine(options.ToolDirectory, tool);

        ProcessStartInfo start = new()
        {
            FileName = executable,
            RedirectStandardOutput = output is not null,
            RedirectStandardInput = input is not null,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(password))
        {
            // Environment, not the command line: a command line is readable by every process on the
            // machine, and this one unlocks the database the snapshot is of.
            start.Environment["PGPASSWORD"] = password;
        }

        using Process process = StartOrThrow(start, tool);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        long bytes = 0;

        try
        {
            if (input is not null)
            {
                await input.CopyToAsync(process.StandardInput.BaseStream, timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            if (output is not null)
            {
                bytes = await CopyCountingAsync(process.StandardOutput.BaseStream, output, timeout.Token)
                    .ConfigureAwait(false);
            }

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);

            throw new BackupEngineException(
                $"{tool} did not finish within {options.Timeout}. The snapshot was abandoned.");
        }

        string errors = await stderr.ConfigureAwait(false);

        return process.ExitCode == 0
            ? bytes
            : throw new BackupEngineException(
                $"{tool} exited with code {process.ExitCode}.{Environment.NewLine}{errors.Trim()}");
    }

    private static Process StartOrThrow(ProcessStartInfo start, string tool)
    {
        try
        {
            return Process.Start(start)
                ?? throw new BackupEngineException($"{tool} could not be started.");
        }
        catch (System.ComponentModel.Win32Exception missing)
        {
            throw new BackupEngineException(
                $"{tool} is not installed or is not on PATH. Set "
                + $"{PostgresBackupOptions.SectionName}:ToolDirectory to the PostgreSQL bin directory.",
                missing);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone between the check and the kill. Nothing to do, and nothing worth saying.
        }
    }

    private static async Task<long> CopyCountingAsync(Stream from, Stream to, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
        }

        return total;
    }
}
