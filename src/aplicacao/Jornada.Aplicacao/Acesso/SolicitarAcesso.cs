using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Acesso;

/// <summary>
/// O que a plataforma decide sobre acesso fora de qualquer pleito. Hoje, só
/// quem é o primeiro administrador de uma empresa (ADR-0018).
/// </summary>
public sealed record RegrasDeAcesso(IReadOnlySet<string> AdministradoresIniciais)
{
    public static readonly RegrasDeAcesso Nenhuma = new(new HashSet<string>());

    public bool EhAdministradorInicial(Matricula matricula) =>
        AdministradoresIniciais.Contains(matricula.Valor, StringComparer.OrdinalIgnoreCase);
}

public sealed record ComandoSolicitarAcesso(
    string Matricula, string Nome, string Email, string Unidade, string PapelPleiteado, string? Justificativa = null);

/// <summary>
/// Cadastro e pleito de papel. Portado de <c>acesso.solicitar_acesso</c>.
///
/// É a única escrita aberta a quem não tem papel, e tem de ser: é justamente
/// quem ainda não foi autorizado que precisa dela. O que ela grava não autoriza
/// nada: cria (ou atualiza) a pessoa e um pedido.
/// </summary>
public sealed class SolicitarAcesso(
    IRepositorioDePessoas pessoas,
    IRepositorioDePleitos pleitos,
    IContextoDeTenantAtual tenant,
    IUnidadeDeTrabalho uow,
    RegrasDeAcesso regras,
    IRelogio relogio)
{
    public sealed record Saida(Pessoa Pessoa, PleitoDeAcesso Pleito);

    public async Task<Resultado<Saida>> ExecutarAsync(ComandoSolicitarAcesso cmd, CancellationToken ct = default)
    {
        DadosDeCadastro dados;
        try { dados = DadosDeCadastro.Conferir(cmd.Matricula, cmd.Nome, cmd.Email, cmd.Unidade); }
        catch (ErroDeDominio e) { return Resultado<Saida>.Recusado(e.Codigo, e.Message); }

        if (!PapelExtensoes.TentarDe(cmd.PapelPleiteado, out var papel))
            return Resultado<Saida>.Recusado("PAPEL_INVALIDO", $"Papel desconhecido: {cmd.PapelPleiteado}.");

        var pessoa = await pessoas.ObterPorMatriculaAsync(dados.Matricula, ct);
        if (pessoa is not null)
        {
            if ((await pleitos.ListarAsync(StatusDoPleito.Pendente, pessoa.Id, ct)).Count > 0)
                return Resultado<Saida>.Recusado("PLEITO_PENDENTE",
                    "Já existe um pleito seu aguardando decisão. Acompanhe em Seu perfil.");
            pessoa.Recadastrar(dados);
        }
        else
        {
            pessoa = Pessoa.Cadastrar(tenant.Atual.Empresa, dados, relogio);
            await pessoas.AdicionarAsync(pessoa, ct);
        }

        var pleito = PleitoDeAcesso.Abrir(pessoa, papel, cmd.Justificativa, relogio);
        if (papel == Papel.Admin && regras.EhAdministradorInicial(dados.Matricula) && !await TemAdministradorAsync(ct))
            pessoa.Conceder(pleito.AprovarPelaConfiguracao(relogio));

        await pleitos.AdicionarAsync(pleito, ct);
        await uow.ConfirmarAsync(ct);
        return Resultado<Saida>.Ok(new Saida(pessoa, pleito));
    }

    private async Task<bool> TemAdministradorAsync(CancellationToken ct) =>
        (await pessoas.ListarAsync(ct: ct)).Any(p => p.Vigentes(relogio.Hoje).Any(a => a.Papel == Papel.Admin));
}
