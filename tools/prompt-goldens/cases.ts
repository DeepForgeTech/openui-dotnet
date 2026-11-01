// The prompt golden cases: one entry per upstream generateSystemPrompt call.
// `library` names a LibrarySpec in `libraries` below; `options` is passed to
// upstream as `promptOptions` and stored beside the golden for the .NET test.

export type LibrarySpec = Record<string, any>;

export interface PromptCase {
  name: string;
  library: string;
  options: Record<string, unknown>;
}

const orderTools = [
  {
    name: "list_orders",
    description: "List orders, newest first",
    inputSchema: {
      type: "object",
      properties: {
        status: { type: "string", enum: ["open", "shipped", "cancelled"] },
        limit: { type: "integer" },
        customer: {
          type: "object",
          properties: { id: { type: "string" }, vip: { type: "boolean" } },
          required: ["id"],
        },
      },
      required: ["status"],
    },
    outputSchema: {
      type: "object",
      properties: {
        total: { type: "number" },
        rows: {
          type: "array",
          items: {
            type: "object",
            properties: {
              id: { type: "string" },
              status: { type: "string", enum: ["open", "shipped"] },
              tags: { type: "array", items: { type: "string" } },
              meta: { type: "object" },
            },
            required: ["id"],
          },
        },
        next: { type: "string" },
      },
    },
    annotations: { readOnlyHint: true },
  },
  {
    name: "create_order",
    description: "Create an order",
    inputSchema: {
      type: "object",
      properties: { sku: { type: "string" }, qty: { type: "number" }, notes: { type: "array" } },
      required: ["sku", "qty"],
    },
    outputSchema: {
      type: "object",
      properties: { id: { type: "string" }, ok: { type: "boolean" } },
    },
    annotations: { destructiveHint: false },
  },
  {
    // No description, empty object schemas, an unknown type and integer-like property names,
    // which JS orders before the others.
    name: "ping",
    inputSchema: { type: "object", properties: {} },
    outputSchema: { type: "object" },
  },
  {
    name: "lookup",
    description: "Edge-case schemas",
    inputSchema: {
      type: "object",
      properties: {
        zeta: { type: ["string", "null"] },
        "10": { type: "string" },
        "2": { type: "number" },
        name: { type: "string", enum: [] },
        "naïve \"key\"": { type: "boolean" },
      },
      required: ["2", "zeta"],
    },
    outputSchema: {
      type: "object",
      properties: {
        zeta: { type: "string" },
        "10": { type: "number" },
        "2": { type: "boolean" },
        "naïve \"key\"\n": { type: "array", items: { type: "number" } },
        ["__proto__"]: { type: "string" },
        // JSON.stringify leaves these alone except the control character.
        "emoji 😀 <b>&'/\\": { type: "string" },
        "ctl\u0001\u007f\u2028": { type: "number" },
        nested: { type: "object", properties: { b: { type: "string" }, a: { type: "number" } } },
        anything: {},
      },
    },
  },
];

const textOptions = {
  preamble: "You are Orders Assistant. Reply with openui-lang only.",
  examples: [
    'Example — a card:\n```\nroot = Card("Hi", [])\n```',
    "Example — two metrics:\n```\nroot = Stack([a, b])\na = Metric(\"A\", 1)\nb = Metric(\"B\", 2)\n```",
  ],
  toolExamples: ['Example — live data:\n```\nrows = Query("list_orders", {}, {rows: []})\n```'],
  additionalRules: ["Never invent order ids.", "Keep titles under 40 characters."],
};

