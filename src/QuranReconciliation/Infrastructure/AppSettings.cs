namespace QuranReconciliation.Infrastructure;

internal sealed class AppSettings
{
    internal const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Theme { get; set; } = "Daylight";
    public float Zoom { get; set; } = 1.00f;

    // Additive portable preference; no research or settings schema migration.
    // Default ON, editable from Data safety, and saved across launches.
    public bool ConfirmBeforeExit { get; set; } = true;
    public bool ShowUthmani { get; set; } = true;
    public bool ShowIndoPak { get; set; } = true;
    public bool ShowIndoPakNastaleeq { get; set; } = true;
    public int LastSurahNumber { get; set; } = 1;
    public bool SourceSelectionInitialized { get; set; }
    public List<int> SelectedTranslationIds { get; set; } = [];
    public List<int> SelectedTafsirIds { get; set; } = [];

    public bool ShowLeftSidebar { get; set; } = true;
    public bool ShowRightSidebar { get; set; } = true;
    public double LeftSidebarWidth { get; set; } = 300;
    public double RightSidebarWidth { get; set; } = 370;
    public string TopBarMode { get; set; } = "Full";

    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
}
