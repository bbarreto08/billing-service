using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using wrench.billing.api.Controllers;
using wrench.billing.application;
using wrench.billing.application.Abstractions;
using wrench.billing.application.Contracts;
using wrench.billing.application.Services;

namespace wrench.billing.tests.Api;

/// <summary>Sobe a API inteira com banco e barramento em memória (sem Postgres, SQS ou rede).</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string WebhookSecret = "segredo-webhook-teste";
    public const string JwtSecret = "chave-jwt-de-teste-com-mais-de-32-caracteres";
    public const string Issuer = "wrench-teste";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:UseInMemory", "true");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Messaging:Transport", "InMemory");
        builder.UseSetting("Pagamento:WebhookSecret", WebhookSecret);
        builder.UseSetting("Pagamento:ReconciliacaoHabilitada", "false");
        builder.UseSetting("Simulacao:Habilitada", "true");
        builder.UseSetting("JwtOptions:Secret", JwtSecret);
        builder.UseSetting("JwtOptions:Issuer", Issuer);
        builder.UseSetting("JwtOptions:Audience", Issuer);

        // A simulação chama o webhook "por HTTP"; nos testes isso é roteado para o próprio TestServer.
        builder.ConfigureServices(services => services
            .AddHttpClient(SimulacaoController.ClienteWebhook)
            .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler()));
    }

    public HttpClient Cliente() => ComPapel("Cliente");

    public HttpClient Admin() => ComPapel("Admin");

    public HttpClient ComPapel(string papel)
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarToken(papel));
        return cliente;
    }

    public static string GerarToken(string papel)
    {
        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.ASCII.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(Issuer, Issuer,
            [new Claim(ClaimTypes.Role, papel)], expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credenciais);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Cria um orçamento pelo mesmo caso de uso que o consumer da fila usa.</summary>
    public async Task<Guid> CriarOrcamentoAsync(Guid? ordemServicoId = null)
    {
        using var escopo = Services.CreateScope();
        var comando = new GerarOrcamento(ordemServicoId ?? Guid.NewGuid(), Guid.NewGuid().ToString(), Guid.NewGuid(),
            "cliente@wrench.com.br", "Revisão", 200m, [new ItemOrcamentoDto(Guid.NewGuid(), "Óleo", 4, 50m)]);

        await escopo.ServiceProvider.GetRequiredService<OrcamentoService>().GerarAsync(comando, CancellationToken.None);

        var db = escopo.ServiceProvider.GetRequiredService<IBillingDb>();
        return db.Orcamentos.Single(o => o.OrdemServicoId == comando.OrdemServicoId).Id;
    }
}