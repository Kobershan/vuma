using Xunit;

// The integration suite provisions isolated PostgreSQL databases from one shared template.
// Serializing test collections prevents independent fixtures from concurrently dropping,
// migrating, and cloning databases on the same server. Tests that verify concurrency still
// create their concurrent operations explicitly inside the test body.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
