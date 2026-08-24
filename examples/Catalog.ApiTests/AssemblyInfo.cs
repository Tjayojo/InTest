using Microsoft.VisualStudio.TestTools.UnitTesting;

// The single authoritative declaration of parallelization intent.
// Do NOT set MSTestParallelizeScope in the .csproj — it generates this attribute,
// and two of them is a build error.
[assembly: DoNotParallelize]
