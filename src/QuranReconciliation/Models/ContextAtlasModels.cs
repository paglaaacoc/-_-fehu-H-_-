using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuranReconciliation.Models;

internal sealed class ProposalCorpusDocument
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("corpus_id")]
    public string CorpusId { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("formal_title")]
    public string FormalTitle { get; set; } = string.Empty;

    [JsonPropertyName("edition")]
    public int Edition { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("published_date")]
    public string PublishedDate { get; set; } = string.Empty;

    [JsonPropertyName("contributors")]
    public List<ProposalContributor> Contributors { get; set; } = [];

    [JsonPropertyName("lineage")]
    public ProposalLineage Lineage { get; set; } = new();

    [JsonPropertyName("evidence_policy")]
    public JsonElement EvidencePolicy { get; set; }

    [JsonPropertyName("terminology")]
    public JsonElement Terminology { get; set; }

    [JsonPropertyName("source_identity")]
    public JsonElement SourceIdentity { get; set; }

    [JsonPropertyName("statistics")]
    public ProposalStatistics Statistics { get; set; } = new();

    [JsonPropertyName("surahs")]
    public List<ProposalSurah> Surahs { get; set; } = [];

    [JsonPropertyName("cross_surah_worksets")]
    public List<ProposalWorkset> CrossSurahWorksets { get; set; } = [];

    [JsonPropertyName("final_review")]
    public JsonElement FinalReview { get; set; }
}

internal sealed class ProposalContributor
{
    [JsonPropertyName("model_name")]
    public string ModelName { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("reasoning_mode")]
    public string ReasoningMode { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("methodology")]
    public string Methodology { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("completed_utc")]
    public string? CompletedUtc { get; set; }
}

internal sealed class ProposalLineage
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("parent_corpus_id")]
    public string? ParentCorpusId { get; set; }

    [JsonPropertyName("parent_payload_sha256")]
    public string? ParentPayloadSha256 { get; set; }
}

internal sealed class ProposalStatistics
{
    [JsonPropertyName("surahs_audited")]
    public int SurahsAudited { get; set; }

    [JsonPropertyName("ayat_covered")]
    public int AyatCovered { get; set; }

    [JsonPropertyName("total_context_blocks")]
    public int TotalContextBlocks { get; set; }

    [JsonPropertyName("total_internal_boundaries")]
    public int TotalInternalBoundaries { get; set; }

    [JsonPropertyName("juz_crossing_blocks")]
    public int JuzCrossingBlocks { get; set; }

    [JsonPropertyName("single_ayah_blocks")]
    public int SingleAyahBlocks { get; set; }

    [JsonPropertyName("juz_coincident_internal_boundaries")]
    public int JuzCoincidentInternalBoundaries { get; set; }

    [JsonPropertyName("macro_groups")]
    public int MacroGroups { get; set; }

    [JsonPropertyName("cross_surah_worksets")]
    public int CrossSurahWorksets { get; set; }
}

internal sealed class ProposalSurah
{
    [JsonPropertyName("surah_number")]
    public int SurahNumber { get; set; }

    [JsonPropertyName("surah_name")]
    public string SurahName { get; set; } = string.Empty;

    [JsonPropertyName("verses_count")]
    public int VersesCount { get; set; }

    [JsonPropertyName("proposal_status")]
    public string ProposalStatus { get; set; } = string.Empty;

    [JsonPropertyName("context_blocks")]
    public List<ProposalContextBlock> ContextBlocks { get; set; } = [];

    [JsonPropertyName("boundaries")]
    public List<ProposalBoundary> Boundaries { get; set; } = [];

    [JsonPropertyName("surah_flags")]
    public List<string> SurahFlags { get; set; } = [];

    [JsonPropertyName("macro_groups")]
    public List<ProposalMacroGroup> MacroGroups { get; set; } = [];

    [JsonPropertyName("importer_ranges")]
    public List<string> ImporterRanges { get; set; } = [];

    public string DisplayLabel =>
        $"{SurahNumber}. {SurahName} · {ContextBlocks.Count} blocks";
}

internal sealed class ProposalContextBlock
{
    [JsonPropertyName("context_block_id")]
    public string ContextBlockId { get; set; } = string.Empty;

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("start")]
    public int StartAyah { get; set; }

    [JsonPropertyName("end")]
    public int EndAyah { get; set; }

    [JsonPropertyName("range")]
    public string Range { get; set; } = string.Empty;

    [JsonPropertyName("juz_start")]
    public int JuzStart { get; set; }

    [JsonPropertyName("juz_end")]
    public int JuzEnd { get; set; }

    [JsonPropertyName("coherence_note")]
    public string CoherenceNote { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = string.Empty;

    public string DisplayRange =>
        StartAyah == EndAyah
            ? StartAyah.ToString()
            : $"{StartAyah}–{EndAyah}";
}

internal sealed class ProposalBoundary
{
    [JsonPropertyName("boundary_id")]
    public string BoundaryId { get; set; } = string.Empty;

    [JsonPropertyName("after_ayah")]
    public int AfterAyah { get; set; }

