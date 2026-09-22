using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Catalogo;

// Eventos de domínio do catálogo (ADR-0008 §1). Gravados na outbox DENTRO da
// transação do fato gerador — nunca publicados direto na fila.
//
// Regra de versionamento: SÓ ADICIONE campo. Remover ou renomear quebra
// consumidor; para mudar, publique um EventoV2 e mantenha os dois até o último
// consumidor migrar.

public sealed record AtivoCadastrado(
    IdDeAtivo Ativo, IdDeEmpresa Empresa, string TipoItem, string Codigo) : EventoDeDominio
{
    public override string ChaveDeIdempotencia => $"ativo-cadastrado:{Ativo}";
}

public sealed record AtivoSubmetido(
    IdDeAtivo Ativo, IdDeEmpresa Empresa, int Revisao, Guid IdAutor, string Motivo) : EventoDeDominio
{
    public override string ChaveDeIdempotencia => $"ativo-submetido:{Ativo}:{Revisao}";
}

public sealed record RevisaoPublicada(
    IdDeAtivo Ativo, IdDeEmpresa Empresa, int Revisao) : EventoDeDominio
{
    public override string ChaveDeIdempotencia => $"revisao-publicada:{Ativo}:{Revisao}";
}

public sealed record AtivoDescontinuado(
    IdDeAtivo Ativo, IdDeEmpresa Empresa, string Motivo) : EventoDeDominio
{
    public override string ChaveDeIdempotencia => $"ativo-descontinuado:{Ativo}";
}

public sealed record ValidacaoDecidida(
    IdDeAtivo Ativo, IdDeEmpresa Empresa, Guid IdValidacao,
    string Etapa, bool Aprovada, Guid IdDecisor) : EventoDeDominio
{
    public override string ChaveDeIdempotencia => $"validacao-decidida:{IdValidacao}";
}
