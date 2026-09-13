using Microsoft.Extensions.Options;

 namespace slskd.Tests.Unit.Transfers.API;

using System;
using System.IO;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using slskd.Transfers;
using slskd.Transfers.API;
using slskd.Transfers.Downloads;
using Xunit;

public class TransfersControllerStreamTests : IDisposable
{
    public TransfersControllerStreamTests()
    {
        Temp = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}");
        Downloads = Path.Combine(Temp, "downloads");
        Incomplete = Path.Combine(Temp, "incomplete");
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(Incomplete);

        Options = new Options
        {
            Directories = new Options.DirectoriesOptions
            {
                Downloads = Downloads,
                Incomplete = Incomplete,
            },
        };

        DownloadsMock = new Mock<IDownloadService>();
        BatchesMock = new Mock<IBatchService>();
        DownloadsMock.SetupGet(d => d.Batches).Returns(BatchesMock.Object);

        var snapshotMock = new Mock<IOptionsSnapshot<Options>>();
        snapshotMock.Setup(s => s.Value).Returns(Options);

        var transfers = new TransferService(
            contextFactory: null,
            uploadService: null,
            downloadService: DownloadsMock.Object);

        Controller = new TransfersController(
            transfers,
            Mock.Of<slskd.Users.IUserService>(),
            snapshotMock.Object);
    }

    public void Dispose()
    {
        Directory.Delete(Temp, recursive: true);
    }

    private TransfersController Controller { get; init; }
    private Mock<IDownloadService> DownloadsMock { get; init; }
    private Mock<IBatchService> BatchesMock { get; init; }
    private Options Options { get; init; }
    private string Temp { get; init; }
    private string Downloads { get; init; }
    private string Incomplete { get; init; }

    [Fact]
    public async Task StreamDownloadAsync_Returns_400_Given_Invalid_Id()
    {
        var context = CreateContext(range: null);

        await Controller.WithContext(context).StreamDownloadAsync("user", "not-a-guid", CancellationToken.None);

        Assert.Equal(400, context.Response.StatusCode);
    }

    [Fact]
    public async Task StreamDownloadAsync_Returns_404_Given_Missing_Transfer()
    {
        DownloadsMock
            .Setup(d => d.Find(It.IsAny<Expression<Func<Transfer, bool>>>()))
            .Returns((Transfer)null);

        var context = CreateContext(range: null);

        await Controller.WithContext(context).StreamDownloadAsync("user", Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task StreamDownloadAsync_Returns_416_Given_Unsatisfiable_Range()
    {
        var transfer = SuccessfulTransfer(size: 256);
        SetupTransfer(transfer, incompleteBytes: null);

        var context = CreateContext(range: "bytes=9999-");

        await Controller.WithContext(context).StreamDownloadAsync("user", transfer.Id.ToString(), CancellationToken.None);

        Assert.Equal(416, context.Response.StatusCode);
        Assert.Equal("bytes */256", context.Response.Headers.ContentRange.ToString());
    }

    [Fact]
    public async Task StreamDownloadAsync_Returns_Full_File_Given_No_Range()
    {
        var bytes = TestBytes(256);
        var transfer = SuccessfulTransfer(size: bytes.Length);
        SetupTransfer(transfer, incompleteBytes: bytes);

        var (context, body) = CreateContextWithBody(range: null);

        await Controller.WithContext(context).StreamDownloadAsync("user", transfer.Id.ToString(), CancellationToken.None);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("bytes", context.Response.Headers.AcceptRanges.ToString());
        Assert.Equal(bytes, body.ToArray());
    }

    [Fact]
    public async Task StreamDownloadAsync_Returns_Partial_Content_Given_Range()
    {
        var bytes = TestBytes(256);
        var transfer = SuccessfulTransfer(size: bytes.Length);
        SetupTransfer(transfer, incompleteBytes: bytes);

        var (context, body) = CreateContextWithBody(range: "bytes=100-199");

        await Controller.WithContext(context).StreamDownloadAsync("user", transfer.Id.ToString(), CancellationToken.None);

        Assert.Equal(206, context.Response.StatusCode);
        Assert.Equal("bytes 100-199/256", context.Response.Headers.ContentRange.ToString());
        Assert.Equal(bytes[100..200], body.ToArray());
    }

    [Fact]
    public async Task StreamDownloadAsync_Returns_404_Given_Already_Failed_Transfer()
    {
        var bytes = TestBytes(256);
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            Username = "user",
            Filename = "music\\user\\song.bin",
            Size = 256,
            BytesTransferred = 100,
            EndedAt = DateTime.UtcNow,
            Exception = "peer went away",
            BatchId = Guid.NewGuid(),
        };
        SetupTransfer(transfer, incompleteBytes: bytes[..100]);

        var context = CreateContext(range: null);

        await Controller.WithContext(context).StreamDownloadAsync("user", transfer.Id.ToString(), CancellationToken.None);

        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task StreamDownloadAsync_Serves_Final_File_After_Move()
    {
        var bytes = TestBytes(256);
        var transfer = SuccessfulTransfer(size: bytes.Length);

        // incomplete file is gone (moved); final file lives under the batch destination
        var destDir = Path.Combine(Downloads, "metrolist", "test");
        Directory.CreateDirectory(destDir);
        File.WriteAllBytes(Path.Combine(destDir, "song.bin"), bytes);

        DownloadsMock
            .Setup(d => d.Find(It.IsAny<Expression<Func<Transfer, bool>>>()))
            .Returns(transfer);
        DownloadsMock
            .Setup(d => d.GetIncompleteFilename(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Path.Combine(Incomplete, "song.bin"));
        BatchesMock
            .Setup(b => b.FindAsync(It.IsAny<Expression<Func<Batch, bool>>>()))
            .ReturnsAsync(new Batch
            {
                Id = transfer.BatchId.Value,
                Options = new BatchOptions { Destination = "metrolist/test" },
            });

        var (context, body) = CreateContextWithBody(range: null);

        await Controller.WithContext(context).StreamDownloadAsync("user", transfer.Id.ToString(), CancellationToken.None);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(bytes, body.ToArray());
    }

    private static byte[] TestBytes(int length)
    {
        var bytes = new byte[length];
        new Random(7).NextBytes(bytes);
        return bytes;
    }

    private Transfer SuccessfulTransfer(int size)
    {
        return new Transfer
        {
            Id = Guid.NewGuid(),
            Username = "user",
            Filename = "music\\user\\song.bin",
            Size = size,
            BytesTransferred = size,
            EndedAt = DateTime.UtcNow,
            BatchId = Guid.NewGuid(),
        };
    }

    private void SetupTransfer(Transfer transfer, byte[] incompleteBytes)
    {
        var incompletePath = Path.Combine(Incomplete, "song.bin");

        if (incompleteBytes is not null)
        {
            File.WriteAllBytes(incompletePath, incompleteBytes);
        }

        DownloadsMock
            .Setup(d => d.Find(It.IsAny<Expression<Func<Transfer, bool>>>()))
            .Returns(transfer);
        DownloadsMock
            .Setup(d => d.GetIncompleteFilename(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(incompletePath);
        BatchesMock
            .Setup(b => b.FindAsync(It.IsAny<Expression<Func<Batch, bool>>>()))
            .ReturnsAsync(new Batch
            {
                Id = transfer.BatchId.Value,
                Options = new BatchOptions { Destination = "metrolist/test" },
            });
    }

    private static DefaultHttpContext CreateContext(string range)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;

        if (range is not null)
        {
            context.Request.Headers["Range"] = range;
        }

        return context;
    }

    private static (DefaultHttpContext Context, MemoryStream Body) CreateContextWithBody(string range)
    {
        var context = CreateContext(range);
        var body = new MemoryStream();
        context.Response.Body = body;
        return (context, body);
    }
}

internal static class TransfersControllerStreamTestExtensions
{
    internal static TransfersController WithContext(this TransfersController controller, DefaultHttpContext context)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = context,
        };

        return controller;
    }
}
