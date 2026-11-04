import assert from "node:assert/strict";
import test from "node:test";
import { loadConfig } from "../src/config.js";
import { fetchWeb, formatSearch } from "../src/mcp/web-tools.js";

test("web fetch rejects loopback and private network targets before requesting them", async () => {
  const { gateway } = await loadConfig("config/gateway.json");
  await assert.rejects(
    fetchWeb("http://127.0.0.1/private", 1000, gateway.web),
    /Private or local network/,
  );
  await assert.rejects(
    fetchWeb("http://localhost/private", 1000, gateway.web),
    /Local network/,
  );
});

test("search formatting preserves source URLs and bounded structured results", () => {
  const output = {
    query: "MateSoul",
    count: 1,
    results: [{
      title: "Result",
      url: "https://example.com/result",
      snippet: "Description",
      hostname: "example.com",
    }],
  };
  const markdown = formatSearch(output, "markdown");
  assert.match(markdown, /\[Result\]\(https:\/\/example.com\/result\)/);
  assert.deepEqual(JSON.parse(formatSearch(output, "json")), output);
});
