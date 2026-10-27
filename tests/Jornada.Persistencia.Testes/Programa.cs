using System.Reflection;
using Jornada.Dominio.Testes.Harness;

Console.WriteLine();
Console.WriteLine("  Jornada DDD — contrato das portas de persistência");
Console.WriteLine("  a mesma suíte para todo adaptador: memória e Cosmos DB");
Console.WriteLine(new string('─', 72));

var filtro = args.FirstOrDefault(a => !a.StartsWith('-'));
return Executor.Executar(Assembly.GetExecutingAssembly(), filtro);
