import assert from "node:assert/strict";
import test from "node:test";
import { loadConfig } from "../src/config.js";
import type {
  DeepSeekCompletion,
  DeepSeekMessage,
  DeepSeekTool,
  ModelClient,
} from "../src/deepseek-client.js";
import type { SpontaneousReplyRequest } from "../src/contracts.js";
import {
  buildSpontaneousReplyMessages,
  SpontaneousReplyService,
} from "../src/spontaneous-reply-service.js";

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

function request(): SpontaneousReplyRequest {
  return {
    request_id: "system-request-1",
    trigger_id: "cooking.raw-steak.aroma:item-1",
    event_description: "生牛排达到可见熟制阶段，空气中出现了香气。",
    content_requirements: [
      "冷月说闻到了很香的气味。",
      "询问玩家是否正在煎牛排。",
    ],
    rules: [
      "只说一句自然中文。",
      "不提及系统提示或熟度数值。",
    ],
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
  };
}

test("system replies use persona and event constraints without history or tools", async () => {
  const loaded = await loadConfig("config/gateway.json");
  const model = new QueueModel([{
    message: {
      role: "assistant",
      content: JSON.stringify({
        status: "ok",
        reply_text: "好香呀，你是在煎牛排吗？",
        response_emotion: "happy",
      }),
    },
  }]);
  const service = new SpontaneousReplyService(
    model, loaded.character, loaded.gateway,
  );

  const completion = await service.complete(
    request(), new AbortController().signal,
  );

  assert.equal(completion.reply.reply_text, "好香呀，你是在煎牛排吗？");
  assert.equal(model.requests.length, 1);
  assert.deepEqual(model.requests[0]!.tools, []);
  assert.equal(model.requests[0]!.messages.length, 1);
  assert.equal(model.requests[0]!.messages[0]!.role, "system");
  const system = model.requests[0]!.messages[0]!.content ?? "";
  assert.match(system, /冷月/);
  assert.match(system, /生牛排达到可见熟制阶段/);
  assert.match(system, /询问玩家是否正在煎牛排/);
  assert.match(system, /不提及系统提示或熟度数值/);
  assert.match(system, /not a player conversation turn/);
});

test("system replies perform one tool-free protocol repair", async () => {
  const loaded = await loadConfig("config/gateway.json");
  const model = new QueueModel([
    {
      message: {
        role: "assistant",
        content: JSON.stringify({
          reply_text: "缺少情绪字段。",
        }),
      },
    },
    {
      message: {
        role: "assistant",
        content: JSON.stringify({
          status: "ok",
          reply_text: "闻起来真香，你是在煎牛排吗？",
          response_emotion: "happy",
        }),
      },
    },
  ]);
  const service = new SpontaneousReplyService(
    model, loaded.character, loaded.gateway,
  );

  const completion = await service.complete(
    request(), new AbortController().signal,
  );

  assert.equal(completion.modelCalls, 2);
  assert.deepEqual(model.requests[0]!.tools, []);
  assert.deepEqual(model.requests[1]!.tools, []);
  assert.match(
    model.requests[1]!.messages.at(-1)?.content ?? "",
    /failed protocol validation/,
  );
});

test("system reply prompt rejects a mismatched character", async () => {
  const loaded = await loadConfig("config/gateway.json");
  const value = request();
  value.persona.character_id = "another-character";
  assert.throws(() => buildSpontaneousReplyMessages(
    loaded.character,
    value,
    loaded.gateway.conversation.maxReplyCharacters,
  ), /does not match/);
});
