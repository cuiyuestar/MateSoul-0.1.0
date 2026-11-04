import assert from "node:assert/strict";
import type { AddressInfo } from "node:net";
import test from "node:test";
import { createApp } from "../src/app.js";
import { createConversationRequestSchema } from "../src/contracts.js";

test("HTTP gateway validates requests and returns the stable Unity contract", async () => {
  const events: Array<{ event: string; fields: Record<string, unknown> }> = [];
  const app = createApp({
    handler: {
      complete: async () => ({
        reply: {
          status: "ok",
          reply_text: "你好呀。",
          response_emotion: "happy",
          action: { id: "none", target_id: null },
          mood_signals: [],
        },
        modelCalls: 1,
        toolCalls: [],
      }),
    },
    spontaneousReplyHandler: {
      complete: async () => ({
        reply: {
          status: "ok",
          reply_text: "好香呀，你是在煎牛排吗？",
          response_emotion: "happy",
        },
        modelCalls: 1,
      }),
    },
    requestSchema: createConversationRequestSchema(16),
    requestBodyLimit: "128kb",
    clientToken: null,
    toolNames: ["web_search", "web_fetch"],
    log: (event, fields) => events.push({ event, fields }),
  });
  const server = app.listen(0, "127.0.0.1");
  await new Promise<void>((resolve) => server.once("listening", resolve));
  const { port } = server.address() as AddressInfo;
  try {
    const health = await fetch(`http://127.0.0.1:${port}/health`);
    assert.equal(health.status, 200);
    assert.deepEqual((await health.json() as { mcp_tools: string[] }).mcp_tools, [
      "web_search",
      "web_fetch",
    ]);

    const invalid = await fetch(`http://127.0.0.1:${port}/v1/conversations/reply`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ input: "missing contract fields" }),
    });
    assert.equal(invalid.status, 400);
    const rejected = events.find((entry) => entry.event === "AI_REQUEST_REJECTED");
    assert.ok(rejected);
    assert.ok(Array.isArray(rejected.fields.issues));

    const response = await fetch(`http://127.0.0.1:${port}/v1/conversations/reply`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        session_id: "session-1",
        request_id: "request-1",
        input: "你好",
        history: [],
        actions: [],
        targets: [],
        moods: [],
        persona: {
          schema_version: 1,
          character_id: "lengyue",
          name: "冷月",
          age: 16,
          gender: "female",
          height_cm: 155,
          personality: [
            { id: "extraversion", display_name: "外向性", option_id: "balanced", option_name: "平衡", description: "自然交流。" },
            { id: "conscientiousness", display_name: "尽责性", option_id: "flexible", option_name: "灵活", description: "灵活安排。" },
            { id: "openness", display_name: "开放性", option_id: "pragmatic", option_name: "务实", description: "关注实际。" },
            { id: "agreeableness", display_name: "亲和性", option_id: "warm", option_name: "温暖", description: "温暖真实。" },
            { id: "emotional_stability", display_name: "情绪稳定性", option_id: "natural", option_name: "自然", description: "自然表达情绪。" },
          ],
        },
        runtime: {
          schema_version: 1,
          revision: 1,
          appearance: { outfit_id: "lavender-casual", display_name: "薰衣草常服" },
          mood: { id: "neutral", level: "none" },
          posture: "standing",
          actions: [],
          recent_events: [],
        },
      }),
    });
    assert.equal(response.status, 200);
    assert.equal(response.headers.get("x-request-id"), "request-1");
    assert.deepEqual(await response.json(), {
      status: "ok",
      reply_text: "你好呀。",
      response_emotion: "happy",
      action: { id: "none", target_id: null },
      mood_signals: [],
    });

    const systemReply = await fetch(
      `http://127.0.0.1:${port}/v1/system-replies/generate`,
      {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          request_id: "system-request-1",
          trigger_id: "cooking.raw-steak.aroma:item-1",
          event_description: "空气中出现了煎牛排香气。",
          content_requirements: ["闻到香味并询问玩家是否在煎牛排。"],
          rules: ["只说一句自然中文。"],
          persona: {
            schema_version: 1,
            character_id: "lengyue",
            name: "冷月",
            age: 16,
            gender: "female",
            height_cm: 155,
            personality: [
              { id: "extraversion", display_name: "外向性", option_id: "balanced", option_name: "平衡", description: "自然交流。" },
              { id: "conscientiousness", display_name: "尽责性", option_id: "flexible", option_name: "灵活", description: "灵活安排。" },
              { id: "openness", display_name: "开放性", option_id: "pragmatic", option_name: "务实", description: "关注实际。" },
              { id: "agreeableness", display_name: "亲和性", option_id: "warm", option_name: "温暖", description: "温暖真实。" },
              { id: "emotional_stability", display_name: "情绪稳定性", option_id: "natural", option_name: "自然", description: "自然表达情绪。" },
            ],
          },
        }),
      },
    );
    assert.equal(systemReply.status, 200);
    assert.equal(systemReply.headers.get("x-request-id"), "system-request-1");
    assert.deepEqual(await systemReply.json(), {
      status: "ok",
      reply_text: "好香呀，你是在煎牛排吗？",
      response_emotion: "happy",
    });
    assert.ok(events.some((entry) =>
      entry.event === "SYSTEM_REPLY_REQUEST_COMPLETED"));
  } finally {
    await new Promise<void>((resolve, reject) =>
      server.close((error) => error === undefined ? resolve() : reject(error)));
  }
});

test("configured client token protects conversation calls without affecting health", async () => {
  const app = createApp({
    handler: {
      complete: async () => {
        throw new Error("must not be reached");
      },
    },
    spontaneousReplyHandler: {
      complete: async () => {
        throw new Error("must not be reached");
      },
    },
    requestSchema: createConversationRequestSchema(16),
    requestBodyLimit: "128kb",
    clientToken: "test-token",
    toolNames: [],
    log: () => {},
  });
  const server = app.listen(0, "127.0.0.1");
  await new Promise<void>((resolve) => server.once("listening", resolve));
  const { port } = server.address() as AddressInfo;
  try {
    assert.equal((await fetch(`http://127.0.0.1:${port}/health`)).status, 200);
    const response = await fetch(`http://127.0.0.1:${port}/v1/conversations/reply`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: "{}",
    });
    assert.equal(response.status, 401);
  } finally {
    await new Promise<void>((resolve, reject) =>
      server.close((error) => error === undefined ? resolve() : reject(error)));
  }
});
