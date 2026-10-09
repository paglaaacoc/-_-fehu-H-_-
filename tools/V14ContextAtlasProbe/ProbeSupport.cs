using Microsoft.Data.Sqlite;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ProbeSupport
{
    internal static byte[] DecodeBuiltIn(
        string directory)
    {
        string encoded =
            string.Concat(
                Directory
                    .EnumerateFiles(
                        directory,
                        "*.b64part",
                        SearchOption.TopDirectoryOnly)
                    .OrderBy(
                        x =>
                            Path.GetFileName(x),
                        StringComparer.Ordinal)
                    .Select(
                        File.ReadAllText));

        return Convert.FromBase64String(
            encoded);
    }

    internal static string Sha256(
        byte[] bytes) =>
        Convert.ToHexString(
            SHA256.HashData(
                bytes))
        .ToLowerInvariant();

    internal static string Sha256File(
        string path) =>
        Sha256(
            File.ReadAllBytes(
                path));

    internal static byte[] BuildVariant(
        ProposalCorpusPackage parent,
        Action<JsonObject>? mutatePayload = null,
        Action<JsonObject>? mutateManifest = null,
        bool syncManifestIdentity = true,
        bool syncPayloadHash = true)
    {
        JsonObject payload =
            JsonNode.Parse(
                Encoding.UTF8.GetString(
                    parent.PayloadBytes))!
            .AsObject();

        JsonObject manifest =
            JsonNode.Parse(
                Encoding.UTF8.GetString(
                    parent.ManifestBytes))!
            .AsObject();

        mutatePayload?.Invoke(
            payload);

        byte[] payloadBytes =
            Encoding.UTF8.GetBytes(
                payload.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));

        if (syncManifestIdentity)
        {
            manifest["corpus_id"] =
                payload["corpus_id"]?.GetValue<string>();

            manifest["display_name"] =
                payload["display_name"]?.GetValue<string>();

            manifest["edition"] =
                payload["edition"]?.GetValue<int>();

            manifest["published_date"] =
                payload["published_date"]?.GetValue<string>();
        }

        if (syncPayloadHash)
        {
            manifest["payload_sha256"] =
                Sha256(
                    payloadBytes);
        }

        mutateManifest?.Invoke(
            manifest);

        byte[] manifestBytes =
            Encoding.UTF8.GetBytes(
                manifest.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));

        return CreatePackage(
            manifestBytes,
            payloadBytes);
    }

    internal static byte[] CreatePackage(
        byte[] manifestBytes,
        byte[] payloadBytes)
    {
        using var stream =
            new MemoryStream();

        using (var archive =
            new ZipArchive(
                stream,
                ZipArchiveMode.Create,
                leaveOpen: true))
        {
            WriteEntry(
                archive,
                "manifest.json",
                manifestBytes);

            WriteEntry(
                archive,
                "proposal-corpus.json",
                payloadBytes);
        }

        return stream.ToArray();
    }

    internal static byte[] ReadEntry(
        string zipPath,
        string entryName)
    {
        using var archive =
            ZipFile.OpenRead(
                zipPath);

        ZipArchiveEntry entry =
            archive.GetEntry(
                entryName)
            ?? throw new InvalidDataException(
                $"ZIP missing {entryName}.");

        using Stream source =
            entry.Open();

        using var memory =
            new MemoryStream();

        source.CopyTo(
            memory);

        return memory.ToArray();
    }

    internal static IReadOnlyList<string> ListEntries(
        string zipPath)
    {
        using var archive =
            ZipFile.OpenRead(
                zipPath);

        return archive.Entries
            .Select(
                x =>
                    x.FullName.Replace(
                        '\\',
                        '/'))
            .OrderBy(
                x =>
                    x,
                StringComparer.Ordinal)
            .ToList();
    }

    internal static void WriteBytes(
        string path,
        byte[] bytes)
    {
        string? directory =
            Path.GetDirectoryName(
                path);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        File.WriteAllBytes(
            path,
            bytes);
    }

    internal static void CopyDirectory(
        string source,
        string target)
    {
        Directory.CreateDirectory(
            target);

        foreach (string directory
                 in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(
                Path.Combine(
                    target,
                    Path.GetRelativePath(
                        source,
                        directory)));
        }

        foreach (string file
                 in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            string destination =
                Path.Combine(
                    target,
                    Path.GetRelativePath(
                        source,
                        file));

            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    destination)!);

            File.Copy(
                file,
                destination,
                overwrite: true);
        }
    }

    internal static void CreateSnapshotDatabase(
        string path)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(
                path)!);

        using var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource =
                        path,
                    Mode =
                        SqliteOpenMode.ReadWriteCreate,
                    Pooling =
                        false
                }.ToString());

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
        """
        PRAGMA journal_mode=DELETE;

        CREATE TABLE context_blocks(
            id INTEGER PRIMARY KEY,
            surah_number INTEGER NOT NULL,
            start_ayah INTEGER NOT NULL,
            end_ayah INTEGER NOT NULL,
            status TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            is_active INTEGER NOT NULL
        );

        CREATE TABLE working_slices(
            id INTEGER PRIMARY KEY,
            surah_number INTEGER NOT NULL,
            start_ayah INTEGER NOT NULL,
            end_ayah INTEGER NOT NULL,
            title TEXT NOT NULL,
            status TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            is_active INTEGER NOT NULL
        );

        CREATE TABLE working_slice_revisions(
            id INTEGER PRIMARY KEY,
            working_slice_id INTEGER NOT NULL
        );

        CREATE TABLE ayah_notes(
            surah_number INTEGER NOT NULL,
            ayah_number INTEGER NOT NULL,
            updated_utc TEXT NOT NULL
        );

        CREATE TABLE ayah_note_history(
            id INTEGER PRIMARY KEY,
            surah_number INTEGER NOT NULL,
            ayah_number INTEGER NOT NULL
        );

        CREATE TABLE context_notes(
            context_block_id INTEGER NOT NULL
        );

        CREATE TABLE context_note_history(
            id INTEGER PRIMARY KEY,
            context_block_id INTEGER NOT NULL
        );

        INSERT INTO context_blocks
            (id,surah_number,start_ayah,end_ayah,status,updated_utc,is_active)
        VALUES
            (10,2,1,5,'Accepted','2026-10-07T12:00:00+00:00',1);

        INSERT INTO working_slices
            (id,surah_number,start_ayah,end_ayah,title,status,updated_utc,is_active)
        VALUES
            (20,2,1,3,'Probe slice','In Review','2026-10-07T12:10:00+00:00',1);

        INSERT INTO working_slice_revisions(id,working_slice_id)
        VALUES (1,20),(2,20);

        INSERT INTO ayah_notes(surah_number,ayah_number,updated_utc)
        VALUES (2,2,'2026-10-07T12:20:00+00:00');

        INSERT INTO ayah_note_history(id,surah_number,ayah_number)
        VALUES (1,2,2);

        INSERT INTO context_notes(context_block_id)
        VALUES (10);

        INSERT INTO context_note_history(id,context_block_id)
        VALUES (1,10);
        """;

        command.ExecuteNonQuery();
    }

    internal static void Require(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }

    internal static void RequireThrows(
        Action action,
        string message)
    {
        try
        {
            action();
        }
        catch
        {
            return;
        }

        throw new InvalidOperationException(
            message);
    }

    internal static JsonObject PayloadNode(
        ProposalCorpusPackage package) =>
        JsonNode.Parse(
            Encoding.UTF8.GetString(
                package.PayloadBytes))!
        .AsObject();

    internal static JsonArray Surahs(
        JsonObject payload) =>
        payload["surahs"]!
            .AsArray();

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        byte[] bytes)
    {
        ZipArchiveEntry entry =
            archive.CreateEntry(
                name,
                CompressionLevel.Optimal);

        using Stream target =
            entry.Open();

        target.Write(
            bytes,
            0,
            bytes.Length);
    }
}
