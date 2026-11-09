import { delay, http, HttpResponse } from 'msw';

/**
 * In-browser fake backend for `npm run dev:mock` — lets you click through the whole UI with no
 * gateway, Keycloak, SQL, Kafka or Docker. State lives in memory and resets on page reload.
 * The rules below are a SIMPLIFIED copy of the real EU261 policy (src/Services/Claims/.../Eu261CompensationPolicy.cs).
 */

type Status = 'Filed' | 'Eligible' | 'Ineligible' | 'SubmittedToAirline' | 'AcceptedByAirline' | 'RejectedByAirline' | 'Escalated' | 'Paid';

interface MockClaim {
  claimId: string;
  travellerId: string;
  flightNumber: string;
  flightDate: string;
  origin: string;
  destination: string;
  disruptionType: 'Delay' | 'Cancellation' | 'DeniedBoarding';
  status: Status;
  compensationAmount: number;
  currency: string;
  eligibilityReason: string;
  distanceKm: number;
  version: number;
  filedAt: string;
  updatedAt: string;
}

interface MockEvent {
  version: number;
  eventType: string;
  occurredAt: string;
  data: string;
}

const AIRPORTS: Record<string, [number, number, boolean]> = {
  AMS: [52.31, 4.77, true], CDG: [49.01, 2.55, true], FRA: [50.04, 8.56, true], MAD: [40.5, -3.57, true],
  BCN: [41.3, 2.08, true], FCO: [41.8, 12.24, true], LIS: [38.77, -9.13, true], ATH: [37.94, 23.94, true],
  DUB: [53.43, -6.25, true], CPH: [55.62, 12.65, true], LHR: [51.47, -0.45, false], JFK: [40.64, -73.78, false],
  TLV: [32.01, 34.89, false], DXB: [25.25, 55.37, false],
};

const USER = { id: 'mock-traveller', name: 'Demo Traveller (mock)', email: 'traveller@reroute.test', roles: ['traveller', 'ops'] };

const claims = new Map<string, MockClaim>();
const events = new Map<string, MockEvent[]>();

function airport(code: string): [number, number, boolean] {
  const found = AIRPORTS[code];
  if (!found) throw new Error(`Unknown airport ${code}`);
  return found;
}

function distanceKm(a: string, b: string): number {
  const [lat1, lon1] = airport(a);
  const [lat2, lon2] = airport(b);
  const r = (d: number) => (d * Math.PI) / 180;
  const h = Math.sin(r(lat2 - lat1) / 2) ** 2 + Math.cos(r(lat1)) * Math.cos(r(lat2)) * Math.sin(r(lon2 - lon1) / 2) ** 2;
  return Math.round(6371 * 2 * Math.atan2(Math.sqrt(h), Math.sqrt(1 - h)));
}

function assess(input: FileBody, km: number): { eligible: boolean; amount: number; reason: string } {
  const intraEu = airport(input.origin)[2] && airport(input.destination)[2];
  const amount = km <= 1500 ? 250 : intraEu || km <= 3500 ? 400 : 600;
  if (input.disruptionType === 'Delay' && input.arrivalDelayMinutes < 180)
    return { eligible: false, amount: 0, reason: `Arrival delay of ${input.arrivalDelayMinutes} min is below the 3-hour threshold.` };
  if (input.disruptionType === 'Cancellation' && (input.cancellationNoticeDays ?? 0) >= 14)
    return { eligible: false, amount: 0, reason: `Cancellation was notified ${input.cancellationNoticeDays} days in advance (≥ 14).` };
  return { eligible: true, amount, reason: input.disruptionType === 'Delay' ? `Arrival delay of ${input.arrivalDelayMinutes} min ≥ 3 h.` : `${input.disruptionType} qualifies for compensation.` };
}

function append(claimId: string, eventType: string, data: Record<string, unknown>): void {
  const stream = events.get(claimId) ?? [];
  stream.push({ version: stream.length + 1, eventType, occurredAt: new Date().toISOString(), data: JSON.stringify(data) });
  events.set(claimId, stream);
}

