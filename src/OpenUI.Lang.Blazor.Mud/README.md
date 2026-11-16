# OpenUI.Lang.Blazor.Mud

[OpenUI Lang](https://github.com/thesysdev/openui) components drawn with [MudBlazor](https://mudblazor.com), for [OpenUI.Lang.Blazor](https://www.nuget.org/packages/OpenUI.Lang.Blazor): 63 drawn components and their 20 items, such as cards, tables, charts, forms, tabs, dates and safe markdown.

The package's version is the MudBlazor version it was built against. Add the one that matches yours: `9.11.0` for MudBlazor 9, `8.10.0` for MudBlazor 8. A fix on the same MudBlazor version adds a fourth number, as in `9.11.0.1`.

```sh
dotnet add package OpenUI.Lang.Blazor.Mud
```

```csharp
builder.Services.AddMudServices();
builder.Services.AddOpenUIComponents<MudComponents>();
```

```razor
<OpenUIProgram Program="@text" />
```

The page needs MudBlazor's stylesheet and script, and an interactive layout with `MudThemeProvider` and `MudPopoverProvider`; without them a `Select` does not open. `ThemeSwitcher` and `AddMudThemeMode` let a person pick light, dark or their system's theme.

`MudDefinitions` is public, so a host with its own design system can teach the same components and draw them with its own views, and `MudIcons` maps icon names onto MudBlazor's. The [repository README](https://github.com/DeepForgeTech/openui-dotnet#drawing-it-in-blazor) has the full setup.

This is an independent project, not part of upstream OpenUI or MudBlazor. MIT license. See `THIRD_PARTY_NOTICES.md` in the package for the upstream material.
