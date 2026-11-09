namespace Reroute.SharedKernel;

/// <summary>
/// State-stored aggregate root (used by CRUD-ish contexts such as Itinerary).
/// Collects domain events that infrastructure dispatches/persists to the outbox after commit.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
