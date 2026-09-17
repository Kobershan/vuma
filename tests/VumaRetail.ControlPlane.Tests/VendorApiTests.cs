using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;
using VumaRetail.ControlPlane;

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
}
