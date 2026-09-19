using FluentAssertions;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VumaRetail.ControlPlane;
using Xunit;

namespace VumaRetail.ControlPlane.Tests;

public sealed class VendorApiTests
{
    [Theory]
    [InlineData("Support", VendorRole.Support, true)]
    [InlineData("Billing", VendorRole.Support, false)]
    [InlineData("Engineering", VendorRole.Engineering, true)]
    [InlineData("Admin", VendorRole.Billing, true)]
    [InlineData("not-a-role", VendorRole.Support, false)]
    public void Vendor_routes_require_the_declared_role(string header, VendorRole minimum, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Vendor-Role"] = header;

        VendorApiAuthorization.IsAllowed(context, minimum).Should().Be(expected);
    }

    [Fact]
    public void Production_does_not_accept_a_caller_supplied_role_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Vendor-Role"] = "Admin";

        VendorApiAuthorization.IsAllowed(context, VendorRole.Admin,
            new VendorApiAuthorizationOptions(), isDevelopment: false).Should().BeFalse();
    }

    [Fact]
    public void Production_binds_role_to_configured_client_certificate()
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new("CN=vuma-vendor", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));
        var context = new DefaultHttpContext();
        context.Connection.ClientCertificate = certificate;
        var options = new VendorApiAuthorizationOptions
        {
            CertificateRoles = new Dictionary<string, VendorRole>
            {
                [certificate.Thumbprint!.Replace(" ", string.Empty, StringComparison.Ordinal)] = VendorRole.Support
            }
        };

        VendorApiAuthorization.IsAllowed(context, VendorRole.Support, options, isDevelopment: false)
            .Should().BeTrue();
        VendorApiAuthorization.IsAllowed(context, VendorRole.Admin, options, isDevelopment: false)
            .Should().BeFalse();
    }
}
