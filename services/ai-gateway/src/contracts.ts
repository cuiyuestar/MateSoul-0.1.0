import { z } from "zod";

const DialogueMessageSchema = z.object({
  role: z.enum(["user", "assistant"]),
  content: z.string().min(1).max(4096),
}).strict();

const ActionInfoSchema = z.object({
  id: z.string().min(1).max(128),
  description: z.string().min(1).max(500),
  target_capability: z.union([
    z.string().min(1).max(128),
    z.literal("").transform(() => null),
    z.null(),
  ]).optional(),
}).strict();

const TargetInfoSchema = z.object({
  id: z.string().min(1).max(128),
  name: z.string().min(1).max(200),
  state: z.string().min(1).max(128),
  capabilities: z.array(z.string().min(1).max(128)).max(64),
}).strict();

const PersonalityTraitSchema = z.object({
  id: z.enum([
    "extraversion",
    "conscientiousness",
    "openness",
    "agreeableness",
    "emotional_stability",
  ]),
  display_name: z.string().min(1).max(64),
  option_id: z.string().min(1).max(64),
  option_name: z.string().min(1).max(64),
  description: z.string().min(1).max(500),
}).strict();

export const PersonaSchema = z.object({
  schema_version: z.literal(1),
  character_id: z.string().min(1).max(64),
  name: z.string().min(1).max(64),
  age: z.literal(16),
  gender: z.literal("female"),
  height_cm: z.literal(155),
  personality: z.array(PersonalityTraitSchema).length(5)
    .refine((traits) => new Set(traits.map((trait) => trait.id)).size === traits.length, {
      message: "Personality trait IDs must be unique.",
    }),
}).strict();

const PresentRuntimeTargetSchema = z.object({
  id: z.string().min(1).max(128),
  name: z.string().min(1).max(200),
  state: z.string().min(1).max(128),
}).strict();

const RuntimeTargetSchema = z.union([
  PresentRuntimeTargetSchema,
  z.object({
    id: z.literal(""),
    name: z.literal(""),
    state: z.literal(""),
  }).strict().transform(() => null),
  z.null(),
]);

const RuntimeActionSchema = z.object({
  id: z.string().min(1).max(128),
  description: z.string().min(1).max(500),
  state: z.enum(["queued", "running", "cancelling"]),
  phase: z.string().min(1).max(128),
  initiator: z.enum(["autonomous", "ai", "ui", "player", "system"]),
  elapsed_seconds: z.number().finite().nonnegative(),
  target: RuntimeTargetSchema.nullable().optional(),
}).strict();

const RuntimeEventSchema = z.object({
  type: z.enum([
    "personality_changed",
    "outfit_changed",
    "mood_changed",
    "action_finished",
  ]),
  action_id: z.string().max(128).optional(),
  state: z.string().max(64).optional(),
  target_id: z.string().max(128).optional(),
  target_name: z.string().max(200).optional(),
  value: z.string().max(200).optional(),
}).strict();

const RuntimeWorldItemSchema = z.object({
  id: z.string().min(1).max(128),
  name: z.string().min(1).max(200),
  state: z.string().min(1).max(128),
  control: z.string().min(1).max(64),
  distance: z.enum(["unknown", "within-reach", "near", "in-room", "far"]),
}).strict();

