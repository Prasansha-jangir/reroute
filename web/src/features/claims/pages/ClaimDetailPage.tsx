import { useParams } from 'react-router';
import { formatDate, formatMoney } from '@/shared/lib/format';
import { useUiStore } from '@/shared/store/uiStore';
import { ClaimTimeline } from '../components/ClaimTimeline';
import { useClaim, useClaimTimeline, useSubmitClaim } from '../hooks/useClaims';
import type { Claim } from '../api/claimsApi';

export default function ClaimDetailPage() {
  const { claimId = '' } = useParams();
  const claim = useClaim(claimId);
  const timeline = useClaimTimeline(claimId);

  if (claim.isPending) return <p aria-busy="true">Loading claim…</p>;
  if (claim.isError) return <p role="alert">{claim.error.message}</p>;

  return (
    <div className="grid">
      <ClaimSummary claim={claim.data} />
      <section className="card" aria-labelledby="history">
        <h2 id="history">History</h2>
        {timeline.data ? <ClaimTimeline entries={timeline.data} /> : <p aria-busy="true">Loading history…</p>}
      </section>
    </div>
  );
}

function ClaimSummary({ claim }: { claim: Claim }) {
  const submit = useSubmitClaim(claim);
  const openCopilot = useUiStore((s) => s.openCopilot);

  return (
    <section className="card" aria-labelledby="claim-title">
      <h2 id="claim-title">{claim.flightNumber} · {claim.origin} → {claim.destination}</h2>
      <p>{formatDate(claim.flightDate)} · {claim.disruptionType} · {Math.round(claim.distanceKm)} km</p>
      <p className="amount">{formatMoney(claim.compensationAmount, claim.currency)}</p>
      <p><span className={`badge badge--${claim.status}`}>{claim.status}</span> {claim.eligibilityReason}</p>
      <div className="row">
        {claim.status === 'Eligible' && (
          <button onClick={() => submit.mutate()} disabled={submit.isPending}>Submit to airline</button>
        )}
        <button className="secondary" onClick={() => openCopilot(claim.claimId)}>Ask the copilot about this claim</button>
      </div>
      {submit.isError && <p role="alert">{submit.error.message}</p>}
    </section>
  );
}
