namespace Apperture.Connectors.CampusNexus.Models;

public readonly record struct ApiCommand(string Module, string EntityName, string Operation, string Payload);
