namespace HubRoute.Models;

/// <summary>Reports whether a downloaded installer passed native trust and publisher checks.</summary>
public sealed record InstallerVerificationResult(bool IsTrusted, string Description);
