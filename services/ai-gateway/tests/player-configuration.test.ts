import assert from "node:assert/strict";
import { createServer, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import test from "node:test";
import { createApp } from "../src/app.js";
import { createConversationRequestSchema } from "../src/contracts.js";
import { DeepSeekClient } from "../src/deepseek-client.js";
import { AIConnectionError } from "../src/player-credentials.js";

async function listen(server: Server): Promise<string> {
  server.listen(0, "127.0.0.1");
  await new Promise<void>((resolve) => server.once("listening", resolve));
  return `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
}

async function close(server: Server): Promise<void> {
  await new Promise<void>((resolve, reject) =>
    server.close((error) => error ? reject(error) : resolve()));
}

test("configuration test validates the real upstream and isolates concurrent player credentials", async () => {
  const received: Array<{ authorization: string | undefined; model: string }> = [];
  const upstream = createServer(async (request, response) => {
    let body = "";
    for await (const chunk of request) body += chunk;
    received.push({
      authorization: request.headers.authorization,
      model: (JSON.parse(body) as { model: string }).model,
    });
    response.setHeader("content-type", "application/json");
    if (request.headers.authorization === "Bearer invalid-test-key") {
      response.writeHead(401);
      response.end(JSON.stringify({ error: "invalid-test-key must not escape upstream" }));
    } else {
      response.end(JSON.stringify({
        choices: [{ message: { role: "assistant", content: '{"status":"ok"}' } }],
      }));
    }
  });
  const upstreamAddress = await listen(upstream);
  const logs: unknown[] = [];
  const app = createApp({
    handler: { complete: async () => { throw new Error("unexpected conversation"); } },
    spontaneousReplyHandler: { complete: async () => { throw new Error("unexpected system reply"); } },
    requestSchema: createConversationRequestSchema(16),
    requestBodyLimit: "128kb", clientToken: null, toolNames: [],
    log: (event, fields) => logs.push({ event, fields }),
    checkConfiguration: async (credentials, signal) => {
      if (!credentials)
        throw new AIConnectionError(401, "AI_CONFIGURATION_REQUIRED", "请配置 AI。");
      await new DeepSeekClient(credentials.apiKey, {
        baseUrl: upstreamAddress, model: credentials.model ?? "default-model",
        thinking: "disabled", maxTokens: 20, requestTimeoutMs: 1000, maxToolRounds: 1,
      }).complete([{ role: "user", content: "Return JSON." }], [], signal);
    },
  });
  const gateway = createServer(app);
  const address = await listen(gateway);
  const check = (key?: string, model = "test-model") => fetch(`${address}/v1/configuration/check`, {
    method: "POST", headers: key === undefined ? {} : {
      "x-ai-api-key": key, "x-ai-model": model,
    },
  });
  try {
    const [first, second, invalid, missing] = await Promise.all([
      check("player-one-key", "model-one"),
      check("player-two-key", "model-two"),
      check("invalid-test-key"), check(),
    ]);
    assert.equal(first.status, 200);
    assert.deepEqual(await first.json(), { status: "ok" });
    assert.equal(second.status, 200);
    assert.equal(invalid.status, 401);
    const invalidBody = await invalid.text();
    assert.ok(invalidBody.includes("INVALID_API_KEY"));
    assert.ok(!invalidBody.includes("invalid-test-key"));
    assert.equal(missing.status, 401);
    assert.ok(received.some((value) =>
      value.authorization === "Bearer player-one-key" && value.model === "model-one"));
    assert.ok(received.some((value) =>
      value.authorization === "Bearer player-two-key" && value.model === "model-two"));
    assert.ok(!JSON.stringify(logs).includes("player-one-key"));
    const oversized = await check("x".repeat(4097));
    assert.equal(oversized.status, 400);
    assert.equal(received.length, 3, "Invalid or missing credentials must never reach the model.");
  } finally {
    await close(gateway);
    await close(upstream);
  }
});

test("configuration check is protected by the optional gateway token", async () => {
  let called = false;
  const app = createApp({
    handler: { complete: async () => { throw new Error("unexpected"); } },
    spontaneousReplyHandler: { complete: async () => { throw new Error("unexpected"); } },
    requestSchema: createConversationRequestSchema(16),
    requestBodyLimit: "128kb", clientToken: "gateway-test-token", toolNames: [], log: () => {},
    checkConfiguration: async () => { called = true; },
  });
  const server = createServer(app);
  const address = await listen(server);
  try {
    assert.equal((await fetch(`${address}/health`)).status, 200);
    assert.equal((await fetch(`${address}/v1/configuration/check`, { method: "POST" })).status, 401);
    assert.equal(called, false);
    assert.equal((await fetch(`${address}/v1/configuration/check`, {
      method: "POST", headers: { authorization: "Bearer gateway-test-token" },
    })).status, 200);
    assert.equal(called, true);
  } finally { await close(server); }
});
