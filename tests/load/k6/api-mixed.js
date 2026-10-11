// Mixed read/write API load for the Personal Platform (k6). Every virtual user signs in as its own account, so
// data isolation, per-user query filters and indexes are exercised the way real traffic would.
//
//   k6 run -e BASE_URL=http://127.0.0.1:18081 -e USERS_FILE=users.txt -e PASSWORD='load test password 2026' api-mixed.js
//
// USERS_FILE holds one pre-confirmed e-mail address per line (see ../provision-users.sh).
import http from 'k6/http';
import { check, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import exec from 'k6/execution';

const BASE = __ENV.BASE_URL || 'http://127.0.0.1:18081';
const PASSWORD = __ENV.PASSWORD || 'load test password 2026';
const users = new SharedArray('users', () =>
  open(__ENV.USERS_FILE || 'users.txt').split('\n').map(s => s.trim()).filter(Boolean));

export const options = {
  scenarios: {
    browse: {
      executor: 'ramping-vus',
      exec: 'browse',
      startVUs: 0,
      stages: [
        { duration: '20s', target: 25 },
        { duration: '60s', target: 25 },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '5s',
    },
    write: {
      executor: 'constant-arrival-rate',
      exec: 'write',
      rate: 10,
      timeUnit: '1s',
      duration: '80s',
      startTime: '10s',
      preAllocatedVUs: 10,
      maxVUs: 30,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{kind:read}': ['p(95)<300', 'p(99)<800'],
    'http_req_duration{kind:write}': ['p(95)<500', 'p(99)<1000'],
    checks: ['rate>0.99'],
  },
};

export function setup() {
  // Access tokens live 5 minutes, longer than this test; one sign-in per account is enough.
  const tokens = users.map(email => {
    const res = http.post(`${BASE}/api/v1/auth/login`, JSON.stringify({ email, password: PASSWORD }),
      { headers: { 'Content-Type': 'application/json' }, tags: { kind: 'auth' } });
    if (res.status !== 200) {
      throw new Error(`login failed for ${email}: ${res.status}`);
    }
    return res.json('accessToken');
  });
  return { tokens };
}

function auth(data) {
  const token = data.tokens[exec.vu.idInTest % data.tokens.length];
  return { 'Authorization': `Bearer ${token}`, 'Content-Type': 'application/json' };
}

function get(path, headers) {
  const res = http.get(`${BASE}${path}`, { headers, tags: { kind: 'read', name: path.split('?')[0] } });
  check(res, { [`GET ${path.split('?')[0]} 200`]: r => r.status === 200 });
  return res;
}

export function browse(data) {
  const headers = auth(data);
  get('/api/v1/dashboard', headers);
  get('/api/v1/tasks?view=Today&page=1&pageSize=50', headers);
  get('/api/v1/habits', headers);
  get('/api/v1/finance/accounts', headers);
  get('/api/v1/finance/transactions?page=1&pageSize=50', headers);
  get('/api/v1/notes?search=idea&page=1&pageSize=20', headers);
  sleep(1);
}

let account = null;

export function write(data) {
  const headers = auth(data);
  const today = new Date().toISOString().slice(0, 10);

  const task = http.post(`${BASE}/api/v1/tasks`, JSON.stringify({
    title: `Load task ${exec.scenario.iterationInTest}`, description: null, priority: 'Medium', dueDate: today, dueTime: null,
    reminderAt: null, categoryId: null, tagIds: null, subtasks: null, recurrence: null,
  }), { headers, tags: { kind: 'write', name: 'POST /tasks' } });
  check(task, { 'task created': r => r.status === 201 });
  if (task.status === 201) {
    const done = http.post(`${BASE}/api/v1/tasks/${task.json('id')}/status`, JSON.stringify({ status: 'Done' }),
      { headers, tags: { kind: 'write', name: 'POST /tasks/{id}/status' } });
    check(done, { 'task completed': r => r.status === 200 });
  }

  if (account === null) {
    const created = http.post(`${BASE}/api/v1/finance/accounts`, JSON.stringify({
      name: `Load ${exec.vu.idInTest}`, type: 'Checking', currencyCode: 'EUR', openingBalance: 1000, openingDate: null,
    }), { headers, tags: { kind: 'write', name: 'POST /finance/accounts' } });
    check(created, { 'account created': r => r.status === 201 });
    account = created.status === 201 ? created.json('id') : null;
  }
  if (account !== null) {
    const key = `load-${exec.vu.idInTest}-${exec.scenario.iterationInTest}-${Date.now()}`;
    const tx = http.post(`${BASE}/api/v1/finance/transactions`, JSON.stringify({
      kind: 'Expense', occurredOn: today, description: 'Load expense', accountId: account, amount: 1.25,
      categoryId: null, destinationAccountId: null, destinationAmount: null,
    }), { headers: { ...headers, 'Idempotency-Key': key }, tags: { kind: 'write', name: 'POST /finance/transactions' } });
    check(tx, { 'expense created': r => r.status === 201 });
  }

  const note = http.post(`${BASE}/api/v1/notes`, JSON.stringify({
    title: 'Load idea', content: 'An idea written during the load test', tagIds: null, isPinned: false,
  }), { headers, tags: { kind: 'write', name: 'POST /notes' } });
  check(note, { 'note created': r => r.status === 201 });
}
