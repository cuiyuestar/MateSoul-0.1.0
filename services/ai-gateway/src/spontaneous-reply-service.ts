import type { CharacterConfig, GatewayConfig } from "./config.js";
import {
  parseSpontaneousReply,
  type SpontaneousReply,
  type SpontaneousReplyRequest,
} from "./contracts.js";
import type {
  DeepSeekMessage,
  ModelClient,
} from "./deepseek-client.js";

export interface SpontaneousReplyCompletion {
  reply: SpontaneousReply;
  modelCalls: number;
  totalTokens?: number;
}

export class SpontaneousReplyService {
  public constructor(
    private readonly model: ModelClient,
    private readonly character: CharacterConfig,
    private readonly config: GatewayConfig,
  ) {}

  public async complete(
    request: SpontaneousReplyRequest,
    signal: AbortSignal,
  ): Promise<SpontaneousReplyCompletion> {
    const messages = buildSpontaneousReplyMessages(
      this.character,
      request,
      this.config.conversation.maxReplyCharacters,
    );
    const completion = await this.model.complete(messages, [], signal);
    const firstTokens = completion.usage?.totalTokens ?? 0;
    const content = completion.message.content?.trim();
    if (content && (completion.message.tool_calls?.length ?? 0) === 0) {
      try {
        return withUsage(
          parseSpontaneousReply(
            JSON.parse(content),
            this.config.conversation.maxReplyCharacters,
          ),
          1,
          firstTokens,
        );
      } catch (error) {
        return this.repair(messages, content, describeError(error), firstTokens, signal);
      }
    }
    return this.repair(
      messages,
      content ?? "{}",
      "DeepSeek returned an empty or tool-bearing system reply.",
      firstTokens,
      signal,
    );
  }

  private async repair(
    messages: DeepSeekMessage[],
    invalidContent: string,
    validationError: string,
    previousTokens: number,
    signal: AbortSignal,
  ): Promise<SpontaneousReplyCompletion> {
    const completion = await this.model.complete(messages.concat(
      { role: "assistant", content: invalidContent },
      {
        role: "user",
        content: "The previous JSON response failed protocol validation: " +
          `${validationError} Return one corrected JSON object only.`,
      },
    ), [], signal);
    const content = completion.message.content?.trim();
    if (!content || (completion.message.tool_calls?.length ?? 0) > 0) {
      throw new Error("DeepSeek did not return a corrected system reply.");
    }
    return withUsage(
      parseSpontaneousReply(
        JSON.parse(content),
        this.config.conversation.maxReplyCharacters,
      ),
      2,
      previousTokens + (completion.usage?.totalTokens ?? 0),
    );
  }
}

export function buildSpontaneousReplyMessages(
  character: CharacterConfig,
  request: SpontaneousReplyRequest,
  maxReplyCharacters: number,
): DeepSeekMessage[] {
  if (request.persona.character_id !== character.id) {
    throw new Error(
      `Character '${request.persona.character_id}' does not match gateway character '${character.id}'.`,
    );
  }
  const protocol = [
    "This is a system-triggered in-world remark, not a player conversation turn.",
    "Do not infer or mention any conversation history, player memory, unavailable runtime state, tools, or internal implementation.",
    "Do not request an action and do not use external information.",
    "Return exactly one JSON object without Markdown.",
    `reply_text must be non-empty and at most ${maxReplyCharacters} characters.`,
    'The JSON shape is {"status":"ok","reply_text":"...","response_emotion":"normal"}.',
    "response_emotion must be exactly one of: normal, happy, sad, angry.",
    `Character persona: ${JSON.stringify(request.persona)}`,
    `Observed event: ${request.event_description}`,
    `Required content: ${JSON.stringify(request.content_requirements)}`,
    `Rules: ${JSON.stringify(request.rules)}`,
  ].join("\n");
  return [{
    role: "system",
    content: `${character.systemPrompt}\n\n${protocol}`,
  }];
}

function withUsage(
  reply: SpontaneousReply,
  modelCalls: number,
  totalTokens: number,
): SpontaneousReplyCompletion {
  return {
    reply,
    modelCalls,
    ...(totalTokens === 0 ? {} : { totalTokens }),
  };
}

function describeError(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
