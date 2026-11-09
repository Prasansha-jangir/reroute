import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { ApiError } from '@/shared/api/errors';
import { formatMoney } from '@/shared/lib/format';
import type { DisruptionType } from '../api/claimsApi';
import { useFileClaim } from '../hooks/useClaims';

export function FileClaimForm() {
  const navigate = useNavigate();
  const fileClaim = useFileClaim();
  const [type, setType] = useState<DisruptionType>('Delay');
  const fieldErrors = fileClaim.error instanceof ApiError ? fileClaim.error.fieldErrors : {};

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    fileClaim.mutate(
      {
        flightNumber: String(form.get('flightNumber')),
        flightDate: String(form.get('flightDate')),
        origin: String(form.get('origin')).toUpperCase(),
        destination: String(form.get('destination')).toUpperCase(),
        disruptionType: type,
        arrivalDelayMinutes: Number(form.get('arrivalDelayMinutes') ?? 0),
        cancellationNoticeDays: type === 'Cancellation' ? Number(form.get('cancellationNoticeDays')) : undefined,
      },
      { onSuccess: (result) => void navigate(`/claims/${result.claimId}`) },
    );
  }

  return (
    <form onSubmit={onSubmit} className="card form" noValidate>
      <h2>File a compensation claim</h2>
      <Field name="flightNumber" label="Flight number" placeholder="KL1234" errors={fieldErrors['flightNumber']} />
      <Field name="flightDate" label="Flight date" type="date" errors={fieldErrors['flightDate']} />
      <div className="row">
        <Field name="origin" label="From (IATA)" placeholder="AMS" maxLength={3} errors={fieldErrors['origin']} />
        <Field name="destination" label="To (IATA)" placeholder="ATH" maxLength={3} errors={fieldErrors['destination']} />
      </div>
      <label>
        What happened?
        <select value={type} onChange={(e) => setType(e.target.value as DisruptionType)}>
          <option value="Delay">Delayed</option>
          <option value="Cancellation">Cancelled</option>
          <option value="DeniedBoarding">Denied boarding</option>
        </select>
      </label>
      {type === 'Cancellation' ? (
        <Field name="cancellationNoticeDays" label="Days of notice before departure" type="number" min={0} errors={fieldErrors['cancellationNoticeDays']} />
      ) : (
        <Field name="arrivalDelayMinutes" label="Arrival delay (minutes)" type="number" min={0} errors={fieldErrors['arrivalDelayMinutes']} />
      )}

      {fileClaim.error && !(fileClaim.error instanceof ApiError && fileClaim.error.status === 400) && (
        <p role="alert" className="error">{fileClaim.error.message}</p>
      )}
      {fileClaim.data && <p role="status">Estimated compensation: {formatMoney(fileClaim.data.compensationAmount)}</p>}

      <button type="submit" disabled={fileClaim.isPending}>{fileClaim.isPending ? 'Checking…' : 'Check & file claim'}</button>
    </form>
  );
}

interface FieldProps extends React.InputHTMLAttributes<HTMLInputElement> {
  name: string;
  label: string;
  errors?: string[] | undefined;
}

function Field({ name, label, errors, ...input }: FieldProps) {
  const errorId = `${name}-error`;
  return (
    <label>
      {label}
      <input name={name} aria-invalid={errors ? true : undefined} aria-describedby={errors ? errorId : undefined} required {...input} />
      {errors && <span id={errorId} className="field-error">{errors.join(' ')}</span>}
    </label>
  );
}
