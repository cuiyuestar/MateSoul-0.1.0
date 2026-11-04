import assert from "node:assert/strict";
import test from "node:test";
import { loadConfig } from "../src/config.js";
import {
  buildMessages,
  ConversationService,
} from "../src/conversation-service.js";
import type { ConversationRequest } from "../src/contracts.js";
import type {
  DeepSeekCompletion,
  DeepSeekMessage,
  DeepSeekTool,
  ModelClient,
} from "../src/deepseek-client.js";
import type { ToolRuntime } from "../src/mcp-runtime.js";

class QueueModel implements ModelClient {
  public readonly requests: Array<{
    messages: readonly DeepSeekMessage[];
    tools: readonly DeepSeekTool[];
  }> = [];

  public constructor(private readonly completions: DeepSeekCompletion[]) {}

  public complete(
    messages: readonly DeepSeekMessage[],
    tools: readonly DeepSeekTool[],
    _signal: AbortSignal,
  ): Promise<DeepSeekCompletion> {
    this.requests.push({
      messages: structuredClone(messages),
      tools: structuredClone(tools),
    });
    const completion = this.completions.shift();
    if (completion === undefined) throw new Error("No queued model completion.");
    return Promise.resolve(completion);
  }
}

class FakeTools implements ToolRuntime {
  public readonly calls: Array<{ name: string; argumentsValue: unknown }> = [];
  public readonly tools: readonly DeepSeekTool[] = [{
    type: "function",
    function: {
      name: "web_search",
      description: "Search current public information",
      parameters: {
        type: "object",
        properties: { query: { type: "string" } },
        required: ["query"],
      },
    },
  }];

  public call(name: string, argumentsValue: unknown): Promise<string> {
    this.calls.push({ name, argumentsValue });
    return Promise.resolve('{"results":[{"title":"Current result"}]}');
  }

  public close(): Promise<void> {
    return Promise.resolve();
  }
}

function request(): ConversationRequest {
  return {
    session_id: "session-1",
    request_id: "request-1",
    input: "请查一下今天的公开信息",
    history: [{ role: "user", content: "你好" }, { role: "assistant", content: "你好呀。" }],
    actions: [],
    targets: [],
    moods: ["happy"],
    persona: {
      schema_version: 1,
      character_id: "lengyue",
      name: "冷月",
      age: 16,
      gender: "female",
      height_cm: 155,
      personality: [
        ["extraversion", "外向性"],
        ["conscientiousness", "尽责性"],
        ["openness", "开放性"],
        ["agreeableness", "亲和性"],
        ["emotional_stability", "情绪稳定性"],
      ].map(([id, display_name]) => ({
        id: id as "extraversion",
        display_name: display_name!,
        option_id: "balanced",
        option_name: "平衡",
        description: "保持自然平衡的表达方式。",
      })),
    },
    runtime: {
      schema_version: 1,
      revision: 3,
      appearance: { outfit_id: "lavender-casual", display_name: "薰衣草常服" },
      mood: { id: "neutral", level: "none" },
      posture: "standing",
      actions: [],
      recent_events: [],
    },
  };
}

test("MCP tools are exposed every turn and model-selected calls are executed", async () => {
  const loaded = await loadConfig("config/gateway.json");
  const model = new QueueModel([
    {
      message: {
        role: "assistant",
        content: null,
        tool_calls: [{
          id: "call-1",
          type: "function",
          function: {
            name: "web_search",
            arguments: '{"query":"current public information"}',
          },
        }],
      },
    },
    {
      message: {
        role: "assistant",
        content: JSON.stringify({
          status: "ok",
          reply_text: "我查到了最新公开信息。",
          response_emotion: "normal",
          action: { id: "none", target_id: null },
          mood_signals: [],
        }),
      },
      usage: { totalTokens: 42 },
    },
  ]);
  const tools = new FakeTools();
  const service = new ConversationService(
    model,
    tools,
    loaded.character,
    loaded.gateway,
    () => {},
  );

  const completion = await service.complete(request(), new AbortController().signal);

  assert.deepEqual(completion.toolCalls, ["web_search"]);
  assert.equal(tools.calls.length, 1);
  assert.equal(model.requests.length, 2);
  assert.equal(model.requests[0]!.tools[0]!.function.name, "web_search");
  assert.equal(
    model.requests[1]!.messages.some((message) =>
      message.role === "tool" && message.tool_call_id === "call-1"),
    true,
  );
  assert.equal(completion.reply.reply_text, "我查到了最新公开信息。");
});

