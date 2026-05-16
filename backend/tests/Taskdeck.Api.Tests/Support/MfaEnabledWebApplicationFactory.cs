using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Tests.Support;

public class MfaEnabledWebApplicationFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<MfaPolicySettings>();
            services.AddSingleton(new MfaPolicySettings { EnableMfaSetup = true });
        });
    }
}
