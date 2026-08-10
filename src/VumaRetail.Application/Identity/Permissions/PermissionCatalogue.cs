using System.Collections.Frozen;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Identity.Permissions;

/// <summary>
/// One permission a module declares it understands.
/// </summary>
/// <param name="Key">The <c>module.entity.action</c> permission.</param>
/// <param name="Description">What holding it lets somebody do, in words a shop owner recognises.</param>
/// <param name="IsHighRisk">
/// True for permissions that should be granted deliberately and reviewed — anything that changes
/// money, prices, permissions or the audit posture. The admin UI shows these differently; nothing
/// else in the system treats them specially.
/// </param>
public sealed record PermissionDescriptor(PermissionKey Key, string Description, bool IsHighRisk = false);

/// <summary>
/// A module's declaration of the permissions it enforces (ADR-013).
/// </summary>
/// <remarks>
/// Implemented once per module, next to that module's code, and registered at startup. A new module
/// adds permissions without touching identity, which is the property that makes the catalogue worth
/// having: identity would otherwise become the file every module stage has to edit.
/// </remarks>
public interface IModulePermissions
{
    /// <summary>The module these belong to. Must match the first segment of every key.</summary>
    string Module { get; }

    /// <summary>The permissions the module declares.</summary>
    IReadOnlyCollection<PermissionDescriptor> Permissions { get; }
}

/// <summary>
/// The closed set of permissions this installation understands.
/// </summary>
/// <remarks>
/// The point of a catalogue is that granting a permission nobody declared is an <em>error</em>. Left
/// open, a typo in a seed script or an admin UI produces a grant that looks right in every list and
/// authorises nothing, and the bug surfaces as "the manager can't approve refunds" three weeks later.
/// </remarks>
public interface IPermissionCatalogue
{
    /// <summary>Every declared permission, ordered by key.</summary>
    IReadOnlyCollection<PermissionDescriptor> All { get; }

    /// <summary>True when a module has declared this permission.</summary>
    /// <param name="permission">The permission to look for.</param>
    bool Contains(PermissionKey permission);

    /// <summary>Looks a permission up, or returns <c>null</c> if no module declares it.</summary>
    /// <param name="permission">The permission to look for.</param>
    PermissionDescriptor? Find(PermissionKey permission);

    /// <summary>
    /// Parses and validates a permission string, throwing if it is malformed or undeclared.
    /// </summary>
    /// <param name="value">The candidate <c>module.entity.action</c> string.</param>
    /// <returns>The validated permission.</returns>
    /// <exception cref="InvalidPermissionKeyException">The string is not shaped like a permission.</exception>
    /// <exception cref="UndeclaredPermissionException">No module declares it.</exception>
    PermissionKey Require(string value);
}

/// <summary>
/// The catalogue, assembled once at startup from every registered <see cref="IModulePermissions"/>.
/// </summary>
/// <remarks>
/// Frozen after construction. Every request asks this repeatedly, it never changes after startup, and
/// a mutable shared dictionary read from every request thread is a race waiting for a busy Saturday.
/// </remarks>
public sealed class PermissionCatalogue : IPermissionCatalogue
{
    private readonly FrozenDictionary<string, PermissionDescriptor> _permissions;

    /// <summary>Assembles the catalogue from the modules' declarations.</summary>
    /// <param name="modules">Every module declaring permissions.</param>
    /// <exception cref="PermissionCatalogueException">
    /// Two modules declared the same permission, or a module declared one outside its own namespace.
    /// </exception>
    public PermissionCatalogue(IEnumerable<IModulePermissions> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        Dictionary<string, PermissionDescriptor> declared = [];

        foreach (IModulePermissions module in modules)
        {
            foreach (PermissionDescriptor descriptor in module.Permissions)
            {
                // A module declaring a permission in another module's namespace is how a permission
                // ends up owned by nobody — and how it survives that module being extracted.
                if (!string.Equals(descriptor.Key.Module, module.Module, StringComparison.Ordinal))
                {
                    throw new PermissionCatalogueException(
                        $"Module '{module.Module}' declared '{descriptor.Key}', which belongs to module "
                        + $"'{descriptor.Key.Module}'. A module declares only its own permissions.");
                }

                if (!declared.TryAdd(descriptor.Key.Value, descriptor))
                {
                    throw new PermissionCatalogueException(
                        $"Permission '{descriptor.Key}' is declared more than once. Each permission has "
                        + "exactly one owning module.");
                }
            }
        }

        _permissions = declared.ToFrozenDictionary(StringComparer.Ordinal);
        All = [.. declared.Values.OrderBy(descriptor => descriptor.Key.Value, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> All { get; }

    /// <inheritdoc />
    public bool Contains(PermissionKey permission) => _permissions.ContainsKey(permission.Value);

    /// <inheritdoc />
    public PermissionDescriptor? Find(PermissionKey permission)
        => _permissions.TryGetValue(permission.Value, out PermissionDescriptor? descriptor) ? descriptor : null;

    /// <inheritdoc />
    public PermissionKey Require(string value)
    {
        PermissionKey key = PermissionKey.Parse(value);

        return Contains(key) ? key : throw new UndeclaredPermissionException(key);
    }
}

/// <summary>Thrown when the catalogue cannot be assembled from the modules' declarations.</summary>
/// <param name="message">What went wrong.</param>
public sealed class PermissionCatalogueException(string message)
    : DomainException("PERMISSION_CATALOGUE_INVALID", message);

/// <summary>Thrown when a permission is used but no module declares it.</summary>
/// <param name="permission">The undeclared permission.</param>
public sealed class UndeclaredPermissionException(PermissionKey permission)
    : DomainException(
        "PERMISSION_NOT_DECLARED",
        $"No module declares '{permission}'. Permissions come from a module's IModulePermissions "
        + "declaration (ADR-013); granting one that does not exist grants nothing at all.");
