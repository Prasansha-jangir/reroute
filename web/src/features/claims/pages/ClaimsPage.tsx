import { Link, useSearchParams } from 'react-router';
import { formatDate, formatMoney } from '@/shared/lib/format';
import { claimStatusSchema, type ClaimStatus } from '../api/claimsApi';
import { FileClaimForm } from '../components/FileClaimForm';
import { useMyClaims } from '../hooks/useClaims';

export default function ClaimsPage() {
  // Filters live in the URL → shareable, back-button friendly, survives refresh.
  const [params, setParams] = useSearchParams();
  const parsedStatus = claimStatusSchema.safeParse(params.get('status'));
  const status: ClaimStatus | undefined = parsedStatus.success ? parsedStatus.data : undefined;
  const page = Math.max(1, Number(params.get('page') ?? 1));
  const { data, isPending, isError, error, isPlaceholderData } = useMyClaims({ status, page });

  return (
    <div className="grid">
      <section className="card" aria-labelledby="my-claims">
        <header className="row space-between">
          <h2 id="my-claims">My claims</h2>
          <select
            aria-label="Filter by status"
            value={status ?? ''}
            onChange={(e) => setParams(e.target.value ? { status: e.target.value } : {})}
          >
            <option value="">All statuses</option>
            {claimStatusSchema.options.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </header>

        {isPending && <p aria-busy="true">Loading claims…</p>}
        {isError && <p role="alert">{error.message}</p>}
        {data && data.items.length === 0 && <p>No claims yet. Had a bad flight? File one →</p>}
        {data && data.items.length > 0 && (
          <table style={{ opacity: isPlaceholderData ? 0.6 : 1 }}>
            <thead>
              <tr><th>Flight</th><th>Date</th><th>Status</th><th className="num">Compensation</th></tr>
            </thead>
            <tbody>
              {data.items.map((c) => (
                <tr key={c.claimId}>
                  <td><Link to={`/claims/${c.claimId}`}>{c.flightNumber}</Link></td>
                  <td>{formatDate(c.flightDate)}</td>
                  <td><span className={`badge badge--${c.status}`}>{c.status}</span></td>
                  <td className="num">{formatMoney(c.compensationAmount, c.currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {data && data.totalCount > data.pageSize && (
          <nav className="row" aria-label="Pagination">
            <button disabled={page <= 1} onClick={() => setParams({ ...(status ? { status } : {}), page: String(page - 1) })}>Previous</button>
            <button disabled={page * data.pageSize >= data.totalCount} onClick={() => setParams({ ...(status ? { status } : {}), page: String(page + 1) })}>Next</button>
          </nav>
        )}
      </section>
      <FileClaimForm />
    </div>
  );
}
