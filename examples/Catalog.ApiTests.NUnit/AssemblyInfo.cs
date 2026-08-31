// The single authoritative declaration of parallelization intent. Unlike the
// xUnit scaffold's attribute, this one does not prevent a live hazard — NUnit's
// default is already sequential ([nunit-is-sequential], measured). It states
// intent rather than fixing one: the explicit, provable analogue of the MSTest
// scaffold's [assembly: DoNotParallelize], and it is what keeps a scaffolded
// suite sequential against a deployed API even after someone adds
// [Parallelizable] to an individual class later.
[assembly: NUnit.Framework.LevelOfParallelism(1)]
