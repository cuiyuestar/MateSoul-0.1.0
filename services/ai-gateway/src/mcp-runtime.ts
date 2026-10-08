import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import type { DeepSeekTool } from "./deepseek-client.js";

export interface ToolRuntime {
  readonly tools: readonly DeepSeekTool[];
  call(name: string, argumentsValue: unknown): Promise<string>;
  close(): Promise<void>;
}

export class McpToolRuntime implements ToolRuntime {
  private readonly client = new Client({
    name: "matesoul-ai-gateway",
    version: "1.0.0",
  });
  private readonly registeredTools = new Map<string, DeepSeekTool>();

  private constructor(
    private readonly transport: StdioClientTransport,
    private readonly maxResultCharacters: number,
  ) {}

  public static async connect(
    configPath: string,
    maxResultCharacters: number,
  ): Promise<McpToolRuntime> {
    const sourceRuntime = fileURLToPath(import.meta.url).endsWith(".ts");
    const serverPath = fileURLToPath(new URL(
      sourceRuntime ? "./mcp/web-server.ts" : "./mcp/web-server.js",
      import.meta.url,
    ));
    const transport = new StdioClientTransport({
      command: process.execPath,
      args: [
        ...(sourceRuntime ? ["--import", "tsx"] : []),
        serverPath,
        "--config",
        configPath,
      ],
      stderr: "inherit",
    });
    const runtime = new McpToolRuntime(transport, maxResultCharacters);
    await runtime.client.connect(transport);
    const listed = await runtime.client.listTools();
    for (const tool of listed.tools) {
      if (runtime.registeredTools.has(tool.name)) {
        throw new Error(`Duplicate MCP tool name: ${tool.name}`);
      }
      runtime.registeredTools.set(tool.name, {
        type: "function",
        function: {
          name: tool.name,
          description: tool.description ?? tool.title ?? tool.name,
          parameters: tool.inputSchema as Record<string, unknown>,
        },
      });
    }
    if (runtime.registeredTools.size === 0) {
      throw new Error("The configured MCP server exposed no tools.");
    }
    return runtime;
  }

  public get tools(): readonly DeepSeekTool[] {
    return Array.from(this.registeredTools.values());
  }

  public async call(name: string, argumentsValue: unknown): Promise<string> {
    if (!this.registeredTools.has(name)) {
      throw new Error(`Unknown MCP tool: ${name}`);
    }
    if (!isRecord(argumentsValue)) {
      throw new Error(`Arguments for MCP tool '${name}' must be a JSON object.`);
    }
    const result = await this.client.callTool({ name, arguments: argumentsValue });
    const content = Array.isArray(result.content) ? result.content : [];
    const text = content
      .filter(isTextContent)
      .map((part) => part.text)
      .join("\n");
    const rendered = text.length > 0
      ? text
      : JSON.stringify(result.structuredContent ?? {});
    const bounded = rendered.length > this.maxResultCharacters
      ? `${rendered.slice(0, this.maxResultCharacters)}\n[Tool result truncated]`
      : rendered;
    return result.isError === true ? `Tool error: ${bounded}` : bounded;
  }

  public async close(): Promise<void> {
    await this.client.close();
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isTextContent(value: unknown): value is { type: "text"; text: string } {
  return isRecord(value) && value.type === "text" && typeof value.text === "string";
}
