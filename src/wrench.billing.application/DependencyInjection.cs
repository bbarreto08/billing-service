using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using wrench.billing.application.Services;

namespace wrench.billing.application;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OrcamentoService>();
        services.AddScoped<PagamentoService>();
        return services;
    }
}