using wrench.billing.application;
using wrench.billing.application.Webhooks;

namespace wrench.billing.tests.Application;

public class AssinaturaWebhookTests
{
    private const string Segredo = "segredo-de-teste";

    [Fact]
    public void AssinaturaGeradaPeloMesmoSegredoEValida()
    {
        var header = AssinaturaWebhookMercadoPago.Assinar(Segredo, "12345", "req-1", "1700000000");

        Assert.True(AssinaturaWebhookMercadoPago.Validar(Segredo, header, "req-1", "12345"));
    }

    [Fact]
    public void ManifestoNormalizaDataIdAlfanumericoParaMinusculo()
    {
        var header = AssinaturaWebhookMercadoPago.Assinar(Segredo, "ABC123", "req-1", "1700000000");

        Assert.True(AssinaturaWebhookMercadoPago.Validar(Segredo, header, "req-1", "abc123"));
    }

    [Fact]
    public void ValidaSemRequestId()
    {
        var header = AssinaturaWebhookMercadoPago.Assinar(Segredo, "12345", null, "1700000000");

        Assert.True(AssinaturaWebhookMercadoPago.Validar(Segredo, header, null, "12345"));
    }

    [Theory]
    [InlineData("outro-id", "req-1")]
    [InlineData("12345", "outro-request")]
    public void DadosAlteradosInvalidam(string dataId, string requestId)
    {
        var header = AssinaturaWebhookMercadoPago.Assinar(Segredo, "12345", "req-1", "1700000000");

        Assert.False(AssinaturaWebhookMercadoPago.Validar(Segredo, header, requestId, dataId));
    }

    [Fact]
    public void SegredoDiferenteInvalida()
    {
        var header = AssinaturaWebhookMercadoPago.Assinar(Segredo, "12345", "req-1", "1700000000");

        Assert.False(AssinaturaWebhookMercadoPago.Validar("outro-segredo", header, "req-1", "12345"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("ts=1700000000")]
    [InlineData("v1=abc")]
    [InlineData("ts=1700000000,v1=0000")]
    public void HeaderAusenteOuMalFormadoInvalida(string? header)
        => Assert.False(AssinaturaWebhookMercadoPago.Validar(Segredo, header, "req-1", "12345"));

    [Fact]
    public void SegredoNaoConfiguradoInvalida()
    {
        var header = AssinaturaWebhookMercadoPago.Assinar(Segredo, "12345", "req-1", "1700000000");

        Assert.False(AssinaturaWebhookMercadoPago.Validar("", header, "req-1", "12345"));
    }
}
