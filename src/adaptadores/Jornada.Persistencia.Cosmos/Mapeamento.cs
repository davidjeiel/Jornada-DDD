using System.Text.Json;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Tenancy;

namespace Jornada.Persistencia.Cosmos;

/// <summary>
/// Domínio ↔ documento. Os rótulos de enum são os mesmos da API (status em
/// <c>snake_case</c>, criticidade pela chave): o que está no banco se lê sem
/// tradução.
/// </summary>
internal static class Mapeamento
{
    public static string Texto(Guid valor) => valor.ToString("D");

    public static DocumentoDeAtivo ParaDocumento(Ativo a) => new()
    {
        Id = Texto(a.Id.Valor),
        Empresa = Texto(a.Empresa.Valor),
        IdAtivo = Texto(a.Id.Valor),
        Unidade = a.Unidade is { } u ? Texto(u.Valor) : null,
        TipoItem = a.TipoItem,
        Codigo = a.Codigo.Valor,
        Nome = a.Nome,
        NomeNormalizado = Normalizar(a.Nome),
        Descricao = a.Descricao,
        IdPai = a.IdPai is { } p ? Texto(p.Valor) : null,
        Criticidade = a.Criticidade.Chave(),
        Status = MaquinaDeCicloDeVida.Rotulo(a.Status),
        Origem = a.Origem.ToString(),
        RevisaoAtual = a.RevisaoAtual,
        UltimaRevisaoPublicada = a.UltimaRevisaoPublicada,
        FimVigencia = a.FimVigencia,
        CriadoEm = a.CriadoEm,
        AtualizadoEm = a.AtualizadoEm,
        Atributos = new Dictionary<string, string>(a.Atributos),
        Responsaveis = [.. a.Responsaveis.Select(r =>
            new ResponsavelEmbutido(r.IdPessoa, r.Papel.ToString(), r.InicioVigencia, r.FimVigencia))],
        Evidencias = [.. a.Evidencias.Select(e => new EvidenciaEmbutida(e.Id, e.Tipo, e.Titulo, e.Url))],
    };

    public static Ativo ParaDominio(DocumentoDeAtivo d) => Ativo.Reidratar(
        new IdDeAtivo(Guid.Parse(d.Id)),
        new IdDeEmpresa(Guid.Parse(d.Empresa)),
        d.TipoItem,
        CodigoDeAtivo.De(d.Codigo),
        d.Nome,
        d.Descricao,
        d.IdPai is { } pai ? new IdDeAtivo(Guid.Parse(pai)) : null,
        CriticidadeExtensoes.De(d.Criticidade),
        Status(d.Status),
        Enum.Parse<OrigemDoAtivo>(d.Origem),
        d.RevisaoAtual,
        d.CriadoEm,
        d.AtualizadoEm,
        unidade: d.Unidade is { } u ? new IdDeUnidade(Guid.Parse(u)) : null,
        fimVigencia: d.FimVigencia,
        ultimaRevisaoPublicada: d.UltimaRevisaoPublicada,
        atributos: d.Atributos,
        responsaveis: d.Responsaveis.Select(r => new Responsabilidade(
            r.IdPessoa, Enum.Parse<PapelDeOwnership>(r.Papel), r.InicioVigencia, r.FimVigencia)),
        evidencias: d.Evidencias.Select(e => new Evidencia(e.Id, e.Tipo, e.Titulo, e.Url)));

    public static DocumentoDeValidacao ParaDocumento(ValidacaoEmProcesso v) => new()
    {
        Id = Texto(v.Id),
        Empresa = Texto(v.Empresa.Valor),
        IdAtivo = Texto(v.Ativo.Valor),
        Revisao = v.Revisao,
        Etapa = v.Etapa.ToString(),
        IdSubmissor = v.IdSubmissor,
        Status = v.Status.ToString(),
        IdDecisor = v.IdDecisor,
        Motivo = v.Motivo,
    };

    public static ValidacaoEmProcesso ParaDominio(DocumentoDeValidacao d) => ValidacaoEmProcesso.Reidratar(
        Guid.Parse(d.Id),
        new IdDeAtivo(Guid.Parse(d.IdAtivo)),
        new IdDeEmpresa(Guid.Parse(d.Empresa)),
        d.Revisao,
        Enum.Parse<EtapaValidacao>(d.Etapa),
        d.IdSubmissor,
        Enum.Parse<StatusDaValidacao>(d.Status),
        d.IdDecisor,
        d.Motivo);

    public static DocumentoDeEvento ParaDocumento(EventoDeDominio evento, IdDeEmpresa empresa, IdDeAtivo ativo) => new()
    {
        Id = Texto(evento.Id),
        Empresa = Texto(empresa.Valor),
        IdAtivo = Texto(ativo.Valor),
        TipoEvento = evento.GetType().Name,
        Payload = JsonSerializer.Serialize(evento, evento.GetType()),
        ChaveDeIdempotencia = evento.ChaveDeIdempotencia,
        OcorridoEm = evento.OcorridoEm,
    };

    /// <summary>O mesmo critério do adaptador em memória: sem diferenciar maiúsculas, inclusive acentuadas.</summary>
    public static string Normalizar(string nome) => nome.Trim().ToLowerInvariant();

    /// <summary>"em_validacao" → <see cref="StatusCicloVida.EmValidacao"/>.</summary>
    public static StatusCicloVida Status(string rotulo) =>
        Enum.Parse<StatusCicloVida>(rotulo.Replace("_", ""), ignoreCase: true);
}
