using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Catalogo;

/// <summary>
/// Uma linha da trilha de auditoria: quem fez o quê, quando, e o que mudou.
/// Equivale a <c>auditoria_evento</c> e ao <c>servicos.auditar</c> do
/// painel-ddd. Só se acrescenta; nada edita nem apaga um registro.
/// </summary>
public sealed record RegistroDeAuditoria(
    Guid Id,
    IdDeEmpresa Empresa,
    IdDeAtivo Ativo,
    string Acao,
    Guid IdPessoa,
    DateTimeOffset Em,
    IReadOnlyDictionary<string, string>? Antes = null,
    IReadOnlyDictionary<string, string>? Depois = null)
{
    public static RegistroDeAuditoria De(
        Ativo ativo, string acao, Guid idPessoa, IRelogio relogio,
        IReadOnlyDictionary<string, string>? antes = null,
        IReadOnlyDictionary<string, string>? depois = null) =>
        new(Guid.CreateVersion7(), ativo.Empresa, ativo.Id, acao, idPessoa, relogio.Agora, antes, depois);
}

/// <summary>As ações registradas, com os mesmos nomes do painel-ddd.</summary>
public static class AcoesAuditadas
{
    public const string Criar = "criar";
    public const string Alterar = "alterar";
    public const string DefinirResponsavel = "definir_responsavel";
    public const string Relacionar = "relacionar";
    public const string AnexarEvidencia = "anexar_evidencia";
    public const string Submeter = "submeter";
    public const string Validar = "validar";
    public const string Publicar = "publicar";
    public const string AbrirRevisao = "abrir_revisao";
    public const string Descontinuar = "descontinuar";
    public const string Arquivar = "arquivar";
}
