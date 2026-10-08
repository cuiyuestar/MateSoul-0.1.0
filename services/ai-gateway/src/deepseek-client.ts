import { z } from "zod";
import type { GatewayConfig } from "./config.js";

const ToolCallSchema = z.object({
  id: z.string().min(1),
  type: z.literal("function"),
  function: z.object({
    name: z.string().min(1),
    arguments: z.string(),
  }).strict(),
}).passthrough();

const ChatResponseSchema = z.object({
  choices: z.array(z.object({
    message: z.object({
      role: z.literal("assistant"),
      content: z.string().nullable().optional(),
      reasoning_content: z.string().nullable().optional(),
      tool_calls: z.array(ToolCallSchema).optional(),
    }).passthrough(),
    finish_reason: z.string().nullable().optional(),
  }).passthrough()).min(1),
  usage: z.object({
    prompt_tokens: z.number().int().nonnegative().optional(),
    completion_tokens: z.number().int().nonnegative().optional(),
    total_tokens: z.number().int().nonnegative().optional(),
  }).passthrough().optional(),
}).passthrough();

export type DeepSeekToolCall = z.infer<typeof ToolCallSchema>;

export interface DeepSeekMessage {
  role: "system" | "user" | "assistant" | "tool";
  content: string | null;
  tool_call_id?: string;
  tool_calls?: DeepSeekToolCall[];
}

export interface DeepSeekTool {
  type: "function";
  function: {
    name: string;
    description: string;
    parameters: Record<string, unknown>;
  };
}

export interface DeepSeekCompletion {
  message: DeepSeekMessage;
  usage?: {
    promptTokens?: number;
    completionTokens?: number;
    totalTokens?: number;
  };
}

export interface ModelClient {
  complete(
    messages: readonly DeepSeekMessage[],
    tools: readonly DeepSeekTool[],
    signal: AbortSignal,
  ): Promise<DeepSeekCompletion>;
}

export class DeepSeekClient implements ModelClient {
  public constructor(
    private readonly apiKey: string,
    private readonly config: GatewayConfig["deepseek"],
  ) {}

  public async complete(
    messages: readonly DeepSeekMessage[],
    tools: readonly DeepSeekTool[],
    signal: AbortSignal,
  ): Promise<DeepSeekCompletion> {
    const timeout = AbortSignal.timeout(this.config.requestTimeoutMs);
    const combinedSignal = AbortSignal.any([signal, timeout]);
    const body: Record<string, unknown> = {
      model: this.config.model,
      messages,
      thinking: { type: this.config.thinking },
      max_tokens: this.config.maxTokens,
      response_format: { type: "json_object" },
    };
    if (tools.length > 0) {
      body.tools = tools;
      body.tool_choice = "auto";
    }

    const endpoint = new URL("chat/completions", `${this.config.baseUrl.replace(/\/+$/, "")}/`);
    const response = await fetch(endpoint, {
      method: "POST",
      headers: {
        authorization: `Bearer ${this.apiKey}`,
        "content-type": "application/json",
        accept: "application/json",
      },
      body: JSON.stringify(body),
      signal: combinedSignal,
    });

    if (!response.ok) {
      const text = (await response.text()).replace(/\s+/g, " ").slice(0, 500);
      throw new Error(`DeepSeek request failed with HTTP ${response.status}: ${text}`);
    }

    const parsed = ChatResponseSchema.parse(await response.json());
    const choice = parsed.choices[0];
    if (choice === undefined) throw new Error("DeepSeek returned no completion choice.");
    const usage = parsed.usage === undefined ? undefined : {
      ...(parsed.usage.prompt_tokens === undefined
        ? {}
        : { promptTokens: parsed.usage.prompt_tokens }),
      ...(parsed.usage.completion_tokens === undefined
        ? {}
        : { completionTokens: parsed.usage.completion_tokens }),
      ...(parsed.usage.total_tokens === undefined
        ? {}
        : { totalTokens: parsed.usage.total_tokens }),
    };
    return {
      message: {
        role: "assistant",
        content: choice.message.content ?? null,
        ...(choice.message.tool_calls === undefined
          ? {}
          : { tool_calls: choice.message.tool_calls }),
      },
      ...(usage === undefined ? {} : { usage }),
    };
  }
}
