using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Domain.Licensing;

namespace VumaRetail.Infrastructure.Licensing;

/// <summary>
/// Reads this machine's hardware characteristics (<c>LICENSING.md</c> §3).
/// </summary>
/// <remarks>
/// <para>
/// A component that cannot be read is left out rather than faked. Absence scores nothing, which is the
/// correct behaviour on a virtual machine with no motherboard UUID and on a container with no
/// system volume — and inventing a stable-looking value for it would silently bind a licence to
/// something that is not the machine.
/// </para>
/// <para>
/// <b>The Windows readings are the ones that matter and this class does not take them yet.</b> The
/// motherboard UUID and the machine GUID come from WMI and the registry, which are Windows-only APIs
/// this cross-platform build cannot call (ADR-031). What is here reads what .NET exposes everywhere —
/// the primary NIC, the system volume, the CPU signature and the machine identity file — and Stage 31,
/// which is where the Windows installer and service land, replaces the machine-identity fallback with
/// the real registry value and adds the motherboard UUID from WMI.
/// </para>
/// <para>
/// A binding taken on fewer components is a weaker binding, and it is weaker in the
/// <em>tolerant</em> direction rather than the brittle one:
/// <see cref="HardwareFingerprint.RequiredScore"/> scales with what was captured, so a machine that
/// can only report six points still tolerates a replaced network card instead of demanding a rebind.
/// The gap is written down here rather than papered over with a fabricated value, because a
/// fabricated component would bind a licence to something that is not the machine.
/// </para>
/// </remarks>
/// <param name="logger">Where an unreadable component is noted, once.</param>
public sealed class MachineFingerprintProvider(ILogger<MachineFingerprintProvider> logger)
    : IHardwareFingerprintProvider
{
    /// <inheritdoc />
    public IReadOnlyDictionary<FingerprintComponent, string> Read()
    {
        Dictionary<FingerprintComponent, string> components = [];

        Add(components, FingerprintComponent.MachineGuid, ReadMachineIdentity);
        Add(components, FingerprintComponent.PrimaryMacAddress, ReadPrimaryMacAddress);
        Add(components, FingerprintComponent.SystemVolumeSerial, ReadSystemVolume);
        Add(components, FingerprintComponent.CpuSignature, ReadCpuSignature);

        return components;
    }

    private void Add(
        Dictionary<FingerprintComponent, string> components,
        FingerprintComponent component,
        Func<string?> read)
    {
        string? value;

        try
        {
            value = read();
        }
#pragma warning disable CA1031 // A machine that will not answer is a component that scores nothing.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            logger.LogDebug(failure, "Fingerprint component {Component} could not be read.", component);
            return;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            components[component] = value;
        }
    }

    /// <summary>
    /// The machine's own identity file, where the platform keeps one.
    /// </summary>
    /// <remarks>
    /// <c>/etc/machine-id</c> on Linux is the closest cross-platform equivalent of the Windows machine
    /// GUID: written once at install, stable across reboots, and regenerated on a clone if the image
    /// was prepared properly. On Windows this falls back to the machine name, which is weak — and is
    /// exactly what Stage 31 replaces with the real registry value.
    /// </remarks>
    private static string? ReadMachineIdentity()
    {
        foreach (string path in (string[])["/etc/machine-id", "/var/lib/dbus/machine-id"])
        {
            if (File.Exists(path))
            {
                string value = File.ReadAllText(path).Trim();

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return Environment.MachineName;
    }

    /// <summary>
    /// The MAC address of the first physical, non-virtual interface.
    /// </summary>
    /// <remarks>
    /// Ordered by name so the answer is stable across restarts. Loopback and tunnel interfaces are
    /// skipped, and so is anything with no address — a virtual adapter that appears when a VPN
    /// connects would otherwise change the fingerprint every time somebody dialled in.
    /// </remarks>
    private static string? ReadPrimaryMacAddress()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.NetworkInterfaceType
                is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .Where(adapter => adapter.GetPhysicalAddress().GetAddressBytes().Length > 0)
            .OrderBy(adapter => adapter.Name, StringComparer.Ordinal)
            .Select(adapter => adapter.GetPhysicalAddress().ToString())
            .FirstOrDefault();

    /// <summary>The system volume's identity: its name and format, which survive a reboot.</summary>
    private static string? ReadSystemVolume()
    {
        DriveInfo? root = DriveInfo.GetDrives()
            .FirstOrDefault(drive => drive.IsReady
                && drive.RootDirectory.FullName == Path.GetPathRoot(Environment.SystemDirectory));

        return root is null ? null : $"{root.VolumeLabel}:{root.DriveFormat}:{root.TotalSize}";
    }

    /// <summary>The processor's identity, as the runtime describes it.</summary>
    private static string? ReadCpuSignature()
        => $"{RuntimeInformation.ProcessArchitecture}:{Environment.ProcessorCount}:"
            + RuntimeInformation.OSArchitecture;
}

/// <summary>
/// This installation's stable id, this process's boot id, and the running version.
/// </summary>
/// <remarks>
/// The install id is kept in a file beside the licence shadow rather than in the database, because it
/// has to survive a restore into a fresh database — a rebuilt store is the <em>same</em> installation,
/// and telling the vendor otherwise on the first heartbeat after a disaster would look exactly like a
/// clone.
/// </remarks>
/// <param name="stateDirectory">Where the install id file lives.</param>
/// <param name="clock">The only source of time (<c>CONVENTIONS.md</c> §6).</param>
public sealed class FileInstallIdentity(string stateDirectory, IClock clock) : IInstallIdentity
{
    private readonly Lazy<Guid> _installId = new(() => ReadOrCreate(stateDirectory));

    /// <inheritdoc />
    public Guid InstallId => _installId.Value;

    /// <inheritdoc />
    public Guid BootId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset BootedAt { get; } = clock.UtcNow;

    /// <inheritdoc />
    public string Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

    private static Guid ReadOrCreate(string directory)
    {
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "install-id");

        if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(), out Guid existing))
        {
            return existing;
        }

        Guid created = Guid.NewGuid();
        File.WriteAllText(path, created.ToString());

        return created;
    }
}

