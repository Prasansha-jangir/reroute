import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';

export const claimId = '0193a7c2-5f3e-7cc1-9f1a-3b2c1d0e9f8a';

export const handlers = [
  http.get('*/bff/user', () => HttpResponse.json({ id: 'u1', name: 'Test User', email: 't@example.test', roles: [] })),
  http.get('*/api/claims', () =>
    HttpResponse.json({
      items: [
        { claimId, flightNumber: 'KL1234', flightDate: '2026-10-01', status: 'Eligible', compensationAmount: 400, currency: 'EUR', filedAt: '2026-10-02T10:00:00Z' },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    })),
  http.post('*/api/claims', () =>
    HttpResponse.json(
      { type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1', title: 'One or more validation errors occurred.', status: 400, errors: { Destination: ['Destination must differ from origin.'] } },
      { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
    )),
];

export const server = setupServer(...handlers);
