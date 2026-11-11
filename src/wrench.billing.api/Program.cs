using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using wrench.billing.api.Configuration;
using wrench.billing.api.Controllers;
using wrench.billing.api.Middlewares;
using wrench.billing.api.Options;
using wrench.billing.application;
using wrench.billing.infra;
using wrench.billing.infra.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureSerilog();

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddObservability(builder.Configuration, builder.Environment);

builder.Services.Configure<SimulacaoOptions>(builder.Configuration.GetSection(SimulacaoOptions.Secao));
builder.Services.ConfigureAuthentication(builder.Configuration);

builder.Services
    .AddBillingApplication()
    .AddBillingInfra(builder.Configuration);

// A simulação chama o webhook público do próprio serviço (mesmo caminho do Mercado Pago).
builder.Services.AddHttpClient(SimulacaoController.ClienteWebhook, (sp, cliente) =>
    cliente.BaseAddress = new Uri(sp.GetRequiredService<IOptions<SimulacaoOptions>>().Value.WebhookBaseUrl));

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddDbContextCheck<BillingDbContext>("banco", tags: ["ready"]);

builder.Services.ConfigureOpenApi();
builder.Services.AddControllers();
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1.0);
    options.ReportApiVersions = true;
    options.AssumeDefaultVersionWhenUnspecified = true;
});

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var escopo = app.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<BillingDbContext>();

    if (db.Database.IsRelational())
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync();
}

app.UseCorrelationId();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();

app.MapOpenApi();
app.MapScalarApiReference("docs-ui", options => options.Title = "Wrench Billing");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/docs-ui", permanent: true));

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    AllowCachingResponses = false,
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

public partial class Program;