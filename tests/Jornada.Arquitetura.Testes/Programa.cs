using System.Reflection;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;

// ═══════════════════════════════════════════════════════════════════════════
//  TESTES DE ARQUITETURA — a regra de dependência do ADR-0001 como build gate.
//
//  "A regra de dependência não é combinado de equipe, é teste que quebra o
//   build. Sem isto, o hexágono dura três sprints."
// ═══════════════════════════════════════════════════════════════════════════

var dominio = typeof(Ativo).Assembly;
var aplicacao = typeof(IRepositorioDeAtivos).Assembly;

int falhas = 0;

void Regra(string nome, Func<string?> verificar)
{
    var problema = verificar();
    if (problema is null)
    {
        Console.WriteLine($"    \u001b[32mOK  \u001b[0m {nome}");
    }
    else
    {
        Console.WriteLine($"    \u001b[31mFALHA\u001b[0m {nome}");
        Console.WriteLine($"          {problema}");
        falhas++;
    }
}

/// Prefixos proibidos no núcleo. Se o nome de um assembly referenciado começa
/// com um destes, o núcleo foi contaminado por infraestrutura.
string[] proibidos =
[
    "Microsoft.EntityFrameworkCore",
    "Microsoft.AspNetCore",
    "Microsoft.Extensions.DependencyInjection",
    "Microsoft.Extensions.Hosting",
    "Microsoft.Extensions.Configuration",
    "Npgsql",
    "Azure.",
    "Dapper",
    "StackExchange.Redis",
    "System.Net.Http",
    "System.Data.Common",
];

/// Tipos que o compilador gera (closures de lambda, iteradores, display classes)
/// carregam campos estáticos mutáveis por natureza. Não são código que alguém
/// escreveu, e inspecioná-los só produz falso positivo.
bool NaoEhGeradoPeloCompilador(Type t) =>
    !t.Name.Contains('<')
    && t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null;

