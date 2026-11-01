using System.Globalization;
using OpenUI.Lang.Library;

namespace OpenUI.Lang.Prompting;

/// <summary>
/// Assembles the prompt sections in the order of prompt.md, section 4.4. Sections are joined by a blank
/// line; the closing rules follow the last section after a single line break.
/// </summary>
internal sealed class PromptBuilder(LibrarySpec library, ResolvedPromptOptions options)
{
    private const string DefaultRoot = "Root";

    private readonly string root = library.Root ?? DefaultRoot;

    // Expressions, $variables and builtins over data come with either tool calls or bindings.
    private readonly bool expressions = options.ToolCalls || options.Bindings;

    private readonly SignatureFeatures features = SignatureFeatures.Of(library);

    public string Build()
    {
        string?[] sections =
        [
            options.Preamble,
            Syntax(),
            Components(),
            Functions(),
            options.ToolCalls ? PromptText.Query : null,
            options.ToolCalls ? PromptText.Mutation : null,
            Actions(),
            options.ToolCalls && options.Bindings ? PromptText.InteractiveFilters : null,
            options.ToolCalls && options.Bindings ? PromptText.Forms : null,
            options.ToolCalls ? PromptText.DataWorkflow : null,
            Tools(),
            Hoisting(),
            Examples(),
            options.EditMode ? PromptText.EditMode : null,
            options.InlineMode ? PromptText.InlineMode : null,
        ];

        return Blocks(sections.OfType<string>()) + "\n" + Closing();
    }

    private string Syntax()
    {
        var rules = new List<string>(PromptText.CoreSyntaxRules(root));
        if (options.Bindings)
        {
            rules.Add(PromptText.StateRule);
        }

        if (expressions)
        {
            rules.AddRange(PromptText.ExpressionRules);
        }

        return PromptText.SyntaxHeader + "\n\n" + Numbered(rules) + "\n" + PromptText.StringsRule;
    }

    private string Components()
    {
        List<string> header = [PromptText.ComponentsHeader];
        if (features.HasActionSlots)
        {
            header.Add(PromptText.ActionExpressionNote(string.Join(", ", ActionStepNames())));
        }

        if (features.RestrictedActionSlots)
        {
            header.Add(PromptText.RestrictedActionNote);
        }

        if (features.BindingProps || options.Bindings)
        {
            header.Add(PromptText.BindingNote);
        }

        return Lines(header) + "\n\n" + ComponentList();
    }

    private IEnumerable<string> ActionStepNames()
    {
        if (options.ToolCalls)
        {
            yield return "@Run";
        }

        yield return "@ToAssistant";
        yield return "@OpenUrl";
        if (options.Bindings)
        {
            yield return "@Set";
            yield return "@Reset";
        }

        foreach (var action in library.Actions)
        {
            yield return "@" + action.Name;
        }
    }

    // Grouped components each appear once, under the first group that names them; names that are not
    // components are skipped, and the rest go under "Other" (section 4.4).
    private string ComponentList()
    {
        if (library.ComponentGroups is not { IsEmpty: false } groups)
        {
            return Lines(library.Components.Select(EntryLine));
        }

        var byName = new Dictionary<string, LibrarySpecEntry>(StringComparer.Ordinal);
        foreach (var component in library.Components)
        {
            byName.TryAdd(component.Name, component);
        }

        var listed = new HashSet<string>(StringComparer.Ordinal);
        var blocks = new List<string>();
        foreach (var group in groups)
        {
            var members = group.Components.Where(name => byName.ContainsKey(name) && listed.Add(name)).Select(name => byName[name]);
            blocks.Add(Group(group.Name, members.Select(EntryLine).Concat(group.Notes ?? [])));
        }

        var others = library.Components.Where(component => !listed.Contains(component.Name)).ToList();
        if (others.Count > 0)
        {
            blocks.Add(Group(PromptText.OtherGroup, others.Select(EntryLine)));
        }

        return Blocks(blocks);
    }

    private string? Functions()
    {
        if (!options.BuiltinFunctions && library.Functions.IsEmpty)
        {
            return null;
        }

        var custom = library.Functions.Select(function => "@" + EntryLine(function));
        if (!options.BuiltinFunctions)
        {
            return Blocks([PromptText.FunctionsHeader, Lines(custom)]);
        }

        var target = options.ToolCalls ? "Query results" : "data";
        return Blocks(
        [
            PromptText.FunctionsHeader,
            PromptText.BuiltinsIntro(target),
            Lines(PromptText.BuiltinFunctions.Concat(custom)),
            PromptText.BuiltinsGuide,
        ]);
    }

    // The full section needs an action slot to teach; custom actions alone get the short form.
    private string? Actions()
    {
        var custom = library.Actions.Select(action => "@" + EntryLine(action));
        if (features.HasActionSlots)
        {
            return FullActions(custom);
        }

        return library.Actions.IsEmpty
            ? null
            : Blocks([PromptText.ActionHeader, PromptText.CustomActionsIntro, Steps(custom)]);
    }

