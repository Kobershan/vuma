using Microsoft.Extensions.Logging;
using VumaRetail.Domain.Primitives;
using VumaRetail.Hardware.Receipts;

namespace VumaRetail.Hardware.Devices;

/// <summary>The cash drawer under the till.</summary>
/// <remarks>
/// Its own interface even though every real implementation kicks it through the printer, because "open
/// the drawer" is a thing the application wants and "send a pulse to pin 2 of the printer's DK jack"
/// is an implementation detail of one way to do it. A self-checkout with a coin recycler opens nothing
/// and satisfies the same interface.
/// </remarks>
public interface ICashDrawer
{
    /// <summary>Opens the drawer.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task OpenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens the drawer by sending the ESC/POS kick pulse to the printer it is wired to.
/// </summary>
/// <remarks>
/// The drawer plugs into a modular jack on the back of the receipt printer, not into the PC. That is
/// why this takes an <see cref="IReceiptPrinter"/>: opening the drawer is a print job that happens to
/// contain no text.
/// </remarks>
/// <param name="printer">The printer the drawer is wired to.</param>
public sealed class PrinterKickCashDrawer(IReceiptPrinter printer) : ICashDrawer
{
    /// <inheritdoc />
    public Task OpenAsync(CancellationToken cancellationToken = default)
        => printer.PrintAsync(EscPos.KickDrawer.ToArray(), cancellationToken);
}

/// <summary>A terminal with no drawer — a self-checkout, a card-only lane, a back-office workstation.</summary>
/// <param name="logger">Where the no-op is recorded, so a miswired till is visible in the logs.</param>
public sealed class NullCashDrawer(ILogger<NullCashDrawer> logger) : ICashDrawer
{
    /// <inheritdoc />
    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Cash drawer open requested on a terminal with no drawer configured.");

        return Task.CompletedTask;
    }
}

/// <summary>What a card payment attempt came back with.</summary>
/// <param name="Approved">Whether the acquirer approved it.</param>
/// <param name="AuthorisationCode">The code to print on the slip and store on the tender, when approved.</param>
/// <param name="DeclineReason">Why it was declined, when it was.</param>
/// <param name="MaskedPan">The masked card number, when the terminal reported one. Never the full number.</param>
public sealed record PaymentAuthorisation(
    bool Approved,
    string? AuthorisationCode,
    string? DeclineReason,
    string? MaskedPan);

/// <summary>The card machine on the counter.</summary>
/// <remarks>
/// <b>Deferred integration</b> in the sense <c>docs/PROGRESS.md</c> §3 means: there is no acquirer
/// contract and no terminal on this machine, so the only implementation here is a simulator. The real
/// one is an acquirer's SDK behind this same interface. What matters is that the interface is narrow
/// enough that it can be — an authorisation attempt in, an approval or a decline out, and no card data
/// crossing it. A full PAN must never reach Vuma's storage, which is why this contract has nowhere to
/// put one.
/// </remarks>
public interface IPaymentTerminal
{
    /// <summary>Asks the terminal to take a card payment.</summary>
    /// <param name="amount">How much to charge.</param>
    /// <param name="reference">The sale's receipt number, for the terminal's own record.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PaymentAuthorisation> AuthoriseAsync(
        Money amount, string reference, CancellationToken cancellationToken = default);
}

/// <summary>
/// Approves everything and issues a deterministic authorisation code.
/// </summary>
/// <remarks>
/// The tested default until an acquirer exists — the same position <c>InProcessControlPlane</c> and
/// <c>FileSystemBackupVault</c> hold for Stage 04b and Stage 04. Deterministic rather than random so a
/// test can assert on the code it produces.
/// </remarks>
public sealed class SimulatedPaymentTerminal : IPaymentTerminal
{
    /// <inheritdoc />
    public Task<PaymentAuthorisation> AuthoriseAsync(
        Money amount, string reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        // Six characters, derived from the reference so the same sale always simulates the same
        // authorisation. A random code would make every test that prints a receipt non-deterministic.
        string code = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(reference)))[..6];

        return Task.FromResult(new PaymentAuthorisation(
            Approved: true,
            AuthorisationCode: code,
            DeclineReason: null,
            MaskedPan: "**** **** **** 0000"));
    }
}

/// <summary>The weighing scale at a deli or produce counter.</summary>
/// <remarks>
/// Reads a weight; it does not price it. A scale that knew prices would be a second place a price
/// lives, and pricing is Stage 10's (ADR-072). The weight comes back as a <see cref="Quantity"/> in
/// the unit the scale is calibrated in, which the caller must reconcile with the item's own unit.
/// </remarks>
public interface IWeighingScale
{
    /// <summary>Reads the current stable weight.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ScaleNotStableException">The reading has not settled.</exception>
    Task<Quantity> ReadStableWeightAsync(CancellationToken cancellationToken = default);
}

/// <summary>The scale was read before its reading settled.</summary>
public sealed class ScaleNotStableException()
    : Exception(
        "The scale's reading has not settled. A weight taken mid-swing is not a weight anybody may be "
        + "charged for — wait for the stable indicator and read again.");

/// <summary>Returns a fixed weight. The tested default until a real scale is attached.</summary>
/// <param name="weight">What to report.</param>
public sealed class SimulatedWeighingScale(Quantity weight) : IWeighingScale
{
    /// <inheritdoc />
    public Task<Quantity> ReadStableWeightAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(weight);
}