    [JsonPropertyName("next_ayah")]
    public int NextAyah { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = string.Empty;

    [JsonPropertyName("english_used")]
    public bool EnglishUsed { get; set; }
}

internal sealed class ProposalMacroGroup
{
    [JsonPropertyName("macro_group_id")]
    public string MacroGroupId { get; set; } = string.Empty;

    [JsonPropertyName("start_ayah")]
    public int StartAyah { get; set; }

    [JsonPropertyName("end_ayah")]
    public int EndAyah { get; set; }

    [JsonPropertyName("verse_span")]
    public string VerseSpan { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("source_label_original")]
    public string SourceLabelOriginal { get; set; } = string.Empty;

    [JsonPropertyName("terminology_normalized")]
    public bool TerminologyNormalized { get; set; }

    [JsonPropertyName("context_block_count")]
    public int ContextBlockCount { get; set; }

    [JsonPropertyName("context_block_ids")]
    public List<string> ContextBlockIds { get; set; } = [];

    [JsonPropertyName("context_ranges")]
    public List<string> ContextRanges { get; set; } = [];
}

internal sealed class ProposalWorkset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [JsonPropertyName("members")]
    public List<ProposalWorksetMember> Members { get; set; } = [];

    [JsonPropertyName("subsets")]
    public List<ProposalWorksetSubset> Subsets { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<ProposalWorksetMember> AllMembers =>
        Members.Concat(
            Subsets.SelectMany(
                x =>
                    x.Members));

    [JsonPropertyName("source_label_original")]
    public string SourceLabelOriginal { get; set; } = string.Empty;

    [JsonPropertyName("source_note_original")]
    public string SourceNoteOriginal { get; set; } = string.Empty;
}

internal sealed class ProposalWorksetSubset
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("members")]
    public List<ProposalWorksetMember> Members { get; set; } = [];
}

internal sealed class ProposalWorksetMember
{
    [JsonPropertyName("surah_number")]
    public int SurahNumber { get; set; }

    [JsonPropertyName("surah_name")]
    public string SurahName { get; set; } = string.Empty;

    [JsonPropertyName("start_ayah")]
    public int StartAyah { get; set; }

    [JsonPropertyName("end_ayah")]
    public int EndAyah { get; set; }

    [JsonPropertyName("verse_span")]
    public string VerseSpan { get; set; } = string.Empty;

    [JsonPropertyName("context_blocks")]
    public List<string> ContextBlocks { get; set; } = [];

    [JsonPropertyName("context_block_ids")]
    public List<string> ContextBlockIds { get; set; } = [];
}

internal sealed class ProposalPackageManifest
{
    [JsonPropertyName("package_schema")]
    public string PackageSchema { get; set; } = string.Empty;

    [JsonPropertyName("package_schema_version")]
    public int PackageSchemaVersion { get; set; }

    [JsonPropertyName("package_type")]
    public string PackageType { get; set; } = string.Empty;

    [JsonPropertyName("corpus_id")]
    public string CorpusId { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("edition")]
    public int Edition { get; set; }

    [JsonPropertyName("published_date")]
    public string PublishedDate { get; set; } = string.Empty;

    [JsonPropertyName("payload_file")]
    public string PayloadFile { get; set; } = string.Empty;

    [JsonPropertyName("payload_sha256")]
    public string PayloadSha256 { get; set; } = string.Empty;

    [JsonPropertyName("terminology_standard")]
    public string TerminologyStandard { get; set; } = string.Empty;
}

internal sealed record ProposalCorpusPackage(
    string PackagePath,
    string PackageSha256,
    string PayloadSha256,
    ProposalPackageManifest Manifest,
    ProposalCorpusDocument Corpus,
    byte[] ManifestBytes,
    byte[] PayloadBytes,
    bool IsBuiltIn)
{
    internal string DisplayLabel =>
        IsBuiltIn
            ? $"{Corpus.DisplayName} · Built-in"
            : $"{Corpus.DisplayName} · Imported";
}

internal sealed record ContextAtlasLiveContext(
    long Id,
    int StartAyah,
    int EndAyah,
    string Status,
    DateTimeOffset UpdatedUtc);

internal sealed record ContextAtlasWorkingSliceSummary(
    long Id,
    string Title,
    string Status,
    int StartAyah,
    int EndAyah,
    int RevisionCount,
    DateTimeOffset UpdatedUtc);

internal sealed record ContextAtlasResearchSnapshot(
    int SurahNumber,
    int StartAyah,
    int EndAyah,
    IReadOnlyList<ContextAtlasLiveContext> Contexts,
    IReadOnlyList<ContextAtlasWorkingSliceSummary> WorkingSlices,
    int AyahNoteCount,
    int AyahNoteRevisionCount,
    int ContextNoteCount,
    int ContextNoteRevisionCount,
    DateTimeOffset? LastUpdatedUtc)
{
    internal bool HasExactContextMatch =>
        Contexts.Any(
            x =>
                x.StartAyah == StartAyah &&
                x.EndAyah == EndAyah);
}

internal sealed record ProposalCorpusComparison(
    int SharedBoundaries,
    int LeftOnlyBoundaries,
    int RightOnlyBoundaries,
    int ExactBlocks,
    int LeftBlocks,
    int RightBlocks);


internal sealed record ContextAtlasRangeTarget(
    int SurahNumber,
    int StartAyah,
    int EndAyah,
    string Label);
