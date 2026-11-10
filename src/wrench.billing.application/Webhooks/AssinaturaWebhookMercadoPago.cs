using System.Security.Cryptography;
using System.Text;

namespace wrench.billing.application.Webhooks;

/// <summary>
/// Validação do header <c>x-signature</c> das notificações do Mercado Pago.
/// Formato: <c>ts=&lt;epoch&gt;,v1=&lt;hmac-sha256-hex&gt;</c>, calculado sobre o manifesto
/// <c>id:&lt;data.id&gt;;request-id:&lt;x-request-id&gt;;ts:&lt;ts&gt;;</c>.
/// Partes ausentes na notificação são omitidas do manifesto.
/// </summary>
public static class AssinaturaWebhookMercadoPago
{
    public static bool Validar(string? segredo, string? xSignature, string? xRequestId, string? dataId)
    {
        if (string.IsNullOrWhiteSpace(segredo) || string.IsNullOrWhiteSpace(xSignature))
            return false;

        string? ts = null;
        string? v1 = null;

        foreach (var parte in xSignature.Split(','))
        {
            var par = parte.Split('=', 2);
            if (par.Length != 2) continue;

            var chave = par[0].Trim();
            var valor = par[1].Trim();

            if (chave == "ts") ts = valor;
            else if (chave == "v1") v1 = valor;
        }

        if (string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(v1))
            return false;

        var esperado = Calcular(segredo, dataId, xRequestId, ts);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(esperado),
            Encoding.UTF8.GetBytes(v1.ToLowerInvariant()));
    }

    /// <summary>Gera o valor do header x-signature (usado pelo mock para simular o Mercado Pago).</summary>
    public static string Assinar(string segredo, string? dataId, string? xRequestId, string ts)
        => $"ts={ts},v1={Calcular(segredo, dataId, xRequestId, ts)}";

    private static string Calcular(string segredo, string? dataId, string? xRequestId, string ts)
    {
        var manifesto = new StringBuilder();

        if (!string.IsNullOrEmpty(dataId))
            manifesto.Append("id:").Append(NormalizarId(dataId)).Append(';');
        if (!string.IsNullOrEmpty(xRequestId))
            manifesto.Append("request-id:").Append(xRequestId).Append(';');
        manifesto.Append("ts:").Append(ts).Append(';');

        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(segredo),
            Encoding.UTF8.GetBytes(manifesto.ToString()));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // O Mercado Pago exige o data.id em minúsculas quando é alfanumérico.
    private static string NormalizarId(string dataId)
        => dataId.All(char.IsLetterOrDigit) ? dataId.ToLowerInvariant() : dataId;
}
