import assert from "node:assert/strict";
import test from "node:test";
import {
  createConversationRequestSchema,
  parseConversationReply,
  type ConversationRequest,
} from "../src/contracts.js";

function request(): ConversationRequest {
  return {
    session_id: "session-1",
    request_id: "request-1",
    input: "去看看桌子",
    history: [],
    actions: [{
      id: "visit",
      description: "Visit an item",
      target_capability: "visit",
    }],
    targets: [{
      id: "table-1",
      name: "桌子",
      state: "default",
      capabilities: ["visit"],
    }],
    moods: ["happy"],
    persona: {
      schema_version: 1,
      character_id: "lengyue",
      name: "冷月",
      age: 16,
      gender: "female",
      height_cm: 155,
      personality: [
        { id: "extraversion", display_name: "外向性", option_id: "balanced", option_name: "平衡", description: "自然交流。" },
        { id: "conscientiousness", display_name: "尽责性", option_id: "flexible", option_name: "灵活", description: "灵活安排。" },
        { id: "openness", display_name: "开放性", option_id: "pragmatic", option_name: "务实", description: "关注实际。" },
        { id: "agreeableness", display_name: "亲和性", option_id: "warm", option_name: "温暖", description: "温暖真实。" },
        { id: "emotional_stability", display_name: "情绪稳定性", option_id: "natural", option_name: "自然", description: "自然表达情绪。" },
      ],
    },
    runtime: {
      schema_version: 1,
      revision: 1,
      appearance: { outfit_id: "lavender-casual", display_name: "薰衣草常服" },
      mood: { id: "neutral", level: "none" },
      posture: "standing",
      actions: [],
      recent_events: [],
      world: {
        schema_version: 1,
        revision: 2,
        player: {
          nearby: true,
          proximity: "within-reach",
          distance_m: 0.7,
          dialogue_open: true,
          interaction_mode: "eye-hand",
          focused_item: {
            id: "table-1",
            name: "桌子",
            state: "default",
            control: "available",
            distance: "near",
          },
        },
        held_items: [],
        activity: null,
        active_facts: [{
          subject_id: "kettle-1",
          subject_name: "电热水壶",
          category: "appliance",
          state: "heating",
          value: "water_ml=600;temperature_c=72;progress=65%",
        }],
        inventory_alerts: [],
        environment: {
          revision: 1,
          source: "PICO Room Capture",
          confidence: "high",
          count_quality: "observed",
          floor_area_m2: 18.2,
          width_m: 4.5,
          length_m: 4.1,
          height_m: 2.7,
          shape: "rectangular",
          open_space: "balanced",
          floor_count: 1,
          semantic_counts: [{
            id: "table",
            name: "桌子",
            count: 1,
            estimated: false,
          }],
          zones: [{ id: "dining", description: "table or seating area" }],
          relations: [],
        },
        virtual_world: {
          revision: 2,
          total_count: 1,
          omitted_group_count: 0,
          item_groups: [{
            definition_id: "table",
            name: "桌子",
            state: "default",
            control: "available",
            count: 1,
          }],
          control_counts: [{ control: "available", count: 1 }],
          salient_items: [{
            id: "table-1",
            name: "桌子",
            state: "default",
            control: "available",
            distance: "near",
          }],
        },
      },
    },
  };
}

test("request schema rejects fields outside the gateway contract", () => {
  const value = { ...request(), api_key: "must-not-cross-client-boundary" };
  assert.equal(createConversationRequestSchema(16).safeParse(value).success, false);
});

test("Unity empty target capability is normalized at the wire boundary", () => {
  const value = request();
  value.actions = [{
    id: "stay",
    description: "Stay here",
    target_capability: "",
  }];
  const parsed = createConversationRequestSchema(16).parse(value);
  assert.equal(parsed.actions[0]?.target_capability, null);
});

test("persona fixed identity and runtime action enums are enforced", () => {
  const invalidIdentity: unknown = {
    ...request(),
    persona: { ...request().persona, age: 17 },
  };
  assert.equal(createConversationRequestSchema(16).safeParse(invalidIdentity).success, false);

  const value = request();
  value.runtime.actions = [{
    id: "sit",
    description: "Sit on a chair",
    state: "running",
    phase: "seated",
    initiator: "player",
    elapsed_seconds: 3,
    target: { id: "chair-1", name: "椅子", state: "default" },
  }];
  assert.equal(createConversationRequestSchema(16).safeParse(value).success, true);

  value.runtime.actions[0]!.target = { id: "", name: "", state: "" };
  const parsed = createConversationRequestSchema(16).parse(value);
  assert.equal(parsed.runtime.actions[0]?.target, null);
});

test("bounded world context is optional for old clients and rejects oversized arrays", () => {
  const legacy = request();
  delete legacy.runtime.world;
  assert.equal(createConversationRequestSchema(16).safeParse(legacy).success, true);

  const oversized = request();
  oversized.runtime.world!.active_facts = Array.from({ length: 13 }, (_, index) => ({
    subject_id: `device-${index}`,
    subject_name: "设备",
    category: "device",
    state: "active",
    value: "",
  }));
  assert.equal(createConversationRequestSchema(16).safeParse(oversized).success, false);
});

test("reply validation is driven by the runtime action and target catalogs", () => {
  const reply = parseConversationReply({
    status: "ok",
    reply_text: "好，我去看看。",
    response_emotion: "happy",
    action: { id: "visit", target_id: "table-1" },
    mood_signals: [{ id: "happy", delta: 4 }],
  }, request(), 600);
  assert.equal(reply.action.id, "visit");
  assert.equal(reply.action.target_id, "table-1");
  assert.equal(reply.response_emotion, "happy");

  assert.throws(() => parseConversationReply({
    reply_text: "执行一个不存在的动作。",
    response_emotion: "normal",
    action: { id: "invented", target_id: null },
    mood_signals: [],
  }, request(), 600), /not in the runtime action catalog/);
});

test("reply emotion is required and limited to the four runtime audio states", () => {
  const baseReply = {
    reply_text: "我明白了。",
    action: { id: "none", target_id: null },
    mood_signals: [],
  };
  assert.throws(() => parseConversationReply(baseReply, request(), 600));
  assert.throws(() => parseConversationReply({
    ...baseReply,
    response_emotion: "surprised",
  }, request(), 600));
  for (const response_emotion of ["normal", "happy", "sad", "angry"]) {
    assert.doesNotThrow(() => parseConversationReply({
      ...baseReply,
      response_emotion,
    }, request(), 600));
  }
});

test("target capabilities and mood ids are validated without name-based rules", () => {
  const value = request();
  value.targets[0] = {
    ...value.targets[0]!,
    name: "任意内容名称",
  };
  assert.doesNotThrow(() => parseConversationReply({
    reply_text: "我过去看看。",
    response_emotion: "normal",
    action: { id: "visit", target_id: "table-1" },
    mood_signals: [],
  }, value, 600));
  assert.throws(() => parseConversationReply({
    reply_text: "心情变化。",
    response_emotion: "sad",
    action: { id: "none", target_id: null },
    mood_signals: [{ id: "unregistered", delta: 1 }],
  }, value, 600), /not registered/);
});
