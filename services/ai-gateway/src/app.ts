import { randomUUID, timingSafeEqual } from "node:crypto";
import express, { type Express, type NextFunction, type Request, type Response } from "express";
import type { ZodType } from "zod";
import type { ConversationCompletion } from "./conversation-service.js";
import type { ConversationRequest, GatewayErrorBody } from "./contracts.js";

export interface ConversationHandler {
  complete(
    request: ConversationRequest,
    signal: AbortSignal,
  ): Promise<ConversationCompletion>;
}

export interface AppOptions {
  handler: ConversationHandler;
  requestSchema: ZodType<ConversationRequest>;
  requestBodyLimit: string;
  clientToken: string | null;
  toolNames: readonly string[];
  log(event: string, fields: Record<string, unknown>): void;
}

export function createApp(options: AppOptions): Express {
  const app = express();
  app.disable("x-powered-by");
  app.use(express.json({ limit: options.requestBodyLimit, strict: true }));
  app.use((request, response, next) => authorize(request, response, next, options.clientToken));

  app.get("/health", (_request, response) => {
    response.json({
      status: "ok",
      service: "matesoul-ai-gateway",
      mcp_tools: options.toolNames,
    });
  });

  app.post("/v1/conversations/reply", async (request, response) => {
    const fallbackRequestId = randomUUID();
    const parsed = options.requestSchema.safeParse(request.body);
    if (!parsed.success) {
      options.log("AI_REQUEST_REJECTED", {
        requestId: fallbackRequestId,
        issues: parsed.error.issues.map((issue) => ({
          path: issue.path.join("."),
          code: issue.code,
          message: issue.message,
        })),
      });
      writeError(response, 400, "INVALID_REQUEST", "对话请求格式无效。", fallbackRequestId);
      return;
    }

    const conversation = parsed.data;
    const started = performance.now();
    const cancellation = new AbortController();
    request.once("aborted", () => cancellation.abort());
    response.setHeader("x-request-id", conversation.request_id);
    options.log("AI_REQUEST_STARTED", {
      sessionId: conversation.session_id,
      requestId: conversation.request_id,
      historyMessages: conversation.history.length,
      actions: conversation.actions.length,
      targets: conversation.targets.length,
      contextRevision: conversation.runtime.revision,
      activeActions: conversation.runtime.actions.length,
      runtimeActions: conversation.runtime.actions.map((action) => ({
        id: action.id,
        state: action.state,
        phase: action.phase,
        targetId: action.target?.id ?? null,
      })),
      posture: conversation.runtime.posture,
      mood: conversation.runtime.mood,
      outfitId: conversation.runtime.appearance.outfit_id,
    });
    // #region debug-point C-D:gateway-request
    void fetch("http://127.0.0.1:7782/event", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ sessionId: "outfit-context-stale", runId: "pre-fix", hypothesisId: "C,D", location: "app.ts:conversation-request", msg: "[DEBUG] gateway request received", data: { requestId: conversation.request_id, contextRevision: conversation.runtime.revision, outfitId: conversation.runtime.appearance.outfit_id, displayName: conversation.runtime.appearance.display_name, historyMessages: conversation.history.length, previousAssistantReply: conversation.history.filter((message) => message.role === "assistant").at(-1)?.content ?? null }, ts: Date.now() }) }).catch(() => {});
    // #endregion

    try {
      const completion = await options.handler.complete(conversation, cancellation.signal);
      options.log("AI_REQUEST_COMPLETED", {
        sessionId: conversation.session_id,
        requestId: conversation.request_id,
        durationMs: Math.round(performance.now() - started),
        modelCalls: completion.modelCalls,
        toolCalls: completion.toolCalls,
        totalTokens: completion.totalTokens ?? null,
        actionId: completion.reply.action.id,
        targetId: completion.reply.action.target_id,
      });
      // #region debug-point C:gateway-reply
      void fetch("http://127.0.0.1:7782/event", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ sessionId: "outfit-context-stale", runId: "pre-fix", hypothesisId: "C", location: "app.ts:conversation-reply", msg: "[DEBUG] gateway reply completed", data: { requestId: conversation.request_id, outfitId: conversation.runtime.appearance.outfit_id, replyText: completion.reply.reply_text, modelCalls: completion.modelCalls }, ts: Date.now() }) }).catch(() => {});
      // #endregion
      response.json(completion.reply);
    } catch (error) {
      options.log("AI_REQUEST_FAILED", {
        sessionId: conversation.session_id,
        requestId: conversation.request_id,
        durationMs: Math.round(performance.now() - started),
        error: error instanceof Error ? error.message : String(error),
      });
      if (!response.headersSent && !cancellation.signal.aborted) {
        writeError(
          response,
          502,
          "AI_UPSTREAM_FAILED",
          "AI 服务暂时无法完成这次对话。",
          conversation.request_id,
        );
      }
    }
  });

  app.use((
    error: unknown,
    _request: Request,
    response: Response,
    _next: NextFunction,
  ) => {
    options.log("HTTP_REQUEST_FAILED", {
      error: error instanceof Error ? error.message : String(error),
    });
    if (!response.headersSent) {
      writeError(response, 400, "INVALID_HTTP_REQUEST", "请求无法解析。", randomUUID());
    }
  });
  return app;
}

function authorize(
  request: Request,
  response: Response,
  next: NextFunction,
  expectedToken: string | null,
): void {
  if (expectedToken === null || request.path === "/health") {
    next();
    return;
  }
  const authorization = request.header("authorization");
  const provided = authorization?.startsWith("Bearer ") ? authorization.slice(7) : "";
  const expectedBytes = Buffer.from(expectedToken);
  const providedBytes = Buffer.from(provided);
  if (providedBytes.length !== expectedBytes.length ||
      !timingSafeEqual(providedBytes, expectedBytes)) {
    writeError(response, 401, "UNAUTHORIZED", "网关认证失败。", randomUUID());
    return;
  }
  next();
}

function writeError(
  response: Response,
  status: number,
  code: string,
  message: string,
  requestId: string,
): void {
  const body: GatewayErrorBody = {
    error: { code, message, request_id: requestId },
  };
  response.status(status).json(body);
}
