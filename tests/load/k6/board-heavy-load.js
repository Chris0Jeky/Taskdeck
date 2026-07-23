import http from "k6/http";
import { check, fail, sleep } from "k6";

const baseUrl = __ENV.K6_BASE_URL || "http://127.0.0.1:5000/api";
const vus = Number(__ENV.K6_VUS || "20");
const duration = __ENV.K6_DURATION || "90s";
const userPool = Number(__ENV.K6_USER_POOL || "6");
const thinkTimeMs = Number(__ENV.K6_THINK_TIME_MS || "200");

export const options = {
  scenarios: {
    board_heavy_api: {
      executor: "constant-vus",
      vus,
      duration,
    },
  },
  thresholds: {
    // CI gate: error rate must stay below 1% (issue #872)
    http_req_failed: ["rate<0.01"],
    checks: ["rate>0.99"],
    // Aggregate CI gate: p95 must stay below 2000ms (issue #872).
    // The measured 2000ms SQLite board-write capacity at 20 VUs remains the
    // check-k6-thresholds near-capacity warning level (informational only).
    // Tail thresholds (p99 / board-write p95) calibrated 2026-07-23 against same-code
    // nightly variance on shared 2-core runners (main @ 6ff32594, 5 nights: 2 pass /
    // 3 fail with zero code change). Observed same-code range: global p99 2.0-3.0s,
    // board-write p95 2.0-3.0s (median ~12ms -- heavy-tailed SQLite write convoy,
    // tracked as #1446). Gates sit at ~1.5-1.7x the 3.0s worst observed-good
    // (board-write p95 4500 = 1.5x; global p99 5000 = 1.67x) so they catch
    // order-of-magnitude regressions instead of runner luck; evidence in #1445.
    // Known trade: a sustained tail regression landing inside (3.0s, gate) produces no
    // FAILING signal -- board-write only re-triggers the always-on >=2000ms capacity
    // warning, and the global p99 band is signal-free. Tail-trend visibility is #1446's
    // problem, not this gate's.
    // Median/read-path thresholds stay tight on purpose. Tighten again if the
    // gate moves to consistent hardware.
    http_req_duration: ["p(95)<2000", "p(99)<5000"],
    "http_req_duration{workload:board-read}": ["p(95)<900"],
    "http_req_duration{workload:board-write}": ["p(95)<4500"],
  },
  summaryTrendStats: ["avg", "min", "med", "p(90)", "p(95)", "p(99)", "max"],
};

function jsonRequestOptions(token, operation, workload) {
  return {
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    tags: {
      operation,
      workload,
    },
  };
}

function authRequestOptions(token, operation, workload) {
  return {
    headers: {
      Authorization: `Bearer ${token}`,
    },
    tags: {
      operation,
      workload,
    },
  };
}

function requestBody(data) {
  return JSON.stringify(data);
}

function parseJson(response, operationName) {
  const parsed = response.json();
  if (!parsed) {
    fail(`${operationName} returned an empty payload.`);
  }

  return parsed;
}

function assertOk(response, operationName, expectedStatuses) {
  const statusOk = expectedStatuses.indexOf(response.status) >= 0;
  const checkOk = check(response, {
    [`${operationName} status is expected`]: () => statusOk,
  });

  if (!checkOk) {
    const snippet = response.body ? response.body.slice(0, 300) : "<no body>";
    fail(`${operationName} failed with status ${response.status}. Body snippet: ${snippet}`);
  }
}

function registerActor(index) {
  const unique = `${Date.now()}-${index}`;
  const username = `k6-load-${unique}`;
  const email = `${username}@taskdeck.local`;
  const password = "LoadHarness123!";

  const response = http.post(
    `${baseUrl}/auth/register`,
    requestBody({ username, email, password }),
    { headers: { "Content-Type": "application/json" }, tags: { operation: "auth.register", workload: "board-write" } },
  );

  assertOk(response, "auth.register", [200]);
  return parseJson(response, "auth.register");
}

