var builder = DistributedApplication.CreateBuilder(args);

var k8s = builder.AddKubernetesEnvironment("k8s");
var valkey = builder.AddValkey("valkey");
const string orleansClusterId = "platform22";
var orleansHost = builder.AddProject<Projects.Platform22_OrleansHost>("orleans-host")
    .WithComputeEnvironment(k8s)
    .WithEnvironment("ORLEANS_CLUSTER_ID", orleansClusterId)
    .WithEnvironment("ORLEANS_SILO_PORT", "11111")
    .WithEnvironment("ORLEANS_GATEWAY_PORT", "30000")
    .WithEndpoint(targetPort: 11111, name: "silo", scheme: "tcp", isProxied: false)
    .WithEndpoint(targetPort: 30000, name: "gateway", scheme: "tcp", isProxied: false)
    .WithReference(valkey)
    .WaitFor(valkey);

var orleansHost2 = builder.ExecutionContext.IsRunMode
    ? builder.AddProject<Projects.Platform22_OrleansHost>("orleans-host-2")
        .WithEnvironment("ORLEANS_SILO_PORT", "11112")
        .WithEnvironment("ORLEANS_GATEWAY_PORT", "30001")
        .WithEnvironment("ORLEANS_CLUSTER_ID", orleansClusterId)
        .WithEndpoint(targetPort: 11112, name: "silo", scheme: "tcp", isProxied: false)
        .WithEndpoint(targetPort: 30001, name: "gateway", scheme: "tcp", isProxied: false)
        .WithReference(valkey)
        .WaitFor(valkey)
    : null;

var platform22 = builder.AddProject<Projects.Platform22>("platform22")
    .WithComputeEnvironment(k8s)
    .WithReference(valkey)
    .WithEnvironment("PLATFORM22_ORLEANS_MODE", "external")
    .WithEnvironment("PLATFORM22_SSH_MODE", "enabled")
    .WithEnvironment("PLATFORM22_SSH_PORT", "2222")
    .WithEnvironment("ORLEANS_GATEWAY_HOST", orleansHost.GetEndpoint("gateway"))
    .WithEnvironment("ORLEANS_GATEWAY_PORT", "30000")
    .WithEnvironment("ORLEANS_CLUSTER_ID", orleansClusterId)
    .WithEndpoint(targetPort: 2222, name: "ssh", scheme: "tcp", isProxied: false)
    .WaitFor(valkey)
    .WaitFor(orleansHost);
if (orleansHost2 is not null)
{
    platform22.WaitFor(orleansHost2);
}

// Anonymous SSH is a local-development convenience only. Published
// deployments must mount authorized keys and use publickey auth.
if (builder.ExecutionContext.IsRunMode)
{
    platform22.WithEnvironment("PLATFORM22_SSH_AUTH", "none");
}

var platform22Node2 = builder.AddProject<Projects.Platform22>("platform22-node-2")
    .WithComputeEnvironment(k8s)
    .WithReference(valkey)
    .WithEnvironment("PLATFORM22_ORLEANS_MODE", "external")
    .WithEnvironment("PLATFORM22_SSH_MODE", "enabled")
    .WithEnvironment("PLATFORM22_SSH_PORT", "2223")
    .WithEnvironment("ORLEANS_GATEWAY_HOST", orleansHost.GetEndpoint("gateway"))
    .WithEnvironment("ORLEANS_GATEWAY_PORT", "30000")
    .WithEnvironment("ORLEANS_CLUSTER_ID", orleansClusterId)
    .WithEndpoint(targetPort: 2223, name: "ssh", scheme: "tcp", isProxied: false)
    .WaitFor(valkey)
    .WaitFor(orleansHost);
if (orleansHost2 is not null)
{
    platform22Node2.WaitFor(orleansHost2);
}

if (builder.ExecutionContext.IsRunMode)
{
    platform22Node2.WithEnvironment("PLATFORM22_SSH_AUTH", "none");
}

builder.Build().Run();
