var builder = DistributedApplication.CreateBuilder(args);

var valkey = builder.AddValkey("valkey");

builder.AddProject<Projects.Platform22>("platform22")
    .WithReference(valkey)
    .WaitFor(valkey);

builder.Build().Run();