function validationProblem(errors: Record<string, string[]>) {
  return HttpResponse.json(
    { title: 'One or more validation errors occurred.', status: 400, errors },
    { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
  );
}

interface FileBody {
  flightNumber: string;
  flightDate: string;
  origin: string;
  destination: string;
  disruptionType: MockClaim['disruptionType'];
  arrivalDelayMinutes: number;
  cancellationNoticeDays?: number;
}

function seed(): void {
  const id = '0193a7c2-5f3e-7cc1-9f1a-3b2c1d0e9f8a';
  const filedAt = new Date(Date.now() - 3 * 86_400_000).toISOString();
  claims.set(id, {
    claimId: id, travellerId: USER.id, flightNumber: 'KL1234', flightDate: filedAt.slice(0, 10), origin: 'AMS', destination: 'ATH',
    disruptionType: 'Delay', status: 'SubmittedToAirline', compensationAmount: 400, currency: 'EUR',
    eligibilityReason: 'Arrival delay of 210 min ≥ 3 h.', distanceKm: 2163, version: 3, filedAt, updatedAt: filedAt,
  });
  append(id, 'ClaimFiled', { flightNumber: 'KL1234', origin: 'AMS', destination: 'ATH' });
  append(id, 'EligibilityAssessed', { isEligible: true, compensationAmount: 400, reason: 'Arrival delay of 210 min ≥ 3 h.' });
  append(id, 'ClaimSubmittedToAirline', {});
}
seed();

/** Mock "real-time" channel: the RealtimeProvider listens for this DOM event in mock mode instead of SignalR. */
export const MOCK_NOTIFICATION_EVENT = 'reroute:mock-notification';

function pushNotification(category: string, title: string, body: string, link: string | null): void {
  window.dispatchEvent(new CustomEvent(MOCK_NOTIFICATION_EVENT, {
    detail: { id: crypto.randomUUID(), category, title, body, link, sentAt: new Date().toISOString() },
  }));
}

export const handlers = [
  http.get('/bff/user', () => HttpResponse.json(USER)),

  http.get('/api/claims', async ({ request }) => {
    await delay(250);
    const url = new URL(request.url);
    const status = url.searchParams.get('status');
    const page = Number(url.searchParams.get('page') ?? 1);
    const pageSize = Number(url.searchParams.get('pageSize') ?? 20);
    const all = [...claims.values()].filter((c) => !status || c.status === status).sort((a, b) => b.filedAt.localeCompare(a.filedAt));
    return HttpResponse.json({
      items: all.slice((page - 1) * pageSize, page * pageSize).map(({ claimId, flightNumber, flightDate, status: s, compensationAmount, currency, filedAt }) =>
        ({ claimId, flightNumber, flightDate, status: s, compensationAmount, currency, filedAt })),
      page, pageSize, totalCount: all.length,
    });
  }),

  http.get('/api/claims/:id', async ({ params }) => {
    await delay(150);
    const claim = claims.get(String(params['id']));
    return claim ? HttpResponse.json(claim, { headers: { ETag: `"${claim.version}"` } }) : HttpResponse.json({ title: 'Claim not found.' }, { status: 404 });
  }),

  http.get('/api/claims/:id/timeline', ({ params }) => HttpResponse.json(events.get(String(params['id'])) ?? [])),

  http.post('/api/claims', async ({ request }) => {
    await delay(400);
    const body = (await request.json()) as FileBody;
    const errors: Record<string, string[]> = {};
    if (!/^[A-Z0-9]{2}\d{1,4}[A-Z]?$/.test(body.flightNumber.replace(/\s/g, '').toUpperCase())) errors['FlightNumber'] = ['Use a flight number like KL1234.'];
    if (!AIRPORTS[body.origin]) errors['Origin'] = [`Mock mode knows: ${Object.keys(AIRPORTS).join(', ')}.`];
    if (!AIRPORTS[body.destination]) errors['Destination'] = [`Mock mode knows: ${Object.keys(AIRPORTS).join(', ')}.`];
    else if (body.origin === body.destination) errors['Destination'] = ['Destination must differ from origin.'];
    if (!body.flightDate) errors['FlightDate'] = ['Pick the flight date.'];
    if (Object.keys(errors).length > 0) return validationProblem(errors);

    const km = distanceKm(body.origin, body.destination);
    const decision = assess(body, km);
    const now = new Date().toISOString();
    const claimId = crypto.randomUUID();
    claims.set(claimId, {
      claimId, travellerId: USER.id, flightNumber: body.flightNumber.replace(/\s/g, '').toUpperCase(), flightDate: body.flightDate,
      origin: body.origin, destination: body.destination, disruptionType: body.disruptionType,
      status: decision.eligible ? 'Eligible' : 'Ineligible', compensationAmount: decision.amount, currency: 'EUR',
      eligibilityReason: decision.reason, distanceKm: km, version: 2, filedAt: now, updatedAt: now,
    });
    append(claimId, 'ClaimFiled', { flightNumber: body.flightNumber, origin: body.origin, destination: body.destination });
    append(claimId, 'EligibilityAssessed', { isEligible: decision.eligible, compensationAmount: decision.amount, reason: decision.reason });
    return HttpResponse.json({ claimId, isEligible: decision.eligible, compensationAmount: decision.amount, currency: 'EUR', reason: decision.reason }, { status: 201 });
  }),

  http.post('/api/claims/:id/submit', async ({ params, request }) => {
    await delay(300);
    const claim = claims.get(String(params['id']));
    if (!claim) return HttpResponse.json({ title: 'Claim not found.' }, { status: 404 });
    if (request.headers.get('If-Match') !== `"${claim.version}"`)
      return HttpResponse.json({ title: 'Claim was modified. Reload and retry.' }, { status: 409 });
    if (claim.status !== 'Eligible')
      return HttpResponse.json({ title: `Cannot submit a claim in status ${claim.status}.` }, { status: 409 });
    claim.status = 'SubmittedToAirline';
    claim.version += 1;
    claim.updatedAt = new Date().toISOString();
    append(claim.claimId, 'ClaimSubmittedToAirline', {});
    // Simulate the airline answering a few seconds later → real-time toast.
    setTimeout(() => {
      claim.status = 'AcceptedByAirline';
      claim.version += 1;
      append(claim.claimId, 'AirlineAcceptedClaim', { airlineReference: 'MOCK-REF-1' });
      pushNotification('claim', 'Good news: the airline accepted your claim', 'Your compensation is on its way.', `/claims/${claim.claimId}`);
    }, 4000);
    return new HttpResponse(null, { status: 204 });
  }),

  http.post('/api/flights/watch', async ({ request }) => {
    const body = (await request.json()) as { flightNumber: string; flightDate: string };
    const flightKey = `${body.flightNumber.toUpperCase().replace(/\s/g, '')}:${body.flightDate}`;
    // Simulate Kafka → RabbitMQ → SignalR: a disruption arrives a few seconds after you start watching.
    setTimeout(() => pushNotification('disruption', `${body.flightNumber.toUpperCase()} has been cancelled`,
      'You may be entitled to up to €600 under EU261. Tap to see your options.', `/disruptions/${flightKey}`), 3000);
    return HttpResponse.json({ flightKey }, { status: 202 });
  }),

  http.post('/api/copilot/ask', async ({ request }) => {
    const { question } = (await request.json()) as { question: string };
    const answer = `(Mock answer) For "${question}": under EU261 you can claim €250–€600 when you arrive 3+ hours late or your flight is cancelled with less than 14 days' notice, unless the cause was extraordinary [1]. The amount depends on flight distance [2]. Next step: file a claim on the Claims page.`;
    const encoder = new TextEncoder();
    const stream = new ReadableStream({
      async start(controller) {
        const send = (event: string, data: unknown) => controller.enqueue(encoder.encode(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`));
        send('citations', [
          { number: 1, title: 'EU261 passenger rights guide', section: 'Article 5 — Cancellation', score: 0.82 },
          { number: 2, title: 'EU261 passenger rights guide', section: 'Article 7 — Compensation amounts', score: 0.77 },
        ]);
        for (const word of answer.split(' ')) {
          await delay(35);
          send('delta', `${word} `);
        }
        send('done', '');
        controller.close();
      },
    });
    return new HttpResponse(stream, { headers: { 'Content-Type': 'text/event-stream' } });
  }),

  http.post('/bff/logout', () => HttpResponse.json({})),
];
