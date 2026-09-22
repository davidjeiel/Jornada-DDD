using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;

namespace Jornada.Dominio.Qualidade;

/// <summary>
/// Fatos sobre o RESTO do catálogo que a calculadora precisa mas o agregado não
/// pode saber sozinho. O caso de uso os obtém pelas portas e entrega prontos.
///
/// É esta separação que transforma <c>qualidade.avaliar(con, id_item)</c> — hoje
/// impossível de testar sem banco — numa função pura (ADR-0001).
/// </summary>
public sealed record ContextoDeQualidade(
    bool PaiExiste,
    bool PaiForaDeVigencia,
    int RelacoesParaAtivosForaDeVigencia,
    int ImplementacoesDaCapacidade);

/// <summary>
/// Portado de <c>catalogo/qualidade.py::avaliar</c>, dimensão por dimensão,
/// com os mesmos pesos, os mesmos descontos e as mesmas pendências.
/// </summary>
public static class CalculadoraDeQualidade
{
    public static ScoreDeQualidade Avaliar(
        Ativo ativo,
        TipoDeAtivo tipo,
        PoliticaDeGovernanca politica,
        ContextoDeQualidade contexto,
        IRelogio relogio)
    {
        var pendencias = new List<string>();
        var hoje = relogio.Hoje;

        // ─────────────────────────────────────────────────────── completude
        // exigidos = ["descricao"] + campos obrigatórios do tipo
        var exigidos = new List<(string Chave, string Rotulo)> { ("descricao", "Descrição") };
        exigidos.AddRange(tipo.CamposObrigatorios.Select(c => (c.Nome, c.Nome)));

        var preenchidos = 0;
        foreach (var (chave, rotulo) in exigidos)
        {
            var valor = chave == "descricao"
                ? ativo.Descricao
                : ativo.Atributos.GetValueOrDefault(chave);

            if (!string.IsNullOrWhiteSpace(valor)) preenchidos++;
            else pendencias.Add($"Preencher {rotulo}");
        }
        var completude = exigidos.Count > 0
            ? (int)Math.Round(100.0 * preenchidos / exigidos.Count, MidpointRounding.ToEven)
            : 100;

        // ───────────────────────────────────────────────────── consistência
        var consistencia = 100;
        if (tipo.Pai is not null)
        {
            if (!contexto.PaiExiste)
            {
                consistencia -= 50;
                pendencias.Add($"Vincular a um item do tipo {tipo.Pai}");
            }
            else if (contexto.PaiForaDeVigencia)
            {
                consistencia -= 30;
                pendencias.Add("Item pai está fora de vigência");
            }
        }

        var orfas = contexto.RelacoesParaAtivosForaDeVigencia;
        if (orfas > 0)
        {
            consistencia -= Math.Min(40, 10 * orfas);
            pendencias.Add($"{orfas} relação(ões) apontam para ativo descontinuado");
        }

        if (ativo.TipoItem.Equals("capacidade", StringComparison.OrdinalIgnoreCase)
            && contexto.ImplementacoesDaCapacidade == 0)
        {
            consistencia -= 20;
            pendencias.Add("Capacidade sem implementação identificada");
        }
        consistencia = Math.Max(0, consistencia);

        // ──────────────────────────────────────────────────────── ownership
        var exigidosPapel = new List<PapelDeOwnership>();
        if (tipo.ExigeOwnerNegocial) exigidosPapel.Add(PapelDeOwnership.OwnerNegocial);
        if (tipo.ExigeOwnerTecnico) exigidosPapel.Add(PapelDeOwnership.OwnerTecnico);

        var papeis = ativo.PapeisVigentes(hoje);
        int ownership;
        if (exigidosPapel.Count > 0)
        {
            var atendidos = exigidosPapel.Count(papeis.Contains);
            ownership = (int)Math.Round(100.0 * atendidos / exigidosPapel.Count,
                                        MidpointRounding.ToEven);
            foreach (var p in exigidosPapel.Where(p => !papeis.Contains(p)))
                pendencias.Add($"Definir {RotuloDeOwnership(p)}");
        }
        else
        {
            // Sem papel exigido pelo tipo, ter QUALQUER responsável vale 100;
            // não ter ninguém vale 70, não 0: o cadastro não está errado, só
            // menos rastreável.
            ownership = papeis.Count > 0 ? 100 : 70;
        }

        // ───────────────────────────────────────────────────────── evidência
        var minima = politica.EvidenciaMinima;
        var totalEv = ativo.Evidencias.Count;
        int evidencia;
        if (minima == 0)
        {
            evidencia = totalEv > 0 ? 100 : 80;
        }
        else
        {
            evidencia = Math.Min(100,
                (int)Math.Round(100.0 * totalEv / minima, MidpointRounding.ToEven));
            if (totalEv < minima)
                pendencias.Add($"Anexar evidência mínima ({totalEv}/{minima})");
        }

        // ────────────────────────────────────────────────────── temporalidade
        var dias = politica.PeriodicidadeRevisaoDias;
        var referencia = ativo.AtualizadoEm != default ? ativo.AtualizadoEm : ativo.CriadoEm;
        var idade = hoje.DayNumber - DateOnly.FromDateTime(referencia.UtcDateTime).DayNumber;

        int temporalidade;
        if (idade <= dias)
        {
            temporalidade = 100;
        }
        else if (idade <= dias * 2)
        {
            temporalidade = 60;
            pendencias.Add($"Revisar cadastro: {idade} dias sem atualização");
        }
        else
        {
            temporalidade = 20;
            pendencias.Add($"Cadastro desatualizado há {idade} dias");
        }

        if (ativo.FimVigencia is { } fim
            && fim < hoje
            && ativo.Status == StatusCicloVida.Publicado)
        {
            temporalidade = Math.Min(temporalidade, 40);
            pendencias.Add("Vigência encerrada mas item continua publicado");
        }

        return new ScoreDeQualidade(completude, consistencia, ownership,
                                    evidencia, temporalidade, pendencias);
    }

    private static string RotuloDeOwnership(PapelDeOwnership p) => p switch
    {
        PapelDeOwnership.OwnerNegocial => "owner negocial",
        PapelDeOwnership.OwnerTecnico => "owner tecnico",
        PapelDeOwnership.Arquiteto => "arquiteto",
        PapelDeOwnership.Mantenedor => "mantenedor",
        _ => p.ToString().ToLowerInvariant(),
    };
}
