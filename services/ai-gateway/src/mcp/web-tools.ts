import { lookup } from "node:dns/promises";
import { isIP } from "node:net";
import { load } from "cheerio";
import type { GatewayConfig } from "../config.js";

export type ResponseFormat = "markdown" | "json";

export interface WebSearchOutput extends Record<string, unknown> {
  query: string;
  count: number;
  results: Array<{
    title: string;
    url: string;
    snippet: string;
    hostname: string;
  }>;
}

export interface WebFetchOutput extends Record<string, unknown> {
  url: string;
  title: string;
  contentType: string;
  content: string;
  truncated: boolean;
}

export async function searchWeb(
  query: string,
  limit: number,
  config: GatewayConfig["web"],
  signal?: AbortSignal,
): Promise<WebSearchOutput> {
  const count = Math.min(limit, config.maxSearchResults);
  const endpoint = new URL(config.searchEndpoint);
  endpoint.searchParams.set("q", query);
  endpoint.searchParams.set("count", String(count));
  endpoint.searchParams.set("mkt", config.searchMarket);
  endpoint.searchParams.set("setlang", config.searchMarket);
  const timeout = AbortSignal.timeout(config.searchTimeoutMs);
  const combinedSignal = signal === undefined ? timeout : AbortSignal.any([signal, timeout]);
  const response = await fetch(endpoint, {
    headers: {
      accept: "text/html,application/xhtml+xml",
      "accept-language": config.searchMarket,
      "user-agent": "Mozilla/5.0 (compatible; MateSoul-Web-MCP/1.0)",
    },
    signal: combinedSignal,
  });
  if (!response.ok) throw new Error(`Web search failed with HTTP ${response.status}.`);
  const document = load(await readLimitedBody(response, config.maxFetchBytes));
  const results: WebSearchOutput["results"] = [];
  document("li.b_algo").each((_index, element) => {
    if (results.length >= count) return false;
    const root = document(element);
    const anchor = root.find("h2 a").first();
    const urlValue = anchor.attr("href");
    if (!urlValue) return;
    let url: URL;
    try {
      url = new URL(urlValue);
    } catch {
      return;
    }
    if (url.protocol !== "http:" && url.protocol !== "https:") return;
    results.push({
      title: normalizeWhitespace(anchor.text()),
      url: url.toString(),
      snippet: normalizeWhitespace(root.find(".b_caption p").first().text()),
      hostname: url.hostname,
    });
  });
  return {
    query,
    count: results.length,
    results,
  };
}

export async function fetchWeb(
  requestedUrl: string,
  maxCharacters: number,
  config: GatewayConfig["web"],
  signal?: AbortSignal,
): Promise<WebFetchOutput> {
  let current = new URL(requestedUrl);
  const timeout = AbortSignal.timeout(config.fetchTimeoutMs);
  const combinedSignal = signal === undefined ? timeout : AbortSignal.any([signal, timeout]);
  let response: Response | undefined;

  for (let redirects = 0; redirects <= 4; redirects++) {
    await assertPublicHttpUrl(current);
    response = await fetch(current, {
      redirect: "manual",
      headers: {
        accept: "text/html,application/xhtml+xml,text/plain,application/json;q=0.9,*/*;q=0.1",
        "user-agent": "MateSoul-Web-MCP/1.0",
      },
      signal: combinedSignal,
    });
    if (![301, 302, 303, 307, 308].includes(response.status)) break;
    const location = response.headers.get("location");
    if (location === null) throw new Error("Redirect response has no Location header.");
    current = new URL(location, current);
    response.body?.cancel();
    response = undefined;
  }

  if (response === undefined) throw new Error("Web fetch exceeded the redirect limit.");
  if (!response.ok) throw new Error(`Web fetch failed with HTTP ${response.status}.`);
  const contentType = response.headers.get("content-type")?.split(";", 1)[0] ?? "text/plain";
  const raw = await readLimitedBody(response, config.maxFetchBytes);
  const extracted = contentType.includes("html") ? extractHtml(raw) : {
    title: current.hostname,
    content: normalizeWhitespace(raw),
  };
  const limit = Math.min(maxCharacters, config.maxFetchCharacters);
  const truncated = extracted.content.length > limit;
  return {
    url: current.toString(),
    title: extracted.title,
    contentType,
    content: truncated ? extracted.content.slice(0, limit) : extracted.content,
    truncated,
  };
}

