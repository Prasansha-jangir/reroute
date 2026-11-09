// k6 load test for the claims read path (cached query side).
//   k6 run -e BASE_URL=http://localhost:5104 -e TOKEN=<jwt> tests/load/claims-read.js
// Publish the resulting p95/p99 in the README — measured numbers beat adjectives.
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  scenarios: {
    steady: { executor: 'constant-arrival-rate', rate: 500, timeUnit: '1s', duration: '2m', preAllocatedVUs: 100, maxVUs: 400 },
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],          // 99.9% availability SLO
    http_req_duration: ['p(99)<300'],         // p99 latency SLO
  },
};

const headers = { Authorization: `Bearer ${__ENV.TOKEN ?? ''}` };

export default function () {
  const res = http.get(`${__ENV.BASE_URL}/api/claims?page=1&pageSize=20`, { headers });
  check(res, { 'status is 200': (r) => r.status === 200 });
}
