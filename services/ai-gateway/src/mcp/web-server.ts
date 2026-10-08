#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { loadConfig, readConfigArgument } from "../config.js";
import {
  fetchWeb,
  formatFetch,
  formatSearch,
  searchWeb,
} from "./web-tools.js";

async function main(): Promise<void> {
  const { gateway } = await loadConfig(readConfigArgument(process.argv.slice(2)));
  const server = new McpServer(
    { name: "web-mcp-server", version: "1.0.0" },
    { capabilities: { logging: {} }, maxToolInputElements: 100 },
  );
  const responseFormat = z.enum(["markdown", "json"]).default("markdown")
    .describe("Use markdown for model-readable text or json for structured output.");

  server.registerTool(
    "web_search",
    {
      title: "Search the Web",
      description: "Search the public web for current information. Returns ranked titles, URLs, and snippets. This tool only reads public information.",
      inputSchema: {
        query: z.string().trim().min(2).max(300)
          .describe("Specific web search query."),
        limit: z.number().int().min(1).max(gateway.web.maxSearchResults).default(5)
          .describe("Maximum number of results."),
        response_format: responseFormat,
      },
      outputSchema: {
        query: z.string(),
        count: z.number().int(),
        results: z.array(z.object({
          title: z.string(),
          url: z.string(),
          snippet: z.string(),
          hostname: z.string(),
        })),
      },
      annotations: {
        readOnlyHint: true,
        destructiveHint: false,
        idempotentHint: true,
        openWorldHint: true,
      },
    },
    async ({ query, limit, response_format }, extra) => {
      try {
        const output = await searchWeb(query, limit, gateway.web, extra.signal);
        return {
          content: [{ type: "text", text: formatSearch(output, response_format) }],
          structuredContent: output,
        };
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: describeError("Web search failed", error) }],
        };
      }
    },
  );

  server.registerTool(
    "web_fetch",
    {
      title: "Read a Web Page",
      description: "Read the main text of a public HTTP or HTTPS page. Private, loopback, and local network addresses are rejected. This tool does not execute page scripts.",
      inputSchema: {
        url: z.url().describe("Public HTTP or HTTPS URL to read."),
        max_characters: z.number().int().min(1000)
          .max(gateway.web.maxFetchCharacters).default(12000)
          .describe("Maximum extracted characters to return."),
        response_format: responseFormat,
      },
      outputSchema: {
        url: z.string(),
        title: z.string(),
        contentType: z.string(),
        content: z.string(),
        truncated: z.boolean(),
      },
      annotations: {
        readOnlyHint: true,
        destructiveHint: false,
        idempotentHint: true,
        openWorldHint: true,
      },
    },
    async ({ url, max_characters, response_format }, extra) => {
      try {
        const output = await fetchWeb(url, max_characters, gateway.web, extra.signal);
        return {
          content: [{ type: "text", text: formatFetch(output, response_format) }],
          structuredContent: output,
        };
      } catch (error) {
        return {
          isError: true,
          content: [{ type: "text", text: describeError("Web fetch failed", error) }],
        };
      }
    },
  );

  await server.connect(new StdioServerTransport());
  console.error("web-mcp-server ready");
}

function describeError(prefix: string, error: unknown): string {
  return `${prefix}: ${error instanceof Error ? error.message : String(error)}`;
}

main().catch((error: unknown) => {
  console.error(error);
  process.exitCode = 1;
});
