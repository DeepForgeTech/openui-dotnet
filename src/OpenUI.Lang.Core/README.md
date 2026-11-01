# OpenUI.Lang.Core

A .NET implementation of [OpenUI Lang](https://github.com/thesysdev/openui), the compact, streaming-first language that models write to describe UI. It parses a model's response while it streams in, checks it against your component library, evaluates state, queries and actions, merges edits, and builds the system prompt that teaches the model your components. It has no dependencies beyond the .NET base library.

```sh
dotnet add package OpenUI.Lang.Core
```

```csharp
using OpenUI.Lang;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;

var library = OpenUILibrary.CreateBuilder("demo")
    .Root("Stack")
    .Add(new ComponentSpec("Stack", "Lays out its children", [new PropSpec("children", new ArrayShape(new ComponentRefShape([])), true)]))
    .Add(new ComponentSpec("Metric", "One headline number", [new PropSpec("label", TypeShape.String, true), new PropSpec("value", TypeShape.Number, true)]))
    .Build();

var document = new OpenUIParser(library).Parse("root = Stack([Metric(\"Open tickets\", 3)])");
var result = OpenUIEvaluator.Evaluate(document);

Console.WriteLine(result.Root!.Type);        // Stack
Console.WriteLine(result.Errors.Count);      // 0
```

`new OpenUIParser(library).CreateStream()` parses a reply chunk by chunk, and `OpenUISession` also runs queries, mutations and actions. `SystemPrompt.Generate(library.ToSpec())` builds the system prompt. The [repository README](https://github.com/DeepForgeTech/openui-dotnet#openuilangcore) has a longer example.

This implementation is checked against the spec and conformance fixtures of [thesysdev/openui at commit `7df27bc`](https://github.com/thesysdev/openui/tree/7df27bc8eeb6fd15ac318ca7745d225a8e16e9b9), and is an independent project, not part of upstream OpenUI.

MIT license. See `THIRD_PARTY_NOTICES.md` in the package for the upstream material.
