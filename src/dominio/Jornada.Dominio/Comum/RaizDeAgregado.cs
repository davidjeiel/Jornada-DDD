namespace Jornada.Dominio.Comum;

/// <summary>
/// Fronteira de consistência. Só a raiz é carregada e salva; tudo dentro dela
/// muda junto, na mesma transação.
/// </summary>
public abstract class RaizDeAgregado
{
    private readonly List<EventoDeDominio> _eventos = [];

    public IReadOnlyList<EventoDeDominio> EventosPendentes => _eventos;

    protected void Registrar(EventoDeDominio evento) => _eventos.Add(evento);

    /// <summary>
    /// Chamado pela unidade de trabalho no <c>SaveChanges</c>. Drenar num ponto
    /// só é o que torna impossível "esquecer de publicar o evento".
    /// </summary>
    public IReadOnlyList<EventoDeDominio> DrenarEventos()
    {
        var saida = _eventos.ToArray();
        _eventos.Clear();
        return saida;
    }
}
