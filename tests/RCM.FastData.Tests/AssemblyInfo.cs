using Xunit;

// The tests mutate CultureInfo.CurrentCulture at process level:
// no parallelization to avoid races.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
