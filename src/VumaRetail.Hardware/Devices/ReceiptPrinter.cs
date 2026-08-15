using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace VumaRetail.Hardware.Devices;

/// <summary>Somewhere a rendered receipt can be sent.</summary>
/// <remarks>
/// Takes bytes, not a <c>ReceiptDocument</c>. Rendering and transport are separate on purpose: the
/// layout is worth testing without a device, and the transport is worth swapping without touching the
/// layout. <c>ReceiptRenderer</c> produces the bytes.
/// </remarks>
public interface IReceiptPrinter
{
    /// <summary>A name for logs and for the device list a manager sees.</summary>
    string Name { get; }

    /// <summary>Sends a rendered receipt to the device.</summary>
    /// <param name="payload">The rendered bytes — ESC/POS for a thermal printer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ReceiptPrinterException">The device could not be reached or refused the job.</exception>
    Task PrintAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}

/// <summary>A receipt could not be printed.</summary>
/// <param name="printer">Which device.</param>
/// <param name="message">What went wrong.</param>
/// <param name="innerException">The underlying failure, where there is one.</param>
public sealed class ReceiptPrinterException(string printer, string message, Exception? innerException = null)
    : Exception($"{printer}: {message}", innerException)
{
    /// <summary>The device that failed.</summary>
    public string Printer { get; } = printer;
}

/// <summary>
/// A thermal printer attached to the store LAN, driven over a raw socket on port 9100.
/// </summary>
/// <remarks>
/// <para>
/// This is how most till printers in a shop are actually attached — an Ethernet port on the back, a
/// fixed IP, and JetDirect-style raw printing on 9100. There is no protocol above the socket: the
/// bytes go down the wire and the printer executes them, which is why <c>ReceiptRenderer</c> has to
/// produce something the device understands rather than a document format.
/// </para>
/// <para>
/// Cross-platform, and therefore testable and runnable anywhere, unlike the serial and USB transports
/// that are Stage 31's Windows installer work (<c>docs/HARDWARE.md</c>).
/// </para>
/// <para>
/// A short connect timeout is deliberate. A till that hangs for the operating system's default connect
/// timeout because somebody unplugged the printer is a till that has stopped serving customers, which
/// R1 does not allow. Failing quickly lets the caller record the sale and reprint later.
/// </para>
/// </remarks>
/// <param name="host">The printer's host name or IP address.</param>
/// <param name="logger">Where a failure is recorded.</param>
/// <param name="port">The raw printing port. 9100 unless the device was configured otherwise.</param>
/// <param name="connectTimeout">How long to wait for the socket. Defaults to three seconds.</param>
public sealed class NetworkReceiptPrinter(
    string host,
    ILogger<NetworkReceiptPrinter> logger,
    int port = 9100,
    TimeSpan? connectTimeout = null) : IReceiptPrinter
{
    private readonly TimeSpan _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public string Name { get; } = $"{host}:{port}";

    /// <inheritdoc />
    public async Task PrintAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        using TcpClient client = new();

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_connectTimeout);

            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);

            await using NetworkStream stream = client.GetStream();
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Receipt printer {Printer} did not answer within {Timeout}.", Name, _connectTimeout);

            throw new ReceiptPrinterException(Name, $"The printer did not answer within {_connectTimeout}.");
        }
        catch (SocketException failure)
        {
            logger.LogWarning(failure, "Receipt printer {Printer} could not be reached.", Name);

            throw new ReceiptPrinterException(Name, "The printer could not be reached.", failure);
        }
    }
}

/// <summary>
/// Writes each receipt to a file instead of a device.
/// </summary>
/// <remarks>
/// The development and demonstration printer, and the one a store with no printer configured falls
/// back to so that a sale still completes and the slip can be found afterwards. Not a null object: the
/// receipt genuinely exists, which is what makes it useful for a seeded demo and for reading what a
/// real printer would have produced.
/// </remarks>
/// <param name="directory">Where the files go. Created if it does not exist.</param>
/// <param name="logger">Where the path is recorded.</param>
public sealed class TextFileReceiptPrinter(string directory, ILogger<TextFileReceiptPrinter> logger) : IReceiptPrinter
{
    /// <inheritdoc />
    public string Name { get; } = $"file:{directory}";

    /// <inheritdoc />
    public async Task PrintAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, $"receipt-{Guid.NewGuid():N}.escpos");

        await File.WriteAllBytesAsync(path, payload, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Receipt written to {Path}.", path);
    }
}
