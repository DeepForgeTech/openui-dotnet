# Changelog

## 1.1.0

First release of `OpenUI.Lang.Blazor` and `OpenUI.Lang.Blazor.Mud`.

- `OpenUI.Lang.Blazor` draws a program with the views a package registers, and knows no UI library.
- `OpenUI.Lang.Blazor.Mud` draws the components with MudBlazor, and adds `ThemeSwitcher` for a light, dark or system theme. It ships once per MudBlazor line, versioned as the MudBlazor it was built against: `8.10.0` and `9.11.0`.
- `MudDefinitions` and `MudIcons` are public, so a host can teach the package's components and draw them with its own views. `ChartValues`, `DateValues` and `DisplayValues` are public in `OpenUI.Lang.Blazor`.
- `OpenUI.Lang.Core` adds component sources, which add to each other. `OpenUI.Lang.Blazor` adds `AddOpenUIComponents` to register one.
- `OpenUI.Lang.AspNetCore` adds an `AddOpenUI` that composes the prompt from those sources.
- `OpenUI.Lang.Core` adds `OpenUIWiringCheck`, an opt-in check for programs whose state is not wired to do what it looks like: a required query arg that starts null or empty, a query that reads another query's result or reaches a `$variable` only through another statement and so never fetches again, and a `$variable` written and never read. The findings are `OpenUIError`s with the new `x-query-null-arg`, `x-query-reads-query`, `x-query-indirect-state` and `x-state-unread` codes. Query refetching is unchanged.
- A typed tool added with `OpenUITools.Add<TArgs, TResult>` that gets an argument of the wrong type now fails with a `tool-error` naming the argument and the type it takes, such as `takes radiusMiles as a number`, in place of the generic `The tool "x" failed.` The message never carries the value or a key the program wrote.
- `@Each(rows, s, ...)` with an unquoted loop name still reports `type-mismatch`, and the message now says to write `"s"` in quotes.
- The `OpenUI.Lang.Core` package carries an analyzer. `OUI0001` warns when a reference that may be null, such as the `null` in `return empty ? null : OuiArray.Create(picks);`, becomes an `OuiValue?`: the implicit conversion turns it into `OuiValue.Null`, so the result has `HasValue` true. The conversions themselves are unchanged.

## 1.0.0

First release of `OpenUI.Lang.Core` and `OpenUI.Lang.AspNetCore`.

- `OpenUI.Lang.Core` parses OpenUI Lang while it streams, checks it against a component library, evaluates state, queries and actions, merges edits, writes a tree back out as text, and generates the system prompt. It passes all 59 program cases and 12 interaction cases of the conformance fixtures from [thesysdev/openui at `7df27bc`](https://github.com/thesysdev/openui/tree/7df27bc8eeb6fd15ac318ca7745d225a8e16e9b9).
- `OpenUI.Lang.AspNetCore` adds `AddOpenUI` and `MapOpenUIChat`, which serve the stock `@openuidev/react-ui` chat from any Microsoft.Extensions.AI `IChatClient`.
- `OpenUI.Lang.Blazor` and `OpenUI.Lang.Blazor.Mud` are in the repository but not packaged yet.
