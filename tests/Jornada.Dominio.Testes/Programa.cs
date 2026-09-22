using System.Reflection;
using Jornada.Dominio.Testes.Harness;

Console.WriteLine();
Console.WriteLine("  Jornada DDD — suíte de domínio");
Console.WriteLine("  regras portadas de painel-ddd (ADR-0012: os testes são a especificação)");
Console.WriteLine(new string('─', 72));

var filtro = args.FirstOrDefault(a => !a.StartsWith('-'));
return Executor.Executar(Assembly.GetExecutingAssembly(), filtro);
