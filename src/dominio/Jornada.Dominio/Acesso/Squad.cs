using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Acesso;

/// <summary>
/// Um time da empresa. Portado de <c>squad</c> e das rotas <c>/squads</c> do
/// painel-ddd: título, sigla (única, em maiúsculas, até 20 letras) e descrição.
/// Inativar não apaga: os vínculos existentes ficam.
/// </summary>
public sealed class Squad
{
    public const int TamanhoMaximoDaSigla = 20;

    public Guid Id { get; }
    public IdDeEmpresa Empresa { get; }
    public string Sigla { get; private set; }
    public string Titulo { get; private set; }
    public string Descricao { get; private set; }
    public bool Ativa { get; private set; }

    private Squad(Guid id, IdDeEmpresa empresa, string sigla, string titulo, string descricao, bool ativa)
    {
        Id = id;
        Empresa = empresa;
        Sigla = sigla;
        Titulo = titulo;
        Descricao = descricao;
        Ativa = ativa;
    }

    public static Squad Criar(IdDeEmpresa empresa, string? sigla, string? titulo, string? descricao)
    {
        var (s, t, d) = Conferir(sigla, titulo, descricao);
        return new Squad(Guid.CreateVersion7(), empresa, s, t, d, ativa: true);
    }

    public void Alterar(string? sigla, string? titulo, string? descricao) =>
        (Sigla, Titulo, Descricao) = Conferir(sigla, titulo, descricao);

    public void Inativar() => Ativa = false;

    /// <summary>A sigla como é guardada e comparada: sem espaços nas pontas, em maiúsculas.</summary>
    public static string NormalizarSigla(string? sigla) => (sigla ?? "").Trim().ToUpperInvariant();

    private static (string, string, string) Conferir(string? sigla, string? titulo, string? descricao)
    {
        var s = NormalizarSigla(sigla);
        var t = (titulo ?? "").Trim();
        var d = (descricao ?? "").Trim();
        if (s.Length == 0 || t.Length == 0 || d.Length == 0)
            throw new ErroDeDominio("SQUAD_INCOMPLETA", "Título, sigla e descrição são obrigatórios.");
        if (s.Length > TamanhoMaximoDaSigla)
            throw new ErroDeDominio("SIGLA_INVALIDA", $"A sigla deve ter no máximo {TamanhoMaximoDaSigla} caracteres.");
        return (s, t, d);
    }

    /// <summary>Reidratação pela persistência. Não usar em regra de negócio.</summary>
    public static Squad Reidratar(Guid id, IdDeEmpresa empresa, string sigla, string titulo, string descricao, bool ativa) =>
        new(id, empresa, sigla, titulo, descricao, ativa);
}