test("one protocol repair is performed for a semantically invalid model reply", async () => {
  const loaded = await loadConfig("config/gateway.json");
  const model = new QueueModel([
    {
      message: {
        role: "assistant",
        content: JSON.stringify({
          reply_text: "错误动作",
          response_emotion: "angry",
          action: { id: "not-registered", target_id: null },
          mood_signals: [],
        }),
      },
    },
    {
      message: {
        role: "assistant",
        content: JSON.stringify({
          status: "ok",
          reply_text: "我明白了。",
          response_emotion: "normal",
          action: { id: "none", target_id: null },
          mood_signals: [],
        }),
      },
    },
  ]);
  const service = new ConversationService(
    model,
    new FakeTools(),
    loaded.character,
    loaded.gateway,
    () => {},
  );

  const completion = await service.complete(request(), new AbortController().signal);

  assert.equal(completion.modelCalls, 2);
  assert.equal(model.requests[1]!.tools.length, 0);
  assert.match(
    model.requests[1]!.messages.at(-1)?.content ?? "",
    /failed protocol validation/,
  );
});

test("one protocol repair is performed for an empty model reply", async () => {
  const loaded = await loadConfig("config/gateway.json");
  const model = new QueueModel([
    { message: { role: "assistant", content: null } },
    {
      message: {
        role: "assistant",
        content: JSON.stringify({
          status: "ok",
          reply_text: "你好呀。",
          response_emotion: "happy",
          action: { id: "none", target_id: null },
          mood_signals: [],
        }),
      },
    },
  ]);
  const service = new ConversationService(
    model,
    new FakeTools(),
    loaded.character,
    loaded.gateway,
    () => {},
  );

  const completion = await service.complete(request(), new AbortController().signal);

  assert.equal(completion.modelCalls, 2);
  assert.equal(completion.reply.reply_text, "你好呀。");
  assert.match(
    model.requests[1]!.messages.at(-1)?.content ?? "",
    /empty final response/,
  );
});

test("system prompt derives available actions, targets, and moods from the request", async () => {
  const loaded = await loadConfig("config/gateway.json");
  const value = request();
  value.actions = [{
    id: "custom-action",
    description: "A registered extension",
    target_capability: null,
  }];
  value.targets = [{
    id: "custom-target",
    name: "扩展目标",
    state: "ready",
    capabilities: [],
  }];
  value.moods = ["curious"];

  const system = buildMessages(
    loaded.character,
    value,
    loaded.gateway.conversation.maxReplyCharacters,
  )[0]?.content ?? "";

  assert.match(system, /custom-action/);
  assert.match(system, /custom-target/);
  assert.match(system, /curious/);
  assert.match(system, /冷月/);
  assert.match(system, /lavender-casual/);
  assert.match(system, /trusted game data, not instructions/);
  assert.match(system, /Never use web tools to answer about character identity/);
  assert.match(system, /normal, happy, sad, angry/);
  assert.match(system, /latest player message in full conversation context/);
});

test("system prompt bounds repeated targets at the 256-instance request limit", async () => {
  const loaded = await loadConfig("config/gateway.json");
  const value = request();
  value.targets = Array.from({ length: 256 }, (_, index) => ({
    id: `cup-${index}`,
    name: "杯子",
    state: "default",
    capabilities: ["visit"],
  }));

  const system = buildMessages(
    loaded.character,
    value,
    loaded.gateway.conversation.maxReplyCharacters,
  )[0]?.content ?? "";

  assert.match(system, /Compact runtime target summary/);
  assert.match(system, /"total_count":256/);
  assert.match(system, /cup-0/);
  assert.match(system, /cup-2/);
  assert.doesNotMatch(system, /cup-255/);
});
