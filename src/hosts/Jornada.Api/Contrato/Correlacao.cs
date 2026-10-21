using Jornada.Dominio.Comum;

namespace Jornada.Api.Contrato;

/// <summary>
/// <c>x-correlation-id</c> (guia CAIXA §7): recebido ou gerado no início da
/// requisição, devolvido ao consumidor, incluído nos logs e no corpo de erro.
///
/// Fica OPCIONAL no contrato — o guia diz "recebido ou gerado". Um valor que
/// não é UUID é descartado e substituído: o identificador vai para log, e
/// texto livre vindo de fora não deve ir para log sem validação.
/// </summary>
public sealed class MiddlewareDeCorrelacao(RequestDelegate proximo, ILogger<MiddlewareDeCorrelacao> logger)
{
    public const string Cabecalho = "x-correlation-id";
    private const string Chave = "jornada:correlation-id";

    public async Task InvokeAsync(HttpContext http)
    {
        var recebido = http.Request.Headers[Cabecalho].ToString();
        var id = Guid.TryParse(recebido, out var guid) ? guid : Guid.CreateVersion7();
        var valor = id.ToString();

        http.Items[Chave] = valor;
        http.Response.OnStarting(() =>
        {
            http.Response.Headers[Cabecalho] = valor;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = valor }))
        {
            await proximo(http);
        }
    }

    /// <summary>O id desta requisição. Fora de uma requisição, gera um novo.</summary>
    public static string De(HttpContext http) =>
        http.Items.TryGetValue(Chave, out var v) && v is string s ? s : Guid.CreateVersion7().ToString();
}

/// <summary>Escreve <see cref="ApiError"/> — o único formato de erro da API.</summary>
public static class RespostaDeErro
{
    public static ApiError Montar(HttpContext http, string codigo, string mensagem) => new()
    {
        Codigo = codigo,
        Mensagem = mensagem,
        CorrelationId = MiddlewareDeCorrelacao.De(http),
        DataHora = http.RequestServices.GetRequiredService<IRelogio>().Agora,
    };

    /// <summary>Para os endpoints: o status sai do código, nunca da rota.</summary>
    public static IResult Resultado(HttpContext http, string codigo, string mensagem) =>
        Results.Json(Montar(http, codigo, mensagem), statusCode: CodigosDeErro.StatusPara(codigo));

    /// <summary>Para os middlewares, que escrevem direto na resposta.</summary>
    public static async Task EscreverAsync(HttpContext http, string codigo, string mensagem)
    {
        http.Response.StatusCode = CodigosDeErro.StatusPara(codigo);
        await http.Response.WriteAsJsonAsync(Montar(http, codigo, mensagem));
    }
}
