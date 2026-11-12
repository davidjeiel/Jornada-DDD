using Jornada.Dominio.Testes.Harness;
using Jornada.Persistencia.Cosmos;

namespace Jornada.Persistencia.Testes.Adaptadores;

/// <summary>
/// Qual adaptador a API liga. O ambiente corporativo só entrega
/// COSMOS_CONNECTION_STRING; o compose local declara provedor e conexão.
/// </summary>
[Suite("Escolha da persistência")]
public sealed class EscolhaDaPersistenciaTestes
{
    private const string Conta = "AccountEndpoint=https://conta:443/;AccountKey=chave==";

    [Teste("sem nada configurado, a API roda em memória")]
    public void SemNadaEmMemoria()
    {
        var escolha = EscolhaDaPersistencia.De(null, null, null);
        Verificar.Falso(escolha.UsaCosmos);
        Verificar.Igual<string?>(null, escolha.StringDeConexao);
    }

    [Teste("só com COSMOS_CONNECTION_STRING, o Cosmos liga sozinho com ela")]
    public void VariavelDoAmbienteLigaOCosmos()
    {
        var escolha = EscolhaDaPersistencia.De(null, Conta, null);
        Verificar.Verdadeiro(escolha.UsaCosmos);
        Verificar.Igual<string?>(Conta, escolha.StringDeConexao);
    }

    [Teste("a variável do ambiente vence ConnectionStrings:Cosmos")]
    public void VariavelDoAmbienteVence()
    {
        var escolha = EscolhaDaPersistencia.De("cosmos", Conta, "AccountEndpoint=https://outra:443/;AccountKey=x==");
        Verificar.Igual<string?>(Conta, escolha.StringDeConexao);
    }

    [Teste("o compose local segue funcionando com provedor e ConnectionStrings:Cosmos")]
    public void ConfiguracaoAntigaSegue()
    {
        var escolha = EscolhaDaPersistencia.De("Cosmos", "  ", Conta);
        Verificar.Verdadeiro(escolha.UsaCosmos);
        Verificar.Igual<string?>(Conta, escolha.StringDeConexao);
    }

    [Teste("provedor declarado memoria vence a variável do ambiente")]
    public void ProvedorDeclaradoVence() =>
        Verificar.Falso(EscolhaDaPersistencia.De("memoria", Conta, null).UsaCosmos);

    [Teste("Cosmos sem conexão nenhuma derruba a partida e diz as duas opções")]
    public void CosmosSemConexao()
    {
        var erro = Verificar.Lanca<InvalidOperationException>(() => EscolhaDaPersistencia.De("cosmos", null, null));
        Verificar.Verdadeiro(erro.Message.Contains(EscolhaDaPersistencia.VariavelDoAmbiente), erro.Message);
    }

    [Teste("provedor desconhecido derruba a partida, em vez de cair na memória")]
    public void ProvedorDesconhecido() =>
        Verificar.Lanca<InvalidOperationException>(() => EscolhaDaPersistencia.De("postgres", Conta, null));
}
