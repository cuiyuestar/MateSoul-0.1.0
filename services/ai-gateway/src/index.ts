#!/usr/bin/env node
import { resolve } from "node:path";
import dotenv from "dotenv";
import { createApp } from "./app.js";
import { loadConfig, readConfigArgument } from "./config.js";
import { ConversationService } from "./conversation-service.js";
import { createConversationRequestSchema } from "./contracts.js";
import { DeepSeekClient } from "./deepseek-client.js";
import { McpToolRuntime } from "./mcp-runtime.js";
import { SpontaneousReplyService } from "./spontaneous-reply-service.js";
import { AIConnectionError, type PlayerCredentials } from "./player-credentials.js";

async function main(): Promise<void> {
  dotenv.config({ path: resolve(".env.local"), quiet: true });
  const loaded = await loadConfig(readConfigArgument(process.argv.slice(2)));
  const apiKey = process.env.DEEPSEEK_API_KEY;

  const tokenEnvironmentVariable = loaded.gateway.server.clientTokenEnvironmentVariable;
  const clientToken = tokenEnvironmentVariable === null
    ? null
    : process.env[tokenEnvironmentVariable] ?? null;
  if (tokenEnvironmentVariable !== null && clientToken === null) {
    throw new Error(`${tokenEnvironmentVariable} is required by the gateway configuration.`);
  }

  const mcp = await McpToolRuntime.connect(
    loaded.configPath,
    loaded.gateway.conversation.maxToolResultCharacters,
  );
  const log = (event: string, fields: Record<string, unknown>): void => {
    process.stdout.write(`${JSON.stringify({
      timestamp: new Date().toISOString(),
      event,
      ...fields,
    })}\n`);
  };
  // A client and its credentials belong to one HTTP request, never a shared singleton.
  const modelFor = (credentials?: PlayerCredentials): DeepSeekClient => {
    const key = credentials?.apiKey ?? apiKey;
    if (!key)
      throw new AIConnectionError(401, "AI_CONFIGURATION_REQUIRED", "先配置ai才能激活对话功能");
    return new DeepSeekClient(key, {
      ...loaded.gateway.deepseek,
      model: credentials?.model ?? loaded.gateway.deepseek.model,
    });
  };
  const app = createApp({
    handler: {
      complete: (request, signal, credentials) => new ConversationService(
        modelFor(credentials), mcp, loaded.character, loaded.gateway, log,
      ).complete(request, signal),
    },
    spontaneousReplyHandler: {
      complete: (request, signal, credentials) => new SpontaneousReplyService(
        modelFor(credentials), loaded.character, loaded.gateway,
      ).complete(request, signal),
    },
    checkConfiguration: async (credentials, signal) => {
      await modelFor(credentials).complete(
        [{ role: "user", content: '仅返回 JSON：{"status":"ok"}' }],
        [], AbortSignal.any([signal, AbortSignal.timeout(15000)]),
      );
    },
    requestSchema: createConversationRequestSchema(
      loaded.gateway.conversation.maxHistoryMessages,
    ),
    requestBodyLimit: loaded.gateway.server.requestBodyLimit,
    clientToken,
    toolNames: mcp.tools.map((tool) => tool.function.name),
    log,
  });
  const server = app.listen(
    loaded.gateway.server.port,
    loaded.gateway.server.host,
    () => log("GATEWAY_READY", {
      address: `http://${loaded.gateway.server.host}:${loaded.gateway.server.port}`,
      model: loaded.gateway.deepseek.model,
      mcpTools: mcp.tools.map((tool) => tool.function.name),
    }),
  );

  let stopping = false;
  const stop = async (signal: string): Promise<void> => {
    if (stopping) return;
    stopping = true;
    log("GATEWAY_STOPPING", { signal });
    await new Promise<void>((resolveClose, rejectClose) => {
      server.close((error) => error === undefined ? resolveClose() : rejectClose(error));
    });
    await mcp.close();
    process.exit(0);
  };
  process.once("SIGINT", () => void stop("SIGINT"));
  process.once("SIGTERM", () => void stop("SIGTERM"));
}

main().catch((error: unknown) => {
  process.stderr.write(`${error instanceof Error ? error.stack ?? error.message : String(error)}\n`);
  process.exitCode = 1;
});
