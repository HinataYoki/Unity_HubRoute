namespace HubRoute.Models;

/// <summary>Reports the process created for a successful Unity Hub launch.</summary>
public sealed record LaunchResult(int ProcessId, string RouteDescription, int StoppedProcessCount);
