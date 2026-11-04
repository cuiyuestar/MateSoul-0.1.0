import type { Request } from "express";

export interface PlayerCredentials {
  apiKey: string;
  model?: string;
}

export class AIConnectionError extends Error {
  public constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
  ) { super(message); }
}

export function readPlayerCredentials(request: Request): PlayerCredentials | undefined {
  const apiKey = request.header("x-ai-api-key");
  const model = request.header("x-ai-model");
  if (apiKey === undefined && model === undefined) return undefined;
  if (!apiKey || apiKey.length > 4096 || !/^[\x21-\x7e]+$/.test(apiKey) ||
      model !== undefined && (!model || model.length > 100 || !/^[\x21-\x7e]+$/.test(model))) {
    throw new AIConnectionError(400, "INVALID_AI_CONFIGURATION", "API Key 或模型名称格式无效。");
  }
  return { apiKey, ...(model === undefined ? {} : { model }) };
}
