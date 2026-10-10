using System.Text.Json;

namespace NivareQ.Step2Pathfinder.Services;

// S2-D B4: pure, fail-closed archive-provider identity guard.
// This is NOT an alternative to BackupContractService.ParseImport.
public static class NotesVNextProviderAuthorityV1
{
    private const int MaxVisitedElements = 300_000;

    public static string ValidateArchiveProvider(JsonElement root, string? requiredSourceGenerationId = null)
    {
        var visited = 0;
        RejectDuplicateObjectKeys(root, ref visited);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("BackupSchemaVersion", out var schema) ||
            schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) ||
            version != BackupSchemaVersion ||
            !root.TryGetProperty("FormatId", out var format) ||
            format.ValueKind != JsonValueKind.String || format.GetString() != BackupFormatId ||
            !root.TryGetProperty("ContractAuthority", out var authority) ||
            authority.ValueKind != JsonValueKind.String || authority.GetString() != "windows-canonical" ||
            !root.TryGetProperty("PreservedExtensions", out var extensions) ||
            extensions.ValueKind != JsonValueKind.Object ||
            !extensions.TryGetProperty(NotesStoreV1.GenerationPointerKey, out var pointer) ||
            pointer.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Backup VNext requires schema-7 Windows canonical providers with an explicit Notes generation pointer.");
        var sourceId = pointer.GetString();
        if (!NotesStoreV1.IsValidGenerationId(sourceId))
            throw new InvalidDataException("Backup VNext source generation pointer is invalid.");
        if (requiredSourceGenerationId is not null &&
            !string.Equals(sourceId, requiredSourceGenerationId, StringComparison.Ordinal))
            throw new InvalidDataException("Backup provider pointer and frozen Notes snapshot disagree.");
        return sourceId!;
    }

    public static void ValidatePointerRebase(string sourceGenerationId, string stagedGenerationId,
        string? currentlyActiveGenerationId)
    {
        if (!NotesStoreV1.IsValidGenerationId(sourceGenerationId) ||
            !NotesStoreV1.IsValidGenerationId(stagedGenerationId) ||
            string.Equals(sourceGenerationId, stagedGenerationId, StringComparison.Ordinal))
            throw new InvalidDataException("A restored Notes generation must use a new, valid identifier.");
        if (currentlyActiveGenerationId is not null &&
            (!NotesStoreV1.IsValidGenerationId(currentlyActiveGenerationId) ||
             string.Equals(currentlyActiveGenerationId, stagedGenerationId, StringComparison.Ordinal)))
            throw new InvalidDataException("Restoration cannot replace the existing authoritative Notes generation.");
    }

    private const int BackupSchemaVersion = 7;
    private const string BackupFormatId = "nivareq.step2pathfinder.backup";

    private static void RejectDuplicateObjectKeys(JsonElement element, ref int visited)
    {
        if (++visited > MaxVisitedElements)
            throw new InvalidDataException("Canonical provider exceeds its structural validation budget.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException("Duplicate JSON object member in canonical provider: " + property.Name);
                RejectDuplicateObjectKeys(property.Value, ref visited);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray())
                RejectDuplicateObjectKeys(value, ref visited);
    }
}
