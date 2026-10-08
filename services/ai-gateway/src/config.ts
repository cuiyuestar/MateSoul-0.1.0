import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { z } from "zod";

const GatewayConfigSchema = z.object({
  server: z.object({
    host: z.string().min(1),
    port: z.number().int().min(1).max(65535),
    requestBodyLimit: z.string().min(1),
    clientTokenEnvironmentVariable: z.string().min(1).nullable(),
  }).strict(),
  deepseek: z.object({
    baseUrl: z.url(),
    model: z.string().min(1),
    thinking: z.enum(["enabled", "disabled"]),
    maxTokens: z.number().int().min(128).max(8192),
    requestTimeoutMs: z.number().int().min(1000).max(120000),
    maxToolRounds: z.number().int().min(1).max(8),
  }).strict(),
  conversation: z.object({
    characterConfig: z.string().min(1),
    maxHistoryMessages: z.number().int().min(0).max(64),
    maxReplyCharacters: z.number().int().min(1).max(4096),
    maxToolResultCharacters: z.number().int().min(1000).max(50000),
  }).strict(),
  web: z.object({
    searchEndpoint: z.url(),
    searchMarket: z.string().min(2).max(16),
    searchTimeoutMs: z.number().int().min(1000).max(60000),
    fetchTimeoutMs: z.number().int().min(1000).max(60000),
    maxSearchResults: z.number().int().min(1).max(20),
    maxFetchBytes: z.number().int().min(1024).max(10 * 1024 * 1024),
    maxFetchCharacters: z.number().int().min(1000).max(100000),
  }).strict(),
}).strict();

const CharacterConfigSchema = z.object({
  id: z.string().min(1),
  displayName: z.string().min(1),
  systemPrompt: z.string().min(1),
}).strict();

export type GatewayConfig = z.infer<typeof GatewayConfigSchema>;
export type CharacterConfig = z.infer<typeof CharacterConfigSchema>;

export interface LoadedConfig {
  gateway: GatewayConfig;
  character: CharacterConfig;
  configPath: string;
}

export async function loadConfig(configPath?: string): Promise<LoadedConfig> {
  const absolutePath = resolve(configPath ?? "config/gateway.json");
  const gateway = GatewayConfigSchema.parse(
    JSON.parse(await readFile(absolutePath, "utf8")),
  );
  const characterPath = resolve(dirname(absolutePath), gateway.conversation.characterConfig);
  const character = CharacterConfigSchema.parse(
    JSON.parse(await readFile(characterPath, "utf8")),
  );
  return { gateway, character, configPath: absolutePath };
}

export function readConfigArgument(args: readonly string[]): string | undefined {
  const index = args.indexOf("--config");
  if (index < 0) return undefined;
  const value = args[index + 1];
  if (value === undefined) throw new Error("--config requires a file path.");
  return value;
}