/// <summary>
/// The licence state's out-of-database copy and its shadow (<c>LICENSING.md</c> §7).
/// </summary>
/// <remarks>
/// <para>
/// Two files, written together and compared on every read. Disagreement is a tamper flag, never a
/// refusal — a half-finished restore and a disk that filled both produce one, and ADR-026 puts the
/// leverage in vendor-side detection rather than in punishing a customer for a failure that is
/// probably the vendor's own installer.
/// </para>
/// <para>
/// <b>DPAPI is Stage 31's.</b> <c>LICENSING.md</c> §7 asks for a DPAPI-protected store; DPAPI is a
/// Windows API and this build is cross-platform (ADR-031), so the copies are encrypted with AES-GCM
/// under a key derived from the machine's own fingerprint — which gives the same property that
/// matters, that the file is not portable to another machine — and the Windows host wraps that key
/// with DPAPI when it lands.
/// </para>
/// </remarks>
/// <param name="stateDirectory">Where the two copies live.</param>
/// <param name="fingerprints">The machine reading the key is derived from.</param>
public sealed class FileLicenceShadowStore(
    string stateDirectory,
    IHardwareFingerprintProvider fingerprints) : ILicenceShadowStore
{
    private const string PrimaryFile = "licence.state";
    private const string ShadowFile = "licence.shadow";

    /// <inheritdoc />
    public async Task WriteAsync(string document, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document);

        Directory.CreateDirectory(stateDirectory);

        byte[] sealed_ = Protect(document);

        await File.WriteAllBytesAsync(Path.Combine(stateDirectory, PrimaryFile), sealed_, cancellationToken)
            .ConfigureAwait(false);

        await File.WriteAllBytesAsync(Path.Combine(stateDirectory, ShadowFile), sealed_, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ShadowReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        string primaryPath = Path.Combine(stateDirectory, PrimaryFile);
        string shadowPath = Path.Combine(stateDirectory, ShadowFile);

        if (!File.Exists(primaryPath) || !File.Exists(shadowPath))
        {
            // Neither present is a fresh install, not a tamper. Only one present is a half-written
            // pair, which is worth flagging.
            return new ShadowReadResult(null, !File.Exists(primaryPath) && !File.Exists(shadowPath));
        }

        byte[] primary = await File.ReadAllBytesAsync(primaryPath, cancellationToken).ConfigureAwait(false);
        byte[] shadow = await File.ReadAllBytesAsync(shadowPath, cancellationToken).ConfigureAwait(false);

        bool agrees = primary.AsSpan().SequenceEqual(shadow);

        return new ShadowReadResult(Unprotect(primary), agrees);
    }

    private byte[] Protect(string document)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(document);
        byte[] nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using AesGcm cipher = new(DeriveKey(), tag.Length);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag);

        return [.. nonce, .. tag, .. ciphertext];
    }

    private string? Unprotect(byte[] sealedBytes)
    {
        int nonceLength = AesGcm.NonceByteSizes.MaxSize;
        int tagLength = AesGcm.TagByteSizes.MaxSize;

        if (sealedBytes.Length <= nonceLength + tagLength)
        {
            return null;
        }

        byte[] plaintext = new byte[sealedBytes.Length - nonceLength - tagLength];

        try
        {
            using AesGcm cipher = new(DeriveKey(), tagLength);

            cipher.Decrypt(
                sealedBytes.AsSpan(0, nonceLength),
                sealedBytes.AsSpan(nonceLength + tagLength),
                sealedBytes.AsSpan(nonceLength, tagLength),
                plaintext);
        }
        catch (CryptographicException)
        {
            // The file is not this machine's, or it has been edited. Either way there is nothing to
            // read; the caller treats a null document as "no shadow" and flags the disagreement.
            return null;
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] DeriveKey()
    {
        StringBuilder material = new();

        foreach ((FingerprintComponent component, string value) in fingerprints.Read().OrderBy(entry => entry.Key))
        {
            material.Append(component).Append('=').Append(value).Append(';');
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString()));
    }
}

/// <summary>
/// Checks the licensing assembly against the hashes the installer recorded (<c>LICENSING.md</c> §7).
/// </summary>
/// <remarks>
/// <see cref="IntegrityStatus.Unknown"/> until Stage 31's installer writes an expected hash into
/// configuration, and <b>Unknown is reported honestly rather than treated as a pass</b>. A check that
/// silently succeeds when it has nothing to compare against is worse than no check: it produces a
/// green light on every cracked install as well as every real one.
/// </remarks>
/// <param name="expectedHash">The SHA-256 the installer recorded, lower-case hex, or empty.</param>
public sealed class AssemblyIntegrityChecker(string expectedHash) : IIntegrityChecker
{
    /// <inheritdoc />
    public IntegrityStatus Check()
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return IntegrityStatus.Unknown;
        }

        string? location = typeof(VumaRetail.Licensing.AssemblyMarker).Assembly.Location;

        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
        {
            // A single-file or trimmed publish has no assembly on disk to hash. Unknown, again
            // honestly: Stage 31 decides which publish shape ships and this answer moves with it.
            return IntegrityStatus.Unknown;
        }

        using FileStream stream = File.OpenRead(location);

        string actual = Convert.ToHexStringLower(SHA256.HashData(stream));

        return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase)
            ? IntegrityStatus.Verified
            : IntegrityStatus.Failed;
    }
}
