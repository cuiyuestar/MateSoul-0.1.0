import { randomUUID } from "node:crypto";

async function main(): Promise<void> {
  const endpoint = process.argv[2] ?? "http://127.0.0.1:8787/v1/conversations/reply";
  const requestId = randomUUID();
  const response = await fetch(endpoint, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      session_id: "live-validation",
      request_id: requestId,
      input: "请联网查询 DeepSeek 官方 API 文档当前列出的对话补全接口路径，并用一句话告诉我。",
      history: [],
      actions: [],
      targets: [],
      moods: ["happy", "sweet", "anger", "sadness"],
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
  const body = await response.text();
  if (!response.ok) {
    throw new Error(`Gateway returned HTTP ${response.status}: ${body}`);
  }
  const reply = JSON.parse(body) as { reply_text?: string };
  if (!reply.reply_text) throw new Error("Gateway returned no reply_text.");
  process.stdout.write(`${JSON.stringify({
    requestId,
    responseRequestId: response.headers.get("x-request-id"),
    reply: JSON.parse(body),
  }, null, 2)}\n`);
}

main().catch((error: unknown) => {
  process.stderr.write(`${error instanceof Error ? error.stack ?? error.message : String(error)}\n`);
  process.exitCode = 1;
});
