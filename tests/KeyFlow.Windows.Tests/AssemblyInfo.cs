using Xunit;
// Raw Input registration is process-wide for keyboard usage; native window tests must not overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