string? SemReferenciaProibida(Assembly assembly)
{
    var achados = assembly.GetReferencedAssemblies()
        .Select(a => a.Name ?? "")
        .Where(n => proibidos.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        .Distinct()
        .ToArray();

    return achados.Length == 0
        ? null
        : $"{assembly.GetName().Name} referencia infraestrutura: {string.Join(", ", achados)}";
}

Console.WriteLine();
Console.WriteLine("  Jornada DDD — guardas de arquitetura (ADR-0001)");
Console.WriteLine(new string('─', 72));
Console.WriteLine();
Console.WriteLine("  Regra de dependência");

Regra("o DOMÍNIO não conhece infraestrutura", () => SemReferenciaProibida(dominio));

Regra("a APLICAÇÃO não conhece infraestrutura", () => SemReferenciaProibida(aplicacao));

Regra("o DOMÍNIO não conhece a aplicação (as setas apontam para dentro)", () =>
    dominio.GetReferencedAssemblies().Any(a => a.Name == "Jornada.Aplicacao")
        ? "Jornada.Dominio referencia Jornada.Aplicacao — inversão quebrada"
        : null);

Regra("o DOMÍNIO não conhece adaptador nenhum", () =>
{
    var adaptadores = dominio.GetReferencedAssemblies()
        .Select(a => a.Name ?? "")
        .Where(n => n.StartsWith("Jornada.Adaptadores", StringComparison.Ordinal)
                 || n.StartsWith("Jornada.Persistencia", StringComparison.Ordinal))
        .ToArray();
    return adaptadores.Length == 0 ? null
        : $"Jornada.Dominio referencia {string.Join(", ", adaptadores)}";
});

Console.WriteLine();
Console.WriteLine("  Portas e contratos");

Regra("toda porta é interface pública e mora em Jornada.Aplicacao.Portas", () =>
{
    var forasteiras = aplicacao.GetTypes()
        .Where(t => t.IsInterface && t.IsPublic)
        .Where(t => t.Namespace != "Jornada.Aplicacao.Portas")
        .Select(t => t.FullName!)
        .ToArray();
    return forasteiras.Length == 0 ? null
        : $"interfaces fora de Portas: {string.Join(", ", forasteiras)}";
});

Regra("nenhuma porta expõe tipo de infraestrutura na assinatura", () =>
{
    var vazamentos = new List<string>();
    foreach (var porta in aplicacao.GetTypes().Where(t => t.IsInterface && t.IsPublic))
    {
        foreach (var metodo in porta.GetMethods())
        {
            var tipos = metodo.GetParameters().Select(p => p.ParameterType)
                              .Append(metodo.ReturnType);
            foreach (var tipo in tipos)
            {
                var assemblyDoTipo = tipo.Assembly.GetName().Name ?? "";
                if (proibidos.Any(p => assemblyDoTipo.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    vazamentos.Add($"{porta.Name}.{metodo.Name} → {tipo.Name}");
            }
        }
    }
    return vazamentos.Count == 0 ? null : string.Join("; ", vazamentos);
});

Console.WriteLine();
Console.WriteLine("  Modelagem");

Regra("agregados não têm construtor público (só fábrica nomeada)", () =>
{
    var abertos = dominio.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract
                 && typeof(Jornada.Dominio.Comum.RaizDeAgregado).IsAssignableFrom(t))
        .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length > 0)
        .Select(t => t.Name)
        .ToArray();
    return abertos.Length == 0 ? null
        : $"construtor público em: {string.Join(", ", abertos)} — use fábrica nomeada";
});

// Os serviços de DECISÃO do domínio — as regras que o ADR-0001 tirou de
// dentro do `con`. Lista explícita de propósito: "tudo que se chama Politica*"
// pegaria também PoliticaDeGovernanca, que é DADO (a linha da tabela de
// políticas), não serviço. Confundir os dois é o que tornava a regra inútil.
string[] servicosDeDecisao =
[
    "PoliticaDePublicacao",      // era governanca.pre_check
    "PoliticaDeAutorizacao",     // era acesso.pode / pode_decidir
    "CalculadoraDeQualidade",    // era qualidade.avaliar
    "MaquinaDeCicloDeVida",      // era governanca.TRANSICOES
    "CaminhoDePublicacao",       // era governanca.ETAPAS_PUBLICACAO
];

Regra("os serviços de decisão são classes estáticas (sem estado, sem injeção)", () =>
{
    var problemas = new List<string>();
    foreach (var nome in servicosDeDecisao)
    {
        var tipo = dominio.GetTypes().FirstOrDefault(t => t.Name == nome);
        if (tipo is null) { problemas.Add($"{nome} não existe mais"); continue; }

        // "static class" em IL = abstract + sealed
        if (!(tipo.IsAbstract && tipo.IsSealed))
            problemas.Add($"{nome} deixou de ser static");
    }
    return problemas.Count == 0 ? null : string.Join("; ", problemas);
});

Regra("não há estado estático mutável no domínio", () =>
{
    var mutaveis = dominio.GetTypes()
        .Where(t => t.Namespace?.StartsWith("Jornada.Dominio", StringComparison.Ordinal) == true)
        .Where(NaoEhGeradoPeloCompilador)
        .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                          .Where(f => !f.IsInitOnly && !f.IsLiteral)
                          .Where(f => !f.Name.Contains('<'))   // cache de lambda: <>c, <>O
                          .Select(f => $"{t.Name}.{f.Name}"))
        .ToArray();

    return mutaveis.Length == 0 ? null
        : $"campo estático mutável (regra que muda sozinha entre requisições): {string.Join(", ", mutaveis)}";
});

Regra("não há DateTime.Now / UtcNow solto no domínio (use IRelogio)", () =>
{
    // Um campo/propriedade estática de data no domínio quase sempre significa
    // que alguém leu o relógio do sistema direto. O tempo entra por IRelogio.
    var suspeitos = dominio.GetTypes()
        .Where(t => t.Namespace?.StartsWith("Jornada.Dominio", StringComparison.Ordinal) == true)
        .Where(NaoEhGeradoPeloCompilador)
        .Where(t => t != typeof(Jornada.Dominio.Comum.RelogioDoSistema))
        .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                          .Where(f => f.FieldType == typeof(DateTime)
                                   || f.FieldType == typeof(DateTimeOffset))
                          .Select(f => $"{t.Name}.{f.Name}"))
        .ToArray();
    return suspeitos.Length == 0 ? null : string.Join(", ", suspeitos);
});

Console.WriteLine();
Console.WriteLine(new string('─', 72));
if (falhas == 0)
{
    Console.WriteLine("  \u001b[32mtodas as guardas de arquitetura passaram\u001b[0m");
    Console.WriteLine();
    return 0;
}
Console.WriteLine($"  \u001b[31m{falhas} guarda(s) violada(s) — ver ADR-0001\u001b[0m");
Console.WriteLine();
return 1;
