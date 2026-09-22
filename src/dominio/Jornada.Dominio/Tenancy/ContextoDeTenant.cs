namespace Jornada.Dominio.Tenancy;

/// <summary>
/// Quem está falando, e por qual empresa. Resolvido UMA VEZ no middleware, a
/// partir do token, e validado contra o control plane (ADR-0003, ADR-0007).
///
/// Repare no que NÃO existe aqui: um jeito de trocar de empresa. Nenhum caso de
/// uso recebe <c>tenant_id</c> de quem chama — se recebesse, seria, por
/// construção, um caso de uso capaz de ler dados de outra empresa.
/// </summary>
public sealed record ContextoDeTenant(
    IdDeEmpresa Empresa,
    IdDeUnidade? Unidade = null)
{
    public override string ToString() =>
        Unidade is null ? $"empresa {Empresa}" : $"empresa {Empresa} / unidade {Unidade}";
}
