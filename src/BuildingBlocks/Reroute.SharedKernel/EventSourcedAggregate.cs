namespace Reroute.SharedKernel;

/// <summary>
/// Event-sourced aggregate root. State is never mutated directly: command methods validate
/// invariants and call <see cref="Raise"/>, which applies the event via <see cref="When"/>
/// and records it as uncommitted. Rehydration replays history through the same <see cref="When"/>.
/// </summary>
public abstract class EventSourcedAggregate<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _uncommittedEvents = [];

    /// <summary>Number of events already persisted for this stream (the expected version on save).</summary>
    public int Version { get; private set; }

    public IReadOnlyList<IDomainEvent> UncommittedEvents => _uncommittedEvents;

    protected void Raise(IDomainEvent domainEvent)
    {
        When(domainEvent);
        _uncommittedEvents.Add(domainEvent);
    }

    /// <summary>Pure state transition. Must not throw and must not contain business decisions.</summary>
    protected abstract void When(IDomainEvent domainEvent);

    protected void LoadFromHistory(IEnumerable<IDomainEvent> history)
    {
        foreach (var domainEvent in history)
        {
            When(domainEvent);
            Version++;
        }
    }

    /// <summary>Called by the repository after a successful append.</summary>
    public void MarkCommitted()
    {
        Version += _uncommittedEvents.Count;
        _uncommittedEvents.Clear();
    }
}
