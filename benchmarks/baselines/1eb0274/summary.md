# Baseline at 1eb0274

Default job, BenchmarkDotNet 0.15.8, .NET 10.0.12, AMD Ryzen 9 3900X, Linux, Release. The machine was in use during the run, so read a difference under about 10% as noise. `cv` is standard deviation over mean, `n` is measured iterations.

Only `StreamingBenchmarks` and `ValidationBenchmarks`. `ParseBenchmarks` is in `../ba77789`, the rest in `../7e68e54`.

```text
StreamingBenchmarks    ParserStream                       Small                       48.6 us cv  4.3% n= 58    83.3 KB
StreamingBenchmarks    ParserStreamReadEveryChunk         Small                       78.0 us cv  1.8% n= 15   133.9 KB
StreamingBenchmarks    SessionStream                      Small                      140.1 us cv  3.3% n= 36   272.7 KB
StreamingBenchmarks    SessionStreamRenderedEveryChunk    Small                      244.7 us cv  2.7% n= 26   447.7 KB
StreamingBenchmarks    ParserStream                       Medium                      1.14 ms cv  3.2% n= 34     1.8 MB
StreamingBenchmarks    ParserStreamReadEveryChunk         Medium                      4.99 ms cv  3.3% n= 35     6.0 MB
StreamingBenchmarks    SessionStream                      Medium                      1.45 ms cv  4.2% n= 55     2.3 MB
StreamingBenchmarks    SessionStreamRenderedEveryChunk    Medium                     21.17 ms cv  3.6% n= 46    34.4 MB
StreamingBenchmarks    ParserStream                       Large                       6.65 ms cv  2.9% n= 30    11.1 MB
StreamingBenchmarks    ParserStreamReadEveryChunk         Large                      80.61 ms cv  3.5% n= 39    91.9 MB
StreamingBenchmarks    SessionStream                      Large                       7.40 ms cv  3.5% n= 40    12.5 MB
StreamingBenchmarks    SessionStreamRenderedEveryChunk    Large                     430.24 ms cv  2.9% n= 35   685.1 MB
ValidationBenchmarks   ValidateEmail                      Small                        470 ns cv  0.7% n= 13      720 B
ValidationBenchmarks   ValidatePattern                    Small                       43.0 us cv  3.2% n= 34   134.0 KB
ValidationBenchmarks   ValidatePatternDistinct            Small                       74.4 us cv  3.6% n= 42   198.8 KB
ValidationBenchmarks   ValidateEmail                      Medium                       6.4 us cv  2.7% n= 26     4.9 KB
ValidationBenchmarks   ValidatePattern                    Medium                      50.1 us cv  3.4% n= 38   138.8 KB
ValidationBenchmarks   ValidatePatternDistinct            Medium                      3.00 ms cv  2.9% n= 36     5.4 MB
ValidationBenchmarks   ValidateEmail                      Large                       65.3 us cv  1.3% n= 13    45.7 KB
ValidationBenchmarks   ValidatePattern                    Large                      110.8 us cv  2.6% n= 25   185.3 KB
ValidationBenchmarks   ValidatePatternDistinct            Large                      29.40 ms cv  2.7% n= 27    57.6 MB
```

## What each case reads

A document works out its errors and references when they are first read, so this decides what a number includes.

- `ParserStream`: errors and references of the final document only. It is the number for a host that does not look until the end.
- `ParserStreamReadEveryChunk`: errors and references of every document.
- `SessionStream`: one render, at the end.
- `SessionStreamRenderedEveryChunk`: a render per chunk, which is what a page that draws each change pays. This is the number to quote for a live page.
- `ValidatePattern`: every field shares one pattern, the best case for compiling a pattern once per pass.
- `ValidatePatternDistinct`: every field has its own pattern, the worst case.

## Against 7e68e54, Large

| Case | 7e68e54 | 1eb0274 |
|---|---|---|
| `ParserStream` | 163.56 ms, 141.7 MB | 6.65 ms, 11.1 MB |
| `ParserStreamReadEveryChunk` | 163.09 ms, 141.7 MB | 80.61 ms, 91.9 MB |
| `SessionStream` | 172.87 ms, 143.2 MB | 7.40 ms, 12.5 MB |
| `SessionStreamRenderedEveryChunk` | 623.89 ms, 798.7 MB | 430.24 ms, 685.1 MB |
| `ValidatePattern` | 8.95 ms, 26.1 MB | 110.8 us, 185.3 KB |
| `ValidatePatternDistinct` | about 26.3 ms, 57.6 MB | 29.40 ms, 57.6 MB |

The two cases that did not exist at 7e68e54 were measured by adding them to a copy of that commit, with `--job short`, so those two old figures are rougher than the rest.

`ValidatePatternDistinct` got slower. In paired short runs, Medium went from about 2.4 to 3.0 ms and Large from about 26.3 to 28.8 ms. Allocation is unchanged; the cause was not isolated. The cost was accepted against the gain in `ValidatePattern`. How many distinct patterns real forms use was not measured.