    private string FullActions(IEnumerable<string> custom)
    {
        List<string> steps = [];
        if (options.ToolCalls)
        {
            steps.Add(PromptText.RunStep);
        }

        steps.AddRange([PromptText.ToAssistantStep, PromptText.OpenUrlStep]);
        if (options.Bindings)
        {
            steps.AddRange([PromptText.SetStep, PromptText.ResetStep]);
        }

        steps.AddRange(custom);

        List<string> parts = [PromptText.ActionHeader, PromptText.ActionIntro, Steps(steps)];
        if (options.ToolCalls)
        {
            parts.Add(PromptText.MutationExample);
        }

        parts.Add(PromptText.NavigationExample);
        parts.Add(options.ToolCalls ? PromptText.AssignActionNote + "\n" + PromptText.RunNotes : PromptText.AssignActionNote);
        return Blocks(parts);
    }

    // Listed whenever the host passes tools, even with tool calls off, as the reference generator does.
    // Bare names come first, then the described tools, each in the order given.
    private string? Tools()
    {
        if (options.Tools.IsEmpty)
        {
            return null;
        }

        var listed = options.Tools.Where(IsBareName).Concat(options.Tools.Where(tool => !IsBareName(tool)));
        List<string> parts = [PromptText.ToolsHeader, Lines(listed.Select(ToolLine))];
        var defaults = options.Tools
            .Where(tool => tool.OutputSchema is not null)
            .Select(tool => $"- {tool.Name}: `{ToolSchemaText.DefaultJson(tool.OutputSchema!.Value)}`")
            .ToList();
        if (defaults.Count > 0)
        {
            parts.Add(PromptText.ToolDefaultsHeader + "\n" + Lines(defaults));
        }

        parts.Add(PromptText.ToolsWarning);
        return Blocks(parts);
    }

    private static bool IsBareName(ToolDescriptor tool) =>
        tool is { Description: null, InputSchema: null, OutputSchema: null, Annotations: null };

    private static string ToolLine(ToolDescriptor tool)
    {
        var line = "- " + tool.Name;
        if (tool.InputSchema is not null || tool.OutputSchema is not null)
        {
            var output = tool.OutputSchema is { } schema ? ToolSchemaText.TypeString(schema) : "any";
            line += $"({ToolSchemaText.Params(tool.InputSchema)}) → {output}";
        }

        return string.IsNullOrEmpty(tool.Description) ? line : line + "\n  " + tool.Description;
    }

    private string Hoisting()
    {
        List<string> order = [PromptText.RootFirstStep(root)];
        if (expressions)
        {
            order.Add(PromptText.VariablesStep);
        }

        if (options.ToolCalls)
        {
            order.Add(PromptText.QueriesStep);
        }

        order.Add(expressions ? PromptText.DataReadyComponentsStep : PromptText.StreamingComponentsStep);
        order.Add(PromptText.DataValuesStep);
        return PromptText.HoistingHeader + "\n" + Numbered(order) + "\n\n" + PromptText.RootFirstReminder(root);
    }

    // The section ends with its own line break, so two blank lines separate it from what follows.
    private string? Examples()
    {
        var examples = options.Examples.Concat(options.ToolExamples).ToList();
        return examples.Count == 0 ? null : PromptText.ExamplesHeader + "\n\n" + Blocks(examples) + "\n";
    }

    private string Closing()
    {
        List<string> checks = [PromptText.RootFirstCheck(root), PromptText.ReferencesCheck];
        if (options.ToolCalls)
        {
            checks.Add(PromptText.QueriesCheck);
        }

        if (options.Bindings)
        {
            checks.Add(PromptText.BindingsCheck);
        }

        if (options.ToolCalls && options.Bindings)
        {
            checks.Add(PromptText.FiltersCheck);
        }

        var closing = PromptText.ImportantRules + "\n\n" + PromptText.VerificationHeader + "\n" + Numbered(checks);
        return options.AdditionalRules.IsEmpty
            ? closing
            : closing + "\n\n" + Lines(options.AdditionalRules.Select(rule => "- " + rule));
    }

    private static string EntryLine(LibrarySpecEntry entry) =>
        string.IsNullOrEmpty(entry.Description) ? entry.Signature : entry.Signature + PromptText.SignatureSeparator + entry.Description;

    private static string Group(string name, IEnumerable<string> lines) => "### " + name + "\n" + Lines(lines);

    private static string Steps(IEnumerable<string> steps) =>
        PromptText.StepsHeader + "\n" + Lines(steps.Select(step => "- " + step));

    private static string Numbered(IEnumerable<string> items) =>
        Lines(items.Select((item, index) => (index + 1).ToString(CultureInfo.InvariantCulture) + ". " + item));

    private static string Lines(IEnumerable<string> lines) => string.Join("\n", lines);

    private static string Blocks(IEnumerable<string> blocks) => string.Join("\n\n", blocks);

    /// <summary>What the component signatures use, read from their printed param types (prompt.md, section 5.1).</summary>
    private readonly record struct SignatureFeatures(bool ActionExpressionSlots, bool RestrictedActionSlots, bool BindingProps)
    {
        public bool HasActionSlots => ActionExpressionSlots || RestrictedActionSlots;

        public static SignatureFeatures Of(LibrarySpec library)
        {
            bool actionExpression = false, restricted = false, binding = false;
            foreach (var type in library.Components.SelectMany(component => SignatureText.ParamTypes(component.Signature)))
            {
                binding |= type.StartsWith("$binding<", StringComparison.Ordinal);
                actionExpression |= type.Contains("ActionExpression", StringComparison.Ordinal);
                restricted |= type.Split('|').Any(part => part.Trim().StartsWith('@'));
            }

            return new SignatureFeatures(actionExpression, restricted, binding);
        }
    }
}