export function formatSearch(output: WebSearchOutput, format: ResponseFormat): string {
  if (format === "json") return JSON.stringify(output);
  if (output.results.length === 0) return `No web results found for "${output.query}".`;
  const lines = [`# Web results for "${output.query}"`, ""];
  output.results.forEach((result, index) => {
    lines.push(`${index + 1}. [${result.title}](${result.url})`);
    lines.push(`   ${result.snippet}`);
  });
  return lines.join("\n");
}

export function formatFetch(output: WebFetchOutput, format: ResponseFormat): string {
  if (format === "json") return JSON.stringify(output);
  return `# ${output.title}\n\nSource: ${output.url}\n\n${output.content}` +
    (output.truncated ? "\n\n[Content truncated]" : "");
}

async function assertPublicHttpUrl(url: URL): Promise<void> {
  if (url.protocol !== "http:" && url.protocol !== "https:") {
    throw new Error("Only HTTP and HTTPS URLs are supported.");
  }
  const hostname = url.hostname.toLowerCase().replace(/\.$/, "");
  if (hostname === "localhost" || hostname.endsWith(".localhost")) {
    throw new Error("Local network URLs are not available to web tools.");
  }
  const addresses = isIP(hostname) === 0
    ? await lookup(hostname, { all: true, verbatim: true })
    : [{ address: hostname, family: isIP(hostname) }];
  if (addresses.length === 0 || addresses.some(({ address }) => isPrivateAddress(address))) {
    throw new Error("Private or local network URLs are not available to web tools.");
  }
}

function isPrivateAddress(address: string): boolean {
  const normalized = address.toLowerCase();
  if (normalized === "::" || normalized === "::1") return true;
  if (normalized.startsWith("fc") || normalized.startsWith("fd") ||
      /^fe[89ab]/.test(normalized)) return true;
  const mapped = normalized.match(/^::ffff:(\d+\.\d+\.\d+\.\d+)$/)?.[1];
  const ipv4 = mapped ?? (isIP(normalized) === 4 ? normalized : null);
  if (ipv4 === null) return false;
  const octets = ipv4.split(".").map(Number);
  const first = octets[0] ?? 0;
  const second = octets[1] ?? 0;
  return first === 0 || first === 10 || first === 127 ||
    (first === 169 && second === 254) ||
    (first === 172 && second >= 16 && second <= 31) ||
    (first === 192 && second === 168) ||
    first >= 224;
}

async function readLimitedBody(response: Response, byteLimit: number): Promise<string> {
  const declared = Number(response.headers.get("content-length"));
  if (Number.isFinite(declared) && declared > byteLimit) {
    throw new Error(`Web response exceeds the ${byteLimit} byte limit.`);
  }
  if (response.body === null) return "";
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let bytes = 0;
  while (true) {
    const part = await reader.read();
    if (part.done) break;
    bytes += part.value.byteLength;
    if (bytes > byteLimit) {
      await reader.cancel();
      throw new Error(`Web response exceeds the ${byteLimit} byte limit.`);
    }
    chunks.push(part.value);
  }
  const body = new Uint8Array(bytes);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return new TextDecoder().decode(body);
}

function extractHtml(html: string): { title: string; content: string } {
  const document = load(html);
  document("script,style,noscript,svg,canvas,nav,header,footer,form").remove();
  const title = normalizeWhitespace(document("title").first().text()) || "Web page";
  const content = normalizeWhitespace(document("main,article").first().text() || document("body").text());
  return { title, content };
}

function normalizeWhitespace(value: string): string {
  return value.replace(/\s+/g, " ").trim();
}
