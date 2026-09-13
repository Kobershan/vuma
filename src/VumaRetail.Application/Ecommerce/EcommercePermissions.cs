#pragma warning disable CS1591
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Ecommerce;

/// <summary>Permission catalogue for the Stage 21 storefront and channel surfaces.</summary>
public sealed class EcommercePermissions : IModulePermissions
{
    public const string View = "ecommerce.catalogue.view";
    public const string Manage = "ecommerce.channel.manage";
    public const string Checkout = "ecommerce.checkout.manage";
    public const string Payment = "ecommerce.payment.manage";

    public string Module => "ecommerce";
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(View), "View published storefront catalogue."),
        new(PermissionKey.Parse(Manage), "Manage storefront channel registrations.", IsHighRisk: true),
        new(PermissionKey.Parse(Checkout), "Submit and manage storefront checkouts.", IsHighRisk: true),
        new(PermissionKey.Parse(Payment), "Process storefront payment notifications.", IsHighRisk: true),
    ];
}

/// <summary>Licensing manifest for Stage 21 Ecommerce.</summary>
public sealed class EcommerceModuleManifest : IModuleManifest
{
    public string Module => "ecommerce";
    public string LicenceFlag => "ecommerce";
    public string Description => "Storefront catalogue, checkout and channel integrations.";
    public bool IsCore => false;
}
