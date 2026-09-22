using System.Diagnostics;
using System.Reflection;

namespace Jornada.Dominio.Testes.Harness;

/// <summary>Marca um método de teste. Trocar por <c>[Fact]</c> do xUnit é substituir esta linha.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TesteAttribute(string descricao) : Attribute
{
    public string Descricao { get; } = descricao;
}

/// <summary>Marca uma classe que contém testes.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SuiteAttribute(string nome) : Attribute
{
    public string Nome { get; } = nome;
}

public sealed class FalhaDeVerificacao(string mensagem) : Exception(mensagem);

/// <summary>Asserções mínimas. Mapeiam 1:1 para <c>Assert.*</c> do xUnit.</summary>
public static class Verificar
{
    public static void Verdadeiro(bool condicao, string porque = "")
    {
        if (!condicao) throw new FalhaDeVerificacao($"esperava verdadeiro: {porque}");
    }

    public static void Falso(bool condicao, string porque = "")
    {
        if (condicao) throw new FalhaDeVerificacao($"esperava falso: {porque}");
    }

    public static void Igual<T>(T esperado, T obtido, string porque = "")
    {
        if (!EqualityComparer<T>.Default.Equals(esperado, obtido))
            throw new FalhaDeVerificacao(
                $"esperava <{esperado}>, obteve <{obtido}>{(porque.Length > 0 ? $" — {porque}" : "")}");
    }

    public static void Contem<T>(IEnumerable<T> colecao, T item, string porque = "")
    {
        if (!colecao.Contains(item))
            throw new FalhaDeVerificacao(
                $"esperava encontrar <{item}> em [{string.Join(", ", colecao)}]" +
                $"{(porque.Length > 0 ? $" — {porque}" : "")}");
    }

    public static void NaoContem<T>(IEnumerable<T> colecao, T item, string porque = "")
    {
        if (colecao.Contains(item))
            throw new FalhaDeVerificacao(
                $"NÃO esperava encontrar <{item}> em [{string.Join(", ", colecao)}]" +
                $"{(porque.Length > 0 ? $" — {porque}" : "")}");
    }

    public static void Vazio<T>(IEnumerable<T> colecao, string porque = "")
    {
        var itens = colecao.ToArray();
        if (itens.Length > 0)
            throw new FalhaDeVerificacao(
                $"esperava coleção vazia, veio [{string.Join(", ", itens)}]" +
                $"{(porque.Length > 0 ? $" — {porque}" : "")}");
    }

    public static TExcecao Lanca<TExcecao>(Action acao, string porque = "")
        where TExcecao : Exception
    {
        try { acao(); }
        catch (TExcecao e) { return e; }
        catch (Exception e)
        {
            throw new FalhaDeVerificacao(
                $"esperava {typeof(TExcecao).Name}, veio {e.GetType().Name}: {e.Message}");
        }
        throw new FalhaDeVerificacao(
            $"esperava {typeof(TExcecao).Name}, nada foi lançado" +
            $"{(porque.Length > 0 ? $" — {porque}" : "")}");
    }
}

/// <summary>Descobre e executa os testes por reflexão. Devolve 0 se tudo passou.</summary>
public static class Executor
{
    public static int Executar(Assembly assembly, string? filtro = null)
    {
        var relogio = Stopwatch.StartNew();
        int passou = 0, falhou = 0;
        var falhas = new List<string>();

        var suites = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<SuiteAttribute>() is not null)
            .OrderBy(t => t.GetCustomAttribute<SuiteAttribute>()!.Nome, StringComparer.Ordinal);

        foreach (var suite in suites)
        {
            var nomeSuite = suite.GetCustomAttribute<SuiteAttribute>()!.Nome;
            if (filtro is not null && !nomeSuite.Contains(filtro, StringComparison.OrdinalIgnoreCase))
                continue;

            Console.WriteLine();
            Console.WriteLine($"  {nomeSuite}");

            var instancia = Activator.CreateInstance(suite);
            var metodos = suite.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<TesteAttribute>() is not null);

            foreach (var metodo in metodos)
            {
                var descricao = metodo.GetCustomAttribute<TesteAttribute>()!.Descricao;
                try
                {
                    var r = metodo.Invoke(instancia, null);
                    if (r is Task tarefa) tarefa.GetAwaiter().GetResult();
                    Console.WriteLine($"    \u001b[32mOK  \u001b[0m {descricao}");
                    passou++;
                }
                catch (Exception e)
                {
                    var raiz = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
                    Console.WriteLine($"    \u001b[31mFALHA\u001b[0m {descricao}");
                    Console.WriteLine($"          {raiz.Message}");
                    falhas.Add($"{nomeSuite} > {descricao}: {raiz.Message}");
                    falhou++;
                }
            }
        }

        relogio.Stop();
        Console.WriteLine();
        Console.WriteLine(new string('─', 72));
        var cor = falhou == 0 ? "\u001b[32m" : "\u001b[31m";
        Console.WriteLine(
            $"  {cor}{passou} passou, {falhou} falhou\u001b[0m  em {relogio.ElapsedMilliseconds} ms");

        if (falhou > 0)
        {
            Console.WriteLine();
            foreach (var f in falhas) Console.WriteLine($"  \u001b[31m×\u001b[0m {f}");
        }
        Console.WriteLine();
        return falhou == 0 ? 0 : 1;
    }
}
