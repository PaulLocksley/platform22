var builder = DistributedApplication.CreateBuilder(args);

var valkey = builder.AddValkey("valkey");
var orleansHost = builder.AddProject<Projects.Platform22_OrleansHost>("orleans-host")
    .WithEnvironment("ORLEANS_SILO_PORT", "11111")
    .WithEnvironment("ORLEANS_GATEWAY_PORT", "30000")
    .WithReference(valkey)
    .WaitFor(valkey);

builder.AddProject<Projects.Platform22>("platform22")
    .WithReference(valkey)
    .WithEnvironment("PLATFORM22_ORLEANS_MODE", "external")
    .WithEnvironment("PLATFORM22_SSH_MODE", "enabled")
    .WithEnvironment("PLATFORM22_SSH_PORT", "2222")
    .WithEnvironment("ORLEANS_GATEWAY_PORT", "30000")
    .WithEndpoint(targetPort: 2222, name: "ssh", scheme: "tcp", isProxied: false)
    .WaitFor(valkey)
    .WaitFor(orleansHost);

builder.Build().Run();
