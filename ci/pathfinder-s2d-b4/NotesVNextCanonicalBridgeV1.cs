using System.Text;
using System.Text.Json;
using NivareQ.Step2Pathfinder.Models;

namespace NivareQ.Step2Pathfinder.Services;

// S2-D B4: typed adapter to THE EXISTING STRICT WINDOWS IMPORT PARSER.
// Not installed in MainWindow, ordinary JSON backup, reset or live writers.
// A positive result is permission to continue OFFLINE preflight, not to commit.
public sealed class NotesVNextCanonicalBridgeV1
{
    private readonly BackupContractService _contract;
    private readonly ShellSettings _currentSettings;
    private readonly CockpitStateEnvelope _currentCockpit;
    private readonly JournalState _currentJournal;
    private readonly UWorldSelectorState _currentSelector;
    private readonly PreservedBackupExtensionState _currentExtensions;
    private readonly string? _expectedSourceGenerationId;

    public NotesVNextCanonicalBridgeV1(BackupContractService contract,
        ShellSettings currentSettings, CockpitStateEnvelope currentCockpit,
        JournalState currentJournal, UWorldSelectorState currentSelector,
        PreservedBackupExtensionState currentExtensions, string? expectedSourceGenerationId = null)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        _currentSettings = currentSettings ?? throw new ArgumentNullException(nameof(currentSettings));
        _currentCockpit = currentCockpit ?? throw new ArgumentNullException(nameof(currentCockpit));
        _currentJournal = currentJournal ?? throw new ArgumentNullException(nameof(currentJournal));
        _currentSelector = currentSelector ?? throw new ArgumentNullException(nameof(currentSelector));
        _currentExtensions = BackupExtensionStore.Clone(currentExtensions ?? throw new ArgumentNullException(nameof(currentExtensions)));
        if (expectedSourceGenerationId is not null && !NotesStoreV1.IsValidGenerationId(expectedSourceGenerationId))
            throw new ArgumentException("Expected Notes generation is not valid.", nameof(expectedSourceGenerationId));
        _expectedSourceGenerationId = expectedSourceGenerationId;
    }

    public BackupImportPlan ParseStrict(JsonElement root)
    {
        NotesVNextProviderAuthorityV1.ValidateArchiveProvider(root, _expectedSourceGenerationId);
        var raw = root.GetRawText();
        if (Encoding.UTF8.GetByteCount(raw) > BackupContractService.MaxBackupBytes)
            throw new InvalidDataException("Canonical provider exceeds the established 100 MB JSON bound.");
        var plan = _contract.ParseImport(raw, _currentSettings, _currentCockpit, _currentJournal,
            _currentSelector, _currentExtensions);
        if (plan.SourceSchemaVersion != BackupContractService.CanonicalSchemaVersion ||
            !plan.ReplaceCockpitState || !plan.ReplaceJournal ||
            !plan.ReplaceUWorldSelector || !plan.ReplacePreservedExtensions)
            throw new InvalidDataException("Backup VNext refused incomplete canonical provider replacement semantics.");
        return plan;
    }

    public void Validate(JsonElement root) => _ = ParseStrict(root);
}
