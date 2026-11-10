using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using wrench.billing.application;
using wrench.billing.application.Contracts;
using wrench.billing.application.Extensions;

namespace wrench.billing.tests.Application;

public class PublishExtensionsTests
{
    private static async Task<(ServiceProvider Provider, ITestHarness Harness)> IniciarAsync()
    {
        var provider = new ServiceCollection().AddMassTransitTestHarness().BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (provider, harness);
    }

    [Fact]
    public async Task CorrelationIdGuid_VaiNoTransporteENoHeader()
    {
        var (provider, harness) = await IniciarAsync();
        await using var _ = provider;
        var correlacao = Guid.NewGuid();

        await harness.Bus.PublishComCorrelacao(
            new OrcamentoFalhou(Guid.NewGuid(), correlacao.ToString(), "x"), correlacao.ToString(), CancellationToken.None);

        var publicada = (await harness.Published.SelectAsync<OrcamentoFalhou>().First()).Context;
        Assert.Equal(correlacao, publicada.CorrelationId);
        Assert.Equal(correlacao.ToString(), publicada.Headers.Get<string>(global::wrench.billing.application.Extensions.PublishExtensions.HeaderCorrelationId));
    }

    [Fact]
    public async Task CorrelationIdTexto_VaiSomenteNoHeader()
    {
        var (provider, harness) = await IniciarAsync();
        await using var _ = provider;

        await harness.Bus.PublishComCorrelacao(
            new OrcamentoFalhou(Guid.NewGuid(), "corr-OS-77", "x"), "corr-OS-77", CancellationToken.None);

        var publicada = (await harness.Published.SelectAsync<OrcamentoFalhou>().First()).Context;
        Assert.Equal("corr-OS-77", publicada.Headers.Get<string>(global::wrench.billing.application.Extensions.PublishExtensions.HeaderCorrelationId));
        Assert.NotEqual("corr-OS-77", publicada.CorrelationId?.ToString());
    }

    [Fact]
    public async Task CorrelationIdEmBranco_NaoCriaHeader()
    {
        var (provider, harness) = await IniciarAsync();
        await using var _ = provider;

        await harness.Bus.PublishComCorrelacao(
            new OrcamentoFalhou(Guid.NewGuid(), " ", "x"), " ", CancellationToken.None);

        var publicada = (await harness.Published.SelectAsync<OrcamentoFalhou>().First()).Context;
        Assert.Null(publicada.Headers.Get<string>(global::wrench.billing.application.Extensions.PublishExtensions.HeaderCorrelationId));
    }
}