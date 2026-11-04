using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ResPlan.Library.Data;
using Xunit;

namespace ResPlan.Tests;

public class DataManagerTests
{
    private static readonly byte[] Dataset = Encoding.UTF8.GetBytes("A complete verified test dataset");

    [Fact]
    public async Task InstallsOnlyExpectedEntryAndUsesSolutionRootForPathLookup()
    {
        using var directory = new TemporaryDirectory();
        string workingDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "nested", "working")).FullName;
        File.WriteAllText(Path.Combine(directory.Path, "ResPlan.slnx"), "");
        byte[] archive = CreateArchive(("ResPlan.pkl", Dataset), ("../escape.txt", Dataset), ("unrelated.txt", Dataset));
        using var client = Client(archive);

        await Install(workingDirectory, client, Source(archive));

        string expected = Path.Combine(directory.Path, "ResPlan", "ResPlan.pkl");
        Assert.Equal(expected, DataManager.GetDataPath(workingDirectory));
        Assert.Equal(Dataset, await File.ReadAllBytesAsync(expected));
        Assert.Equal(new[] { expected }, Directory.GetFiles(Path.GetDirectoryName(expected)!));
        Assert.False(File.Exists(Path.Combine(directory.Path, "escape.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreservesExistingLocalAndLegacyNestedUserDatasetsWithoutHttp(bool nested)
    {
        using var directory = new TemporaryDirectory();
        string folder = nested ? Path.Combine(directory.Path, "ResPlan", "ResPlan") : Path.Combine(directory.Path, "ResPlan");
        Directory.CreateDirectory(folder);
        string existing = Path.Combine(folder, "ResPlan.pkl");
        byte[] userData = Encoding.UTF8.GetBytes("User-supplied data with a different hash");
        File.WriteAllBytes(existing, userData);
        var handler = new FakeHandler(() => throw new InvalidOperationException("HTTP must not be called"));
        using var client = new HttpClient(handler);

        await Install(directory.Path, client, DataManager.PinnedSource);

        Assert.Equal(existing, DataManager.GetDataPath(directory.Path));
        Assert.Equal(userData, File.ReadAllBytes(existing));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task ArchiveHashMismatchLeavesNoDatasetOrTemporaryFiles()
    {
        using var directory = new TemporaryDirectory();
        byte[] archive = CreateArchive(("ResPlan.pkl", Dataset));
        using var client = Client(archive);
        var source = Source(archive) with { ArchiveSha256 = new string('0', 64) };

        await Assert.ThrowsAsync<InvalidDataException>(() => Install(directory.Path, client, source));

        AssertNoFiles(directory);
    }

    [Fact]
    public async Task EntryHashMismatchLeavesNoDatasetOrTemporaryFiles()
    {
        using var directory = new TemporaryDirectory();
        byte[] archive = CreateArchive(("ResPlan.pkl", Dataset));
        using var client = Client(archive);
        var source = Source(archive) with { EntrySha256 = new string('0', 64) };

        await Assert.ThrowsAsync<InvalidDataException>(() => Install(directory.Path, client, source));

        AssertNoFiles(directory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsMissingOrDuplicateExpectedEntry(bool duplicate)
    {
        using var directory = new TemporaryDirectory();
        byte[] archive = duplicate
            ? CreateArchive(("ResPlan.pkl", Dataset), ("ResPlan.pkl", Dataset))
            : CreateArchive(("nested/ResPlan.pkl", Dataset));
        using var client = Client(archive);

        await Assert.ThrowsAsync<InvalidDataException>(() => Install(directory.Path, client, Source(archive)));

        AssertNoFiles(directory);
    }

    [Fact]
    public async Task RejectsOversizedExtractedEntryBeforePublishing()
    {
        using var directory = new TemporaryDirectory();
        byte[] archive = CreateArchive(("ResPlan.pkl", Dataset));
        using var client = Client(archive);
        var source = Source(archive) with { EntryLength = Dataset.Length - 1 };

        await Assert.ThrowsAsync<InvalidDataException>(() => Install(directory.Path, client, source));

        AssertNoFiles(directory);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task RejectsTruncatedOrOversizedDownloadsWithoutContentLength(int extra)
    {
        using var directory = new TemporaryDirectory();
        byte[] archive = CreateArchive(("ResPlan.pkl", Dataset));
        byte[] delivered = extra < 0 ? archive[..^1] : archive.Concat(new byte[] { 0 }).ToArray();
        using var client = Client(new TestReadStream(delivered));

        await Assert.ThrowsAsync<InvalidDataException>(() => Install(directory.Path, client, Source(archive)));

        AssertNoFiles(directory);
    }

    [Fact]
    public async Task RejectsUnexpectedDeclaredDownloadLength()
    {
        using var directory = new TemporaryDirectory();
        byte[] archive = CreateArchive(("ResPlan.pkl", Dataset));
        using var client = Client(archive);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Install(directory.Path, client, Source(archive) with { ArchiveLength = archive.Length + 1 }));

        AssertNoFiles(directory);
    }

    [Fact]
    public async Task HttpFailureLeavesNoPartialFiles()
    {
        using var directory = new TemporaryDirectory();
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => Install(directory.Path, client, DataManager.PinnedSource));

        AssertNoFiles(directory);
    }

    [Fact]
    public async Task InterruptedDownloadCleansPartialFile()
    {
        using var directory = new TemporaryDirectory();
        byte[] archive = CreateArchive(("ResPlan.pkl", Dataset));
        var stream = new TestReadStream(archive) { FailAfterFirstRead = true };
        using var client = Client(stream);

        await Assert.ThrowsAsync<IOException>(() => Install(directory.Path, client, Source(archive)));

        Assert.True(stream.Reads >= 2);
        AssertNoFiles(directory);
    }

    [Fact]
    public async Task CancellationDuringDownloadCleansPartialFile()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        byte[] archive = CreateArchive(("ResPlan.pkl", Dataset));
        var stream = new TestReadStream(archive) { BeforeSecondRead = cancellation.Cancel };
        using var client = Client(stream);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Install(directory.Path, client, Source(archive), cancellation.Token));

        Assert.True(stream.Reads >= 2);
        AssertNoFiles(directory);
    }

    [Fact]
    public async Task DatasetCreatedDuringDownloadIsNotOverwritten()
    {
        using var directory = new TemporaryDirectory();
        byte[] archive = CreateArchive(("ResPlan.pkl", Dataset));
        string target = Path.Combine(directory.Path, "ResPlan", "ResPlan.pkl");
        byte[] userData = Encoding.UTF8.GetBytes("A newer local dataset");
        var handler = new FakeHandler(() =>
        {
            File.WriteAllBytes(target, userData);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
        });
        using var client = new HttpClient(handler);

        await Install(directory.Path, client, Source(archive));

        Assert.Equal(userData, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, Directory.GetFiles(Path.GetDirectoryName(target)!));
    }

    private static Task Install(string directory, HttpClient client, DataManager.DatasetSource source,
        CancellationToken cancellationToken = default)
        => DataManager.EnsureDataAsync(directory, client, source, _ => { }, cancellationToken);

    private static DataManager.DatasetSource Source(byte[] archive) => new(
        new Uri("https://dataset.example.test/pinned.zip"), archive.Length, Hash(archive),
        "ResPlan.pkl", Dataset.Length, Hash(Dataset));

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    private static byte[] CreateArchive(params (string Name, byte[] Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(content);
            }
        return output.ToArray();
    }

    private static HttpClient Client(byte[] bytes) => new(new FakeHandler(() =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));

    private static HttpClient Client(Stream stream) => new(new FakeHandler(() =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));

    private static void AssertNoFiles(TemporaryDirectory directory)
        => Assert.Empty(Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories));

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("resplan-download-tests-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FakeHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            return Task.FromResult(response());
        }
    }

    private sealed class TestReadStream(byte[] data) : Stream
    {
        private readonly MemoryStream inner = new(data, writable: false);
        public bool FailAfterFirstRead { get; init; }
        public Action? BeforeSecondRead { get; init; }
        public int Reads { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (Reads == 2) BeforeSecondRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (FailAfterFirstRead && Reads > 1) throw new IOException("Simulated interrupted download");
            return inner.ReadAsync(buffer[..Math.Min(buffer.Length, 16)], cancellationToken);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
