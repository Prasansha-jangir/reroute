namespace Reroute.SharedKernel;

/// <summary>
/// Marker for something that happened in the domain. Domain events are immutable facts,
/// named in the past tense (ClaimFiled, EligibilityAssessed) and form part of the ubiquitous language.
/// </summary>
public interface IDomainEvent;
