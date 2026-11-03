# Changelog

## 1.1.0

First release of `OpenUI.Lang.Blazor` and `OpenUI.Lang.Blazor.Mud`.

- `OpenUI.Lang.Blazor` draws a program with the views a package registers, and knows no UI library.
- `OpenUI.Lang.Blazor.Mud` draws the components with MudBlazor, and adds `ThemeSwitcher` for a light, dark or system theme.
- `OpenUI.Lang.Core` adds component sources, which add to each other. `OpenUI.Lang.Blazor` adds `AddOpenUIComponents` to register one.
- `OpenUI.Lang.AspNetCore` adds an `AddOpenUI` that composes the prompt from those sources.

## 1.0.0

First release of `OpenUI.Lang.Core` and `OpenUI.Lang.AspNetCore`.

- `OpenUI.Lang.Core` parses OpenUI Lang while it streams, checks it against a component library, evaluates state, queries and actions, merges edits, writes a tree back out as text, and generates the system prompt. It passes all 59 program cases and 12 interaction cases of the conformance fixtures from [thesysdev/openui at `7df27bc`](https://github.com/thesysdev/openui/tree/7df27bc8eeb6fd15ac318ca7745d225a8e16e9b9).
- `OpenUI.Lang.AspNetCore` adds `AddOpenUI` and `MapOpenUIChat`, which serve the stock `@openuidev/react-ui` chat from any Microsoft.Extensions.AI `IChatClient`.
- `OpenUI.Lang.Blazor` and `OpenUI.Lang.Blazor.Mud` are in the repository but not packaged yet.
