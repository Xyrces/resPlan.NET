using System.IO.Compression;
using System.Security.Cryptography;

namespace ResPlan.Library.Data;

public static class DataManager
{
    private const string DataDir = "ResPlan";
    private const string PklFile = "ResPlan.pkl";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(15) };

    internal sealed record DatasetSource(
        Uri ArchiveUri, long ArchiveLength, string ArchiveSha256,
        string EntryName, long EntryLength, string EntrySha256);

    internal static readonly DatasetSource PinnedSource = new(
        new Uri("https://raw.githubusercontent.com/m-agour/ResPlan/e2b78fe069aee1ab1e1828a612743f308e3c32a7/ResPlan.zip"),
        100106537, "f718de8865e51bbe93b49b584798e3c536ed6b4b8a5d32f01b56812f389aeb46",
        PklFile, 258453658, "103edd854a5f365aa875ed832e6cef0d8bc72c4b34e5df0856c01b6970684cb5");

    /// <summary>Finds an existing dataset or installs the checksum-verified, pinned upstream dataset.</summary>
    /// <remarks>Existing local datasets are used as-is and are never replaced.</remarks>
    public static Task EnsureDataAsync(Action<string>? logger = null)
        => EnsureDataAsync(logger, CancellationToken.None);

    public static Task EnsureDataAsync(Action<string>? logger, CancellationToken cancellationToken)
        => EnsureDataAsync(Directory.GetCurrentDirectory(), Client, PinnedSource, logger, cancellationToken);

    internal static async Task EnsureDataAsync(string workingDirectory, HttpClient client, DatasetSource source,
        Action<string>? logger = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var log = logger ?? Console.WriteLine;
        string dataPath = GetDataPath(workingDirectory);
        if (File.Exists(dataPath))
        {
            log($"Data found at {dataPath}");
            return;
        }

        string targetDirectory = Path.GetDirectoryName(dataPath)!;
        Directory.CreateDirectory(targetDirectory);
        string archivePath = Path.Combine(targetDirectory, $".ResPlan-{Guid.NewGuid():N}.zip.tmp");
        string extractedPath = Path.Combine(targetDirectory, $".ResPlan-{Guid.NewGuid():N}.pkl.tmp");
        try
        {
            log("ResPlan data not found. Downloading the pinned dataset...");
            using (var response = await client.GetAsync(source.ArchiveUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length && length != source.ArchiveLength)
                    throw new InvalidDataException("The archive download has an unexpected length.");
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = CreateTemporaryFile(archivePath);
                await CopyAndVerifyAsync(input, output, source.ArchiveLength, source.ArchiveSha256,
                    "Downloaded archive", cancellationToken);
            }

            log("Archive verified. Extracting ResPlan.pkl...");
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var entries = archive.Entries.Where(entry => entry.FullName == source.EntryName).ToArray();
                if (entries.Length != 1)
                    throw new InvalidDataException($"The dataset archive must contain exactly one '{source.EntryName}' entry.");
                var entry = entries[0];
                if (entry.Length != source.EntryLength)
                    throw new InvalidDataException("The dataset entry has an unexpected uncompressed length.");
                await using var input = entry.Open();
                await using var output = CreateTemporaryFile(extractedPath);
                await CopyAndVerifyAsync(input, output, source.EntryLength, source.EntrySha256,
                    "Extracted dataset", cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // The temporary file is on the destination filesystem. Publish only
                // verified complete data, without replacing a dataset created meanwhile.
                File.Move(extractedPath, dataPath, overwrite: false);
                log($"Dataset verified and installed at {dataPath}");
            }
            catch (IOException) when (File.Exists(dataPath))
            {
                log($"An existing dataset was retained at {dataPath}");
            }
        }
        finally
        {
            try { File.Delete(archivePath); }
            finally { File.Delete(extractedPath); }
        }
    }

    private static FileStream CreateTemporaryFile(string path) => new(path, FileMode.CreateNew,
        FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task CopyAndVerifyAsync(Stream input, Stream output, long expectedLength,
        string expectedSha256, string description, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long length = 0;
        while (true)
        {
            int count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (count > expectedLength - length)
                throw new InvalidDataException($"{description} exceeds its expected size.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            hash.AppendData(buffer, 0, count);
            length += count;
        }
        if (length != expectedLength)
            throw new InvalidDataException($"{description} is incomplete or has an unexpected size.");
        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(expectedSha256)))
            throw new InvalidDataException($"{description} SHA-256 does not match the pinned dataset.");
        await output.FlushAsync(cancellationToken);
    }

    public static string GetDataPath() => GetDataPath(Directory.GetCurrentDirectory());

    internal static string GetDataPath(string workingDirectory)
    {
        string destinationRoot = Path.GetFullPath(workingDirectory);
        for (var current = new DirectoryInfo(destinationRoot); current is not null; current = current.Parent)
        {
            string direct = Path.Combine(current.FullName, DataDir, PklFile);
            if (File.Exists(direct)) return direct;
            string nested = Path.Combine(current.FullName, DataDir, DataDir, PklFile);
            if (File.Exists(nested)) return nested;
            if (File.Exists(Path.Combine(current.FullName, "ResPlan.slnx")))
            {
                destinationRoot = current.FullName;
                break;
            }
        }
        return Path.Combine(destinationRoot, DataDir, PklFile);
    }
}
