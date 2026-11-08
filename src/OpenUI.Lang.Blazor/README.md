# OpenUI.Lang.Blazor

Draws [OpenUI Lang](https://github.com/thesysdev/openui) programs in Blazor, under any render mode, while they stream. It is the renderer and nothing else: the components come from the packages a host registers, such as [OpenUI.Lang.Blazor.Mud](https://www.nuget.org/packages/OpenUI.Lang.Blazor.Mud), or one you write.

```sh
dotnet add package OpenUI.Lang.Blazor
```

```csharp
builder.Services.AddOpenUIComponents<YourComponents>();
```

```razor
<OpenUIProgram Program="@text" />
```

- `IOpenUIStateBinding` keeps a program's `$variables` equal to a store the host already has, passed as `StateBinding`.
- `ShowAllErrors`, on a program or through `OpenUIViewOptions`, draws errors even when the program renders.
- A component package defines its components once with `LibraryDefinition`, which is the parser's library and the system prompt too, and checks itself in a unit test with `FindProblems()`.
- Views derive from `OpenUIComponent`, or from `OpenUIWritableComponent` when the user writes through them. A view draws again only when its props change by value.

The [repository README](https://github.com/DeepForgeTech/openui-dotnet#drawing-it-in-blazor) covers hosting a program and writing a component package.

This is an independent project, not part of upstream OpenUI. MIT license. See `THIRD_PARTY_NOTICES.md` in the package for the upstream material.
