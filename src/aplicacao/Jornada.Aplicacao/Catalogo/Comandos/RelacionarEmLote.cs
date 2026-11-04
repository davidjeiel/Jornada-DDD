using Jornada.Dominio.Catalogo;

namespace Jornada.Aplicacao.Catalogo.Comandos;

public sealed record ComandoRelacionarEmLote(
    IdDeAtivo Origem, IReadOnlyList<IdDeAtivo> Destinos, string Tipo,
    Criticidade Criticidade = Criticidade.Media, string? Mecanismo = null);

/// <summary>
/// Várias relações do mesmo tipo de uma vez. Portado de
/// <c>servicos.relacionar_em_lote</c>: mapear as dependências de uma aplicação
/// custava um envio por relação. O próprio ativo e as relações que já existem
/// são ignorados; cada destino passa pelas mesmas regras de
/// <see cref="RelacionarAtivos"/>, e a recusa de um não impede os outros.
/// </summary>
public sealed class RelacionarEmLote(RelacionarAtivos relacionar)
{
    public sealed record Erro(IdDeAtivo Destino, string Codigo, string Mensagem);

    public sealed record Saida(int Criadas, int Ignoradas, IReadOnlyList<Erro> Erros);

    public async Task<Resultado<Saida>> ExecutarAsync(ComandoRelacionarEmLote cmd, CancellationToken ct = default)
    {
        int criadas = 0, ignoradas = 0;
        var erros = new List<Erro>();
        foreach (var destino in cmd.Destinos.Distinct())
        {
            if (destino == cmd.Origem) { ignoradas++; continue; }

            var r = await relacionar.ExecutarAsync(
                new ComandoRelacionarAtivos(cmd.Origem, destino, cmd.Tipo, cmd.Criticidade, cmd.Mecanismo), ct);
            if (!r.Sucesso)
            {
                // Recusa que vale para o lote inteiro (permissão, tipo, mecanismo, origem): para aqui.
                if (r.Codigo is "SEM_PERMISSAO" or "PESSOA_NAO_IDENTIFICADA" or "MODO_LEITURA"
                    or "TIPO_OBRIGATORIO" or "TIPO_DE_RELACAO_INVALIDO" or "MECANISMO_INVALIDO" or "ATIVO_NAO_ENCONTRADO")
                    return Resultado<Saida>.Recusado(r.Codigo, r.Mensagem!);
                erros.Add(new Erro(destino, r.Codigo!, r.Mensagem!));
            }
            else if (r.Valor) criadas++;
            else ignoradas++;
        }
        return Resultado<Saida>.Ok(new Saida(criadas, ignoradas, erros));
    }
}
