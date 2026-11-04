import { resolve } from "node:path";
import { McpToolRuntime } from "../src/mcp-runtime.js";

async function main(): Promise<void> {
  const runtime = await McpToolRuntime.connect(
    resolve("config/gateway.json"),
    16000,
  );
  try {
    const searchText = await runtime.call("web_search", {
      query: "DeepSeek API official documentation",
      limit: 3,
      response_format: "json",
    });
    const search = JSON.parse(searchText) as {
      results: Array<{ url: string; title: string }>;
    };
    if (search.results.length === 0) throw new Error("Live web search returned no results.");
    const fetched = await runtime.call("web_fetch", {
      url: search.results[0]!.url,
      max_characters: 3000,
      response_format: "json",
    });
    const page = JSON.parse(fetched) as { title: string; content: string };
    if (page.content.length === 0) throw new Error("Live web fetch returned empty content.");
    process.stdout.write(`${JSON.stringify({
      tools: runtime.tools.map((tool) => tool.function.name),
      searchResults: search.results.length,
      firstResult: search.results[0],
      fetchedTitle: page.title,
      fetchedCharacters: page.content.length,
    }, null, 2)}\n`);
  } finally {
    await runtime.close();
  }
}

main().catch((error: unknown) => {
  process.stderr.write(`${error instanceof Error ? error.stack ?? error.message : String(error)}\n`);
  process.exitCode = 1;
});
