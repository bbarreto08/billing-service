using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using wrench.billing.application;
using wrench.billing.application.Abstractions;
using wrench.billing.domain.Pagamentos;
using wrench.billing.infra.Pagamentos;
using Microsoft.Extensions.DependencyInjection;
using wrench.billing.application.Webhooks;

namespace wrench.billing.tests.Api;

public class ApiTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private static JsonElement Json(string texto) => JsonDocument.Parse(texto).RootElement;

    private static HttpRequestMessage Webhook(string externalId, string? assinatura = null, string? requestId = null)
    {
        requestId ??= Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var msg = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/pagamento/webhook?data.id={externalId}&type=payment")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    type = "payment",
                    action = "payment.updated",
                    data = new { id = externalId }
                }),
                Encoding.UTF8, "application/json")
        };
        msg.Headers.Add("x-request-id", requestId);
        msg.Headers.Add("x-signature",
            assinatura ?? AssinaturaWebhookMercadoPago.Assinar(ApiFactory.WebhookSecret, externalId, requestId, ts));
        return msg;
    }

    [Fact]
    public async Task Health_RespondeSaudavel()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task OpenApi_EDocumentacaoEstaoDisponiveis()
    {
        var cliente = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/docs-ui")).StatusCode);
    }

    [Fact]
    public async Task Orcamento_SemTokenRetorna401()
    {
        var resposta = await fabrica.CreateClient().GetAsync($"/api/v1/orcamento/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Orcamento_InexistenteRetorna404()
    {
        var cliente = fabrica.Cliente();

        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"/api/v1/orcamento/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await cliente.GetAsync($"/api/v1/orcamento/ordem-servico/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await cliente.PutAsync($"/api/v1/orcamento/{Guid.NewGuid()}/aprovar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await cliente.GetAsync($"/api/v1/pagamento/orcamento/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task FluxoFeliz_AprovarPagarViaWebhookEConsultar()
    {
        var cliente = fabrica.Cliente();
        var ordemServicoId = Guid.NewGuid();
        var orcamentoId = await fabrica.CriarOrcamentoAsync(ordemServicoId);

        var consulta = await cliente.GetAsync($"/api/v1/orcamento/ordem-servico/{ordemServicoId}");
        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        Assert.Equal("Gerado", Json(await consulta.Content.ReadAsStringAsync()).GetProperty("status").GetString());

        var aprovar = await cliente.PutAsync($"/api/v1/orcamento/{orcamentoId}/aprovar", null);
        Assert.Equal(HttpStatusCode.OK, aprovar.StatusCode);
        var pagamento = Json(await aprovar.Content.ReadAsStringAsync());
        Assert.Equal("Pendente", pagamento.GetProperty("status").GetString());

        var simulacao = await fabrica.Admin().PostAsync($"/api/v1/simulacao/pagamentos/{orcamentoId}/aprovado", null);
        Assert.Equal(HttpStatusCode.OK, simulacao.StatusCode);
        Assert.Equal(200, Json(await simulacao.Content.ReadAsStringAsync()).GetProperty("webhookStatus").GetInt32());

        var final = await cliente.GetAsync($"/api/v1/orcamento/{orcamentoId}");
        Assert.Equal("Pago", Json(await final.Content.ReadAsStringAsync()).GetProperty("status").GetString());

        var pag = await cliente.GetAsync($"/api/v1/pagamento/orcamento/{orcamentoId}");
        Assert.Equal("Aprovado", Json(await pag.Content.ReadAsStringAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Recusar_RetornaOrcamentoRecusadoEAprovarDepoisRetorna409()
    {
        var cliente = fabrica.Cliente();
        var orcamentoId = await fabrica.CriarOrcamentoAsync();

        var recusar = await cliente.PutAsJsonAsync($"/api/v1/orcamento/{orcamentoId}/recusar",
            new { motivoRecusa = "muito caro" });
        Assert.Equal(HttpStatusCode.OK, recusar.StatusCode);
        Assert.Equal("Recusado", Json(await recusar.Content.ReadAsStringAsync()).GetProperty("status").GetString());

        var aprovar = await cliente.PutAsync($"/api/v1/orcamento/{orcamentoId}/aprovar", null);
        Assert.Equal(HttpStatusCode.Conflict, aprovar.StatusCode);
    }

    [Fact]
    public async Task Recusar_SemCorpoUsaMotivoPadrao()
    {
        var cliente = fabrica.Cliente();
        var orcamentoId = await fabrica.CriarOrcamentoAsync();

        var resposta = await cliente.PutAsync($"/api/v1/orcamento/{orcamentoId}/recusar", null);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Webhook_AssinaturaInvalidaRetorna401()
    {
        var resposta = await fabrica.CreateClient().SendAsync(Webhook("123", assinatura: "ts=1,v1=deadbeef"));

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Webhook_SemAssinaturaRetorna401()
    {
        var msg = Webhook("123");
        msg.Headers.Remove("x-signature");

        Assert.Equal(HttpStatusCode.Unauthorized, (await fabrica.CreateClient().SendAsync(msg)).StatusCode);
    }

    [Fact]
    public async Task Webhook_PagamentoDesconhecidoEConfirmadoComOk()
    {
        var resposta = await fabrica.CreateClient().SendAsync(Webhook("pagamento-que-nao-existe"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Webhook_EventoQueNaoEPagamentoEIgnoradoComOk()
    {
        var requestId = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pagamento/webhook?data.id=99&type=plan")
        {
            Content = new StringContent("""{"type":"plan","data":{"id":99}}""", Encoding.UTF8, "application/json")
        };
        msg.Headers.Add("x-request-id", requestId);
        msg.Headers.Add("x-signature", AssinaturaWebhookMercadoPago.Assinar(ApiFactory.WebhookSecret, "99", requestId, ts));

        Assert.Equal(HttpStatusCode.OK, (await fabrica.CreateClient().SendAsync(msg)).StatusCode);
    }

    [Fact]
    public async Task Webhook_CorpoInvalidoComAssinaturaValidaUsaDadosDaQuery()
    {
        var requestId = Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pagamento/webhook?data.id=abc&type=payment")
        {
            Content = new StringContent("não é json", Encoding.UTF8, "application/json")
        };
        msg.Headers.Add("x-request-id", requestId);
        msg.Headers.Add("x-signature", AssinaturaWebhookMercadoPago.Assinar(ApiFactory.WebhookSecret, "abc", requestId, ts));

        Assert.Equal(HttpStatusCode.OK, (await fabrica.CreateClient().SendAsync(msg)).StatusCode);
    }

    [Fact]
    public async Task Webhook_ReenvioDaMesmaNotificacaoEIdempotente()
    {
        var cliente = fabrica.Cliente();
        var orcamentoId = await fabrica.CriarOrcamentoAsync();
        var pagamento = Json(await (await cliente.PutAsync($"/api/v1/orcamento/{orcamentoId}/aprovar", null))
            .Content.ReadAsStringAsync());
        var externalId = pagamento.GetProperty("externalId").GetString()!;

        using (var escopo = fabrica.Services.CreateScope())
            escopo.ServiceProvider.GetRequiredService<MockPagamentoGateway>()
                .Simular(externalId, StatusProvedor.Aprovado);

        var requestId = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(Webhook(externalId, requestId: requestId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(Webhook(externalId, requestId: requestId))).StatusCode);

        using var verificacao = fabrica.Services.CreateScope();
        var db = verificacao.ServiceProvider.GetRequiredService<IBillingDb>();
        Assert.Equal(PagamentoStatus.Aprovado, db.Pagamentos.Single(p => p.ExternalId == externalId).Status);
    }

    [Theory]
    [InlineData("recusado")]
    [InlineData("expirado")]
    public async Task Simulacao_FalhasCancelamOOrcamento(string resultado)
    {
        var cliente = fabrica.Cliente();
        var orcamentoId = await fabrica.CriarOrcamentoAsync();
        await cliente.PutAsync($"/api/v1/orcamento/{orcamentoId}/aprovar", null);

        var simulacao = await fabrica.Admin().PostAsync($"/api/v1/simulacao/pagamentos/{orcamentoId}/{resultado}", null);

        Assert.Equal(HttpStatusCode.OK, simulacao.StatusCode);
        var final = await cliente.GetAsync($"/api/v1/orcamento/{orcamentoId}");
        Assert.Equal("Cancelado", Json(await final.Content.ReadAsStringAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Simulacao_ResultadoInvalidoRetorna400()
    {
        var cliente = fabrica.Cliente();

        var resposta = await fabrica.Admin().PostAsync($"/api/v1/simulacao/pagamentos/{Guid.NewGuid()}/talvez", null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Simulacao_SemCobrancaRetorna404()
    {
        var cliente = fabrica.Cliente();

        var resposta = await fabrica.Admin().PostAsync($"/api/v1/simulacao/pagamentos/{Guid.NewGuid()}/aprovado", null);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Simulacao_ExigeAutenticacao()
    {
        var resposta = await fabrica.CreateClient()
            .PostAsync($"/api/v1/simulacao/pagamentos/{Guid.NewGuid()}/aprovado", null);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