const RuntimeWorldSchema = z.object({
  schema_version: z.literal(1),
  revision: z.number().int().nonnegative(),
  player: z.object({
    nearby: z.boolean(),
    proximity: z.enum(["unknown", "within-reach", "near", "in-room", "far"]),
    distance_m: z.number().finite().nonnegative(),
    dialogue_open: z.boolean(),
    interaction_mode: z.enum(["unknown", "controller", "eye-hand", "gesture"]),
    focused_item: RuntimeWorldItemSchema.nullable().optional(),
  }).strict(),
  held_items: z.array(z.object({
    id: z.string().min(1).max(128),
    name: z.string().min(1).max(200),
    state: z.string().min(1).max(128),
    holder: z.enum(["player", "character"]),
  }).strict()).max(8),
  activity: z.object({
    id: z.string().min(1).max(128),
    name: z.string().min(1).max(200),
    state: z.string().min(1).max(64),
    phase: z.string().min(1).max(128),
    player_score: z.number().int().nonnegative(),
    companion_score: z.number().int().nonnegative(),
  }).strict().nullable().optional(),
  active_facts: z.array(z.object({
    subject_id: z.string().min(1).max(128),
    subject_name: z.string().min(1).max(200),
    category: z.string().min(1).max(64),
    state: z.string().min(1).max(128),
    value: z.string().max(500),
  }).strict()).max(12),
  inventory_alerts: z.array(z.object({
    definition_id: z.string().min(1).max(128),
    name: z.string().min(1).max(200),
    remaining: z.number().int().nonnegative(),
    limit: z.number().int().positive(),
    severity: z.enum(["low", "empty"]),
  }).strict()).max(8),
  environment: z.object({
    revision: z.number().int().nonnegative(),
    source: z.string().min(1).max(200),
    confidence: z.enum(["low", "medium", "high"]),
    count_quality: z.enum(["observed", "estimated"]),
    floor_area_m2: z.number().finite().nonnegative(),
    width_m: z.number().finite().nonnegative(),
    length_m: z.number().finite().nonnegative(),
    height_m: z.number().finite().nonnegative(),
    shape: z.enum(["unknown", "rectangular", "irregular", "multi-area"]),
    open_space: z.enum(["open", "balanced", "dense"]),
    floor_count: z.number().int().nonnegative(),
    semantic_counts: z.array(z.object({
      id: z.string().min(1).max(64),
      name: z.string().min(1).max(100),
      count: z.number().int().nonnegative(),
      estimated: z.boolean(),
    }).strict()).max(24),
    zones: z.array(z.object({
      id: z.string().min(1).max(64),
      description: z.string().min(1).max(200),
    }).strict()).max(6),
    relations: z.array(z.object({
      subject: z.string().min(1).max(128),
      relation: z.string().min(1).max(64),
      target: z.string().min(1).max(128),
    }).strict()).max(8),
  }).strict(),
  virtual_world: z.object({
    revision: z.number().int().nonnegative(),
    total_count: z.number().int().nonnegative(),
    omitted_group_count: z.number().int().nonnegative(),
    item_groups: z.array(z.object({
      definition_id: z.string().min(1).max(128),
      name: z.string().min(1).max(200),
      state: z.string().min(1).max(128),
      control: z.string().min(1).max(64),
      count: z.number().int().positive(),
    }).strict()).max(24),
    control_counts: z.array(z.object({
      control: z.string().min(1).max(64),
      count: z.number().int().positive(),
    }).strict()).max(8),
    salient_items: z.array(RuntimeWorldItemSchema).max(12),
  }).strict(),
}).strict();

const RuntimeSchema = z.object({
  schema_version: z.literal(1),
  revision: z.number().int().nonnegative(),
  appearance: z.object({
    outfit_id: z.string().min(1).max(128),
    display_name: z.string().min(1).max(200),
  }).strict(),
  mood: z.object({
    id: z.string().min(1).max(128),
    level: z.enum(["none", "light", "medium", "high"]),
  }).strict(),
  posture: z.string().min(1).max(128),
  actions: z.array(RuntimeActionSchema).max(16),
  recent_events: z.array(RuntimeEventSchema).max(8),
  world: RuntimeWorldSchema.nullable().optional(),
}).strict();

export function createConversationRequestSchema(maxHistoryMessages: number) {
  return z.object({
    session_id: z.string().min(1).max(128),
    request_id: z.string().min(1).max(128),
    input: z.string().trim().min(1).max(1000),
    history: z.array(DialogueMessageSchema).max(maxHistoryMessages),
    actions: z.array(ActionInfoSchema).max(64),
    targets: z.array(TargetInfoSchema).max(256),
    moods: z.array(z.string().min(1).max(128)).max(64),
    persona: PersonaSchema,
    runtime: RuntimeSchema,
  }).strict();
}

