# Baseline at ba77789

`ParserStream` below reads nothing from any document and `ValidatePattern` is the best case for its cache. See `../1eb0274/summary.md` for what each case includes.

Default job, BenchmarkDotNet 0.15.8, .NET 10.0.12, AMD Ryzen 9 3900X, Linux, Release. The machine was in use during the run, so read a difference under about 10% as noise. `cv` is standard deviation over mean, `n` is measured iterations.

Only the classes the changes since 7e68e54 touch: `ParseBenchmarks` (which now reads the result it parses), `StreamingBenchmarks` and `ValidationBenchmarks`. The other classes keep their numbers in `../7e68e54`; a short run of the Evaluation, Action, Mutation and Builtin classes after the changes showed nothing outside the noise, with identical allocations.

```text
ParseBenchmarks        Parse                              Small                       27.4 us cv  2.9% n= 29    42.5 KB
ParseBenchmarks        Parse                              Medium                     428.7 us cv  1.5% n= 13   589.8 KB
ParseBenchmarks        Parse                              Large                       2.17 ms cv  3.3% n= 37     2.6 MB
StreamingBenchmarks    ParserStream                       Small                       44.3 us cv  2.6% n= 25    79.9 KB
StreamingBenchmarks    SessionStream                      Small                      145.1 us cv  6.4% n= 92   270.5 KB
StreamingBenchmarks    SessionStreamRenderedEveryChunk    Small                      246.1 us cv  2.8% n= 28   446.1 KB
StreamingBenchmarks    ParserStream                       Medium                      1.12 ms cv  2.2% n= 19     1.8 MB
StreamingBenchmarks    SessionStream                      Medium                      1.51 ms cv  8.8% n= 86     2.2 MB
StreamingBenchmarks    SessionStreamRenderedEveryChunk    Medium                     21.18 ms cv  2.4% n= 21    34.3 MB
StreamingBenchmarks    ParserStream                       Large                       6.71 ms cv  2.8% n= 28    11.0 MB
StreamingBenchmarks    SessionStream                      Large                       7.46 ms cv  1.4% n= 12    12.5 MB
StreamingBenchmarks    SessionStreamRenderedEveryChunk    Large                     428.81 ms cv  3.6% n= 42   685.1 MB
ValidationBenchmarks   ValidateEmail                      Small                        481 ns cv  2.8% n= 29      720 B
ValidationBenchmarks   ValidatePattern                    Small                       42.1 us cv  0.6% n= 13   134.0 KB
ValidationBenchmarks   ValidateEmail                      Medium                       6.4 us cv  2.4% n= 28     4.9 KB
ValidationBenchmarks   ValidatePattern                    Medium                      50.2 us cv  4.2% n= 54   138.8 KB
ValidationBenchmarks   ValidateEmail                      Large                       66.8 us cv  3.4% n= 37    45.7 KB
ValidationBenchmarks   ValidatePattern                    Large                      110.2 us cv  2.8% n= 27   185.3 KB
```
