// The single authoritative declaration of parallelization intent. xUnit v3
// parallelises by default; this attribute is the counterpart of the MSTest
// scaffold's [assembly: DoNotParallelize] — without it a scaffolded suite runs
// concurrently against a deployed API.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