export const cases: PromptCase[] = [
  { name: "defaults", library: "fixtures", options: {} },
  { name: "tool-calls-string-tools", library: "fixtures", options: { tools: ["list_orders", "create_order"] } },
  { name: "tool-calls-object-tools", library: "fixtures", options: { tools: orderTools.slice(0, 2) } },
  {
    name: "tool-calls-mixed-tools",
    library: "fixtures",
    options: { tools: [orderTools[1], "list_orders", orderTools[2], orderTools[3]] },
  },
  { name: "tool-calls-no-tools", library: "fixtures", options: { toolCalls: true } },
  { name: "tool-calls-empty-tools", library: "fixtures", options: { tools: [] } },
  { name: "tools-with-tool-calls-off", library: "fixtures", options: { tools: ["list_orders"], toolCalls: false } },
  { name: "tool-calls-without-bindings", library: "fixtures", options: { toolCalls: true, bindings: false } },
  { name: "bindings-only", library: "fixtures", options: { bindings: true } },
  { name: "builtin-functions-only", library: "fixtures", options: { builtinFunctions: true } },
  { name: "builtin-functions-off", library: "fixtures", options: { toolCalls: true, builtinFunctions: false } },
  { name: "edit-mode", library: "fixtures", options: { editMode: true } },
  { name: "inline-mode", library: "fixtures", options: { inlineMode: true } },
  { name: "text-extras", library: "fixtures", options: textOptions },
  { name: "empty-text-extras", library: "fixtures", options: { preamble: "", examples: [], toolExamples: [], additionalRules: [] } },
  {
    name: "everything",
    library: "fixtures",
    options: { tools: orderTools, bindings: true, builtinFunctions: true, editMode: true, inlineMode: true, ...textOptions },
  },
  { name: "component-groups", library: "grouped", options: {} },
  { name: "component-groups-with-tools", library: "grouped", options: { tools: ["list_orders"], editMode: true } },
  { name: "no-functions-or-actions", library: "no-functions-or-actions", options: {} },
  { name: "no-functions-or-actions-tools", library: "no-functions-or-actions", options: { tools: orderTools.slice(0, 1) } },
  { name: "minimal-library", library: "minimal", options: {} },
  { name: "minimal-library-bindings", library: "minimal", options: { bindings: true } },
  { name: "actions-without-slots", library: "actions-without-slots", options: {} },
  { name: "actions-without-slots-tool-calls", library: "actions-without-slots", options: { toolCalls: true } },
  { name: "sparse-descriptions", library: "sparse", options: { builtinFunctions: true } },
];

// ─── Library variants (the "fixtures" library is tests/fixtures/1.0/library.json) ──

export function buildLibraries(fixtures: LibrarySpec): Record<string, LibrarySpec> {
  const clone = () => structuredClone(fixtures);

  const grouped = clone();
  grouped.componentGroups = [
    { name: "Layout", components: ["Stack", "Card"], notes: ["Prefer Stack for rows.", "Cards may nest."] },
    // Ghost does not exist and Card is already grouped: both are skipped.
    { name: "Content", components: ["Text", "Ghost", "Tag", "Card", "Metric"] },
    { name: "Forms", components: ["Form", "Input"], notes: [] },
  ];

  const noFunctionsOrActions = clone();
  delete noFunctionsOrActions.functions;
  delete noFunctionsOrActions.actions;
  delete noFunctionsOrActions.schema.functions;
  delete noFunctionsOrActions.schema.actions;

  const minimal = {
    components: {
      Card: { signature: "Card(title: string, children?: any[])", description: "A container" },
      Text: { signature: "Text(text: string)", description: "A run of text" },
    },
  };

  const actionsWithoutSlots = {
    id: "plain",
    root: "Card",
    components: minimal.components,
    functions: { Upper: fixtures.functions.Upper },
    actions: fixtures.actions,
  };

  const sparse = {
    root: "Page",
    components: {
      Page: { signature: "Page(children: any[])" },
      Field: { signature: "Field(name: string, value?: $binding<string>)", description: "" },
      Link: { signature: "Link(label: string, to?: @OpenUrl | @ToAssistant)", description: "A link" },
    },
    functions: { Slug: { signature: "Slug(text: string) → string" } },
    actions: { Log: { signature: "Log(message: string)", description: "" } },
  };

  return {
    fixtures: fixtures,
    grouped,
    "no-functions-or-actions": noFunctionsOrActions,
    minimal,
    "actions-without-slots": actionsWithoutSlots,
    sparse,
  };
}