function createBoard(token, actorIndex) {
  const response = http.post(
    `${baseUrl}/boards`,
    requestBody({ name: `Load Board ${actorIndex}` }),
    jsonRequestOptions(token, "boards.create", "board-write"),
  );

  assertOk(response, "boards.create", [201]);
  return parseJson(response, "boards.create");
}

function createColumn(token, boardId, actorIndex, columnIndex) {
  const response = http.post(
    `${baseUrl}/boards/${boardId}/columns`,
    requestBody({
      boardId,
      name: `Load Column ${actorIndex}-${columnIndex}`,
      position: columnIndex,
    }),
    jsonRequestOptions(token, "columns.create", "board-write"),
  );

  assertOk(response, "columns.create", [201]);
  return parseJson(response, "columns.create");
}

function createCard(token, boardId, columnId, actorIndex, cardIndex) {
  const response = http.post(
    `${baseUrl}/boards/${boardId}/cards`,
    requestBody({
      boardId,
      columnId,
      title: `Load Card ${actorIndex}-${cardIndex}`,
      description: "seeded for load harness",
    }),
    jsonRequestOptions(token, "cards.create", "board-write"),
  );

  assertOk(response, "cards.create", [201]);
  return parseJson(response, "cards.create");
}

function moveCard(token, boardId, cardId, targetColumnId) {
  const response = http.post(
    `${baseUrl}/boards/${boardId}/cards/${cardId}/move`,
    requestBody({
      targetColumnId,
      targetPosition: 0,
    }),
    jsonRequestOptions(token, "cards.move", "board-write"),
  );

  assertOk(response, "cards.move", [200]);
}

function runBoardReadSlice(token, boardId) {
  const listBoards = http.get(
    `${baseUrl}/boards?includeArchived=false`,
    authRequestOptions(token, "boards.list", "board-read"),
  );
  assertOk(listBoards, "boards.list", [200]);

  const boardDetail = http.get(
    `${baseUrl}/boards/${boardId}`,
    authRequestOptions(token, "boards.detail", "board-read"),
  );
  assertOk(boardDetail, "boards.detail", [200]);

  const boardColumns = http.get(
    `${baseUrl}/boards/${boardId}/columns`,
    authRequestOptions(token, "columns.list", "board-read"),
  );
  assertOk(boardColumns, "columns.list", [200]);

  const boardCards = http.get(
    `${baseUrl}/boards/${boardId}/cards`,
    authRequestOptions(token, "cards.list", "board-read"),
  );
  assertOk(boardCards, "cards.list", [200]);
}

export function setup() {
  const actors = [];
  for (let actorIndex = 0; actorIndex < userPool; actorIndex += 1) {
    const auth = registerActor(actorIndex);
    const token = auth.token;
    const board = createBoard(token, actorIndex);
    const boardId = board.id;

    const columns = [];
    for (let columnIndex = 0; columnIndex < 3; columnIndex += 1) {
      columns.push(createColumn(token, boardId, actorIndex, columnIndex));
    }

    const cards = [];
    for (let cardIndex = 0; cardIndex < 9; cardIndex += 1) {
      const column = columns[cardIndex % columns.length];
      cards.push(createCard(token, boardId, column.id, actorIndex, cardIndex));
    }

    actors.push({
      token,
      boardId,
      columnIds: columns.map((column) => column.id),
      cardIds: cards.map((card) => card.id),
    });
  }

  return { actors };
}

export default function (data) {
  const actor = data.actors[(__VU - 1) % data.actors.length];
  const token = actor.token;
  const boardId = actor.boardId;

  runBoardReadSlice(token, boardId);

  const performWrite = __ITER % 3 === 0;
  if (performWrite) {
    const targetColumn = actor.columnIds[__ITER % actor.columnIds.length];
    createCard(token, boardId, targetColumn, __VU, __ITER);
  } else {
    const cardId = actor.cardIds[__ITER % actor.cardIds.length];
    const targetColumn = actor.columnIds[(__ITER + 1) % actor.columnIds.length];
    moveCard(token, boardId, cardId, targetColumn);
  }

  sleep(thinkTimeMs / 1000);
}