export type ConversationRequest = z.infer<
  ReturnType<typeof createConversationRequestSchema>
>;

const ModelReplySchema = z.object({
  status: z.literal("ok").optional(),
  reply_text: z.string().trim().min(1),
  response_emotion: z.enum(["normal", "happy", "sad", "angry"]),
  action: z.object({
    id: z.string().min(1),
    target_id: z.string().min(1).nullable().optional(),
  }).strict().nullable().optional(),
  mood_signals: z.array(z.object({
    id: z.string().min(1),
    delta: z.number().finite().min(-32).max(32),
  }).strict()).max(16).optional(),
}).strict();

export interface ConversationReply {
  status: "ok";
  reply_text: string;
  response_emotion: "normal" | "happy" | "sad" | "angry";
  action: {
    id: string;
    target_id: string | null;
  };
  mood_signals: Array<{
    id: string;
    delta: number;
  }>;
}

export function parseConversationReply(
  value: unknown,
  request: ConversationRequest,
  maxReplyCharacters: number,
): ConversationReply {
  const parsed = ModelReplySchema.parse(value);
  if (parsed.reply_text.length > maxReplyCharacters) {
    throw new Error(`reply_text exceeds ${maxReplyCharacters} characters.`);
  }

  const action = parsed.action ?? { id: "none", target_id: null };
  const actionDefinition = request.actions.find((candidate) => candidate.id === action.id);
  if (action.id !== "none" && actionDefinition === undefined) {
    throw new Error(`Action '${action.id}' is not in the runtime action catalog.`);
  }

  const targetId = action.target_id ?? null;
  const capability = actionDefinition?.target_capability ?? null;
  if (capability !== null) {
    const target = request.targets.find((candidate) => candidate.id === targetId);
    if (target === undefined || !target.capabilities.includes(capability)) {
      throw new Error(
        `Action '${action.id}' requires a target with capability '${capability}'.`,
      );
    }
  } else if (targetId !== null) {
    throw new Error(`Action '${action.id}' does not accept a target.`);
  }

  const moodIds = new Set(request.moods);
  const moodSignals = parsed.mood_signals ?? [];
  for (const signal of moodSignals) {
    if (!moodIds.has(signal.id)) {
      throw new Error(`Mood '${signal.id}' is not registered by the runtime.`);
    }
  }

  return {
    status: "ok",
    reply_text: parsed.reply_text,
    response_emotion: parsed.response_emotion,
    action: { id: action.id, target_id: targetId },
    mood_signals: moodSignals,
  };
}

export interface GatewayErrorBody {
  error: {
    code: string;
    message: string;
    request_id: string;
  };
}

export const SpontaneousReplyRequestSchema = z.object({
  request_id: z.string().min(1).max(128),
  trigger_id: z.string().min(1).max(256),
  event_description: z.string().trim().min(1).max(1000),
  content_requirements: z.array(
    z.string().trim().min(1).max(500),
  ).min(1).max(16),
  rules: z.array(z.string().trim().min(1).max(500)).min(1).max(32),
  persona: PersonaSchema,
}).strict();

export type SpontaneousReplyRequest = z.infer<
  typeof SpontaneousReplyRequestSchema
>;

const SpontaneousReplyModelSchema = z.object({
  status: z.literal("ok").optional(),
  reply_text: z.string().trim().min(1),
  response_emotion: z.enum(["normal", "happy", "sad", "angry"]),
}).strict();

export interface SpontaneousReply {
  status: "ok";
  reply_text: string;
  response_emotion: "normal" | "happy" | "sad" | "angry";
}

export function parseSpontaneousReply(
  value: unknown,
  maxReplyCharacters: number,
): SpontaneousReply {
  const parsed = SpontaneousReplyModelSchema.parse(value);
  if (parsed.reply_text.length > maxReplyCharacters) {
    throw new Error(`reply_text exceeds ${maxReplyCharacters} characters.`);
  }
  return {
    status: "ok",
    reply_text: parsed.reply_text,
    response_emotion: parsed.response_emotion,
  };
}
