namespace QuranReconciliation.Infrastructure;

internal static class AppPaths
{
    internal static string BaseDirectory => AppContext.BaseDirectory;
    internal static string DataDirectory => Path.Combine(BaseDirectory, "Data");
    internal static string CorpusDirectory => Path.Combine(BaseDirectory, "Corpus");
    internal static string CorpusDatabase => Path.Combine(CorpusDirectory, "corpus.sqlite");
    internal static string CorpusSearchDatabase => Path.Combine(CorpusDirectory, "corpus-search.sqlite");
    internal static string WordByWordDatabase => Path.Combine(CorpusDirectory, "word-by-word.sqlite");
    internal static string JuzMapFile => Path.Combine(CorpusDirectory, "juz-map.json");
    internal static string BackupsDirectory => Path.Combine(BaseDirectory, "Backups");
    internal static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    internal static string ResearchDatabase => Path.Combine(DataDirectory, "research.sqlite");
    internal static string ContextAtlasDirectory => Path.Combine(BaseDirectory, "ContextAtlas");
    internal static string ContextAtlasImportedDirectory => Path.Combine(ContextAtlasDirectory, "Imported");
    internal static string ContextAtlasExportsDirectory => Path.Combine(ContextAtlasDirectory, "Exports");
    internal static string ContextAtlasBuiltInDirectory => Path.Combine(CorpusDirectory, "ContextAtlas", "BuiltIn");
    internal static string ContextAtlasHandoffTemplatesDirectory => Path.Combine(CorpusDirectory, "ContextAtlas", "HandoffTemplates");

    internal static void EnsurePortableDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}
