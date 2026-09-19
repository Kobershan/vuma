using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using VumaRetail.Web.Security;

namespace VumaRetail.IntegrationTests.Security;

public sealed class ProductionSecurityGuardTests
{
    [Fact]
    public void Production_rejects_the_shipped_development_jwt_key()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("AllowedHosts", "store.example.test"),
                new KeyValuePair<string, string?>("ConnectionStrings:Vuma", "Host=db;Password=real-secret"),
                new KeyValuePair<string, string?>(
                    "Vuma:Jwt:SigningKey",
                    "development-only-signing-key-replace-me-0000000000"),
            ])
            .Build();

        Action validate = () => ProductionSecurityGuard.Validate(
            configuration,
            new TestHostEnvironment { EnvironmentName = Environments.Production },
            cloudHost: false);

        validate.Should().Throw<InvalidOperationException>()
            .WithMessage("*Vuma:Jwt:SigningKey*");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = typeof(ProductionSecurityGuardTests).Assembly.GetName().Name!;

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
