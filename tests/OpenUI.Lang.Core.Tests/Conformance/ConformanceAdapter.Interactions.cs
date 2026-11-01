using System.Text.Json;
using System.Text.Json.Nodes;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Conformance;

internal static partial class ConformanceAdapter
{
    /// <summary>
    /// Level 2. Mounts the program in a session, replays interactions.json and returns
    /// <c>{ "events": [...], "state": {...}, "toolCalls": [...] }</c>.
    /// </summary>
    public static JsonObject RunInteractions(string caseDirectory, JsonObject library) =>
        RunInteractionsAsync(caseDirectory, library).GetAwaiter().GetResult();

    private static async Task<JsonObject> RunInteractionsAsync(string caseDirectory, JsonObject library)
    {
        var spec = JsonNode.Parse(File.ReadAllText(Path.Combine(caseDirectory, "interactions.json")))!.AsObject();
        var steps = spec["interactions"]!.AsArray().Select(s => s!.AsObject()).ToList();
        var toolCalls = new JsonArray();
        var tools = new OpenUITools();
        void SetTool(string name, JsonNode? result) => tools.Add(name, OpenUIToolEffect.Read, (args, _) =>
        {
            lock (toolCalls)
            {
                toolCalls.Add(new JsonObject { ["tool"] = name, ["args"] = Summarize(args) });
            }

            return ValueTask.FromResult(ToValue(result));
        });

        // Tool steps before the first user step apply before the program mounts.
        var i = 0;
        for (; i < steps.Count && steps[i].ContainsKey("tool"); i++)
        {
            SetTool(steps[i]["tool"]!.GetValue<string>(), steps[i]["result"]);
        }

        var events = new RecordingEvents();
        await using var session = OpenUISession.Create(CreateLibrary(library), new OpenUISessionOptions { Tools = tools, Events = events });
        session.Load(File.ReadAllText(Path.Combine(caseDirectory, "input.oui")));
        await session.WhenIdleAsync();

        for (; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.ContainsKey("tool"))
            {
                SetTool(step["tool"]!.GetValue<string>(), step["result"]);
                continue;
            }

            var renderer = TestRenderer.Mount(session);
            var typing = step["type"] as JsonArray;
            if (typing is null)
            {
                await renderer.Click(step["click"]!.GetValue<string>());
            }
            else
            {
                renderer.Type(typing[0]!.GetValue<string>(), typing[1]!.GetValue<string>());
            }

            await session.WhenIdleAsync();
        }

        return new JsonObject
        {
            ["events"] = new JsonArray([.. events.Items]),
            ["state"] = new JsonObject([.. session.State.Snapshot().Select(p => KeyValuePair.Create(p.Key, Summarize(p.Value)))]),
            ["toolCalls"] = toolCalls,
        };
    }

    private sealed class RecordingEvents : IOpenUIHostEventHandler
    {
        public List<JsonNode> Items { get; } = [];

        public ValueTask OnEventAsync(OpenUIHostEvent e, CancellationToken ct)
        {
            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                e.WriteTo(writer);
            }

            lock (Items)
            {
                Items.Add(JsonNode.Parse(buffer.WrittenSpan)!);
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// The test renderers of the reference runner: a form names the form of its children, <c>Input</c>
    /// and <c>Button</c> register handlers by statement id, and any other component renders all its props.
    /// </summary>
    private sealed class TestRenderer
    {
        private readonly OpenUISession _session;
        private readonly Dictionary<string, Func<Task>> _clicks = [];
        private readonly Dictionary<string, Action<string>> _inputs = [];

        private TestRenderer(OpenUISession session) => _session = session;

        public static TestRenderer Mount(OpenUISession session)
        {
            var renderer = new TestRenderer(session);
            renderer.Render(session.Current.Root, formName: null);
            return renderer;
        }

        public Task Click(string id) => _clicks.TryGetValue(id, out var click)
            ? click()
            : throw new InvalidOperationException($"Nothing rendered for {id}.");

        public void Type(string id, string text)
        {
            if (!_inputs.TryGetValue(id, out var input))
            {
                throw new InvalidOperationException($"Nothing rendered for {id}.");
            }

            input(text);
        }

        private void Render(OuiValue value, string? formName)
        {
            if (value.TryGetArray(out var list))
            {
                foreach (var item in list)
                {
                    Render(item, formName);
                }

                return;
            }

            if (!value.TryGetComponent(out var component))
            {
                return;
            }

            var id = component.StatementId;
            switch (component.Type)
            {
                case "Input":
                    if (id is not null)
                    {
                        _inputs[id] = text => WriteInput(component, formName, text);
                    }

                    break;
                case "Button":
                    if (id is not null)
                    {
                        _clicks[id] = () => RunAction(component, formName);
                    }

                    break;
                default:
                    foreach (var prop in component.Props)
                    {
                        Render(prop.Value, component.DeclaredFormName ?? formName);
                    }

                    break;
            }
        }

        // A bound $variable takes the plain value; a form field records its componentType.
        private void WriteInput(OuiComponent input, string? formName, string text)
        {
            if (input["value"].Binding is { } binding)
            {
                _session.State.Write(binding, text);
                return;
            }

            input["name"].Value.TryGetString(out var name);
            _session.Forms.Set(formName, name ?? "", text, input.Spec!.Traits.FieldComponentType!);
        }

        private async Task RunAction(OuiComponent button, string? formName)
        {
            if (button["action"].Value.TryGetAction(out var action))
            {
                await _session.RunActionAsync(action, new ActionOrigin(button, formName));
            }
        }
    }
}
