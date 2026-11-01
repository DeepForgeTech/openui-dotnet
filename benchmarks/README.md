# OpenUI.Lang benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org) benchmarks for the critical paths of `OpenUI.Lang.Core`, to run repeatedly and compare before and after a change. They use only the public API.

## What is measured

Inputs come in three sizes (`Size`). Small is a program from `tests/fixtures/1.0`. Medium and Large are generated in the same shape, because the fixtures stop at a few hundred characters: a ticket dashboard with a query, `@Filter`, `@Sort`, a table and one card per team (about 90 and 400 statements, 10, 100 and 1000 query rows).

| Class | Measures |
| --- | --- |
| `ParseBenchmarks` | `OpenUIParser.Parse` on a whole reply. |
| `StreamingBenchmarks` | One reply fed in 16-character chunks: the parser alone, a session read once at the end, and a session read after every chunk. Cost per chunk that grows with program size is quadratic. |
| `EvaluationBenchmarks` | `OpenUIEvaluator.Evaluate` with query rows supplied, and a session re-evaluated after a state write. |
| `BuiltinBenchmarks` | `@Sum`, `@Filter`, `@Sort` (numbers and text), member pluck and `@Each` over query rows. |
| `ActionBenchmarks` | A button click through `RunActionAsync`, alone and inside a form that checks every field first. |
| `MutationBenchmarks` | A click that runs a mutation (the `mutation-run` fixture). |
| `EcmaBenchmarks` | The ECMAScript conversions (`StringToNumber`, `NumberToString`, loose equality, `+`, `String()`) and locale compare, per input. |
| `ValidationBenchmarks` | `ValidateForm` with `email` and `pattern` rules, which is the public way into the regex engine. |
| `ValueJsonBenchmarks` | Reading and writing query rows as JSON, untyped and typed. |
| `PromptBenchmarks` | `SystemPrompt.Generate` for the fixture library and generated libraries of 60 and 300 components, with default options and with tools, bindings, built-ins, edit mode and examples on. |

Every class has `[MemoryDiagnoser]`. Setup is in `[GlobalSetup]` and checks that the program parses, evaluates and clicks without errors, so a benchmark that stops doing its work fails instead of getting faster.

## Running

Always in Release; BenchmarkDotNet refuses a build with optimizations off. Close other builds first, or the numbers mean little.

```sh
dotnet run -c Release --project benchmarks/OpenUI.Lang.Benchmarks -- --list flat              # every benchmark
dotnet run -c Release --project benchmarks/OpenUI.Lang.Benchmarks -- --filter '*'             # all of them (a long run)
dotnet run -c Release --project benchmarks/OpenUI.Lang.Benchmarks -- --filter '*Streaming*'   # one class
dotnet run -c Release --project benchmarks/OpenUI.Lang.Benchmarks -- --filter '*Builtin*Sort*' # a few methods
dotnet run -c Release --project benchmarks/OpenUI.Lang.Benchmarks -- --job Dry --filter '*'   # does each one run? (the numbers mean nothing)
```

`--filter` is required: without it BenchmarkDotNet asks which benchmark to run. It takes globs on `Namespace.Class.Method`, and a match runs every size of that method.

## Results

Reports go to `benchmarks/artifacts/` (git-ignored; `--artifacts <dir>` changes it): `results/` holds the markdown, CSV and `*-report-full.json` of each class.

## Comparing two runs

Run the same filter on both builds, keeping each run's `results/` folder (copy or rename it, since the next run overwrites the files). Then compare them with `ResultsComparer` from [dotnet/performance](https://github.com/dotnet/performance/tree/main/src/tools/ResultsComparer), which reads the `*-report-full.json` files:

```sh
git clone https://github.com/dotnet/performance && cd performance/src/tools/ResultsComparer
dotnet run -c Release -- --base <before>/results --diff <after>/results --threshold 5%
```

It lists the benchmarks that got slower and faster by more than the threshold. Compare runs from the same machine and the same load.

`benchmarks/baselines/` is for a committed baseline to use as `--base`.
