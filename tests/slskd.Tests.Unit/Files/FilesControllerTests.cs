using Microsoft.Extensions.Options;

namespace slskd.Tests.Unit.Files
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Abstractions;
    using Microsoft.AspNetCore.Mvc.Infrastructure;
    using Microsoft.AspNetCore.Routing;
    using Moq;
    using slskd.Files;
    using slskd.Files.API;
    using Xunit;

    public class FilesControllerTests : IDisposable
    {
        public FilesControllerTests()
        {
            Temp = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}");
            Downloads = Path.Combine(Temp, "downloads");
            Directory.CreateDirectory(Downloads);

            var options = new Options
            {
                Directories = new Options.DirectoriesOptions
                {
                    Downloads = Downloads,
                    Incomplete = Path.Combine(Temp, "incomplete"),
                },
            };

            var monitorMock = new Mock<IOptionsMonitor<Options>>();
            monitorMock.Setup(m => m.CurrentValue).Returns(options);

            var snapshotMock = new Mock<IOptionsSnapshot<Options>>();
            snapshotMock.Setup(s => s.Value).Returns(options);

            Controller = new FilesController(new FileService(monitorMock.Object), snapshotMock.Object);
        }

        public void Dispose()
        {
            Directory.Delete(Temp, recursive: true);
        }

        private FilesController Controller { get; init; }
        private string Temp { get; init; }
        private string Downloads { get; init; }

        [Fact]
        public void GetDownloadFile_Returns_BadRequest_Given_Traversal_Path()
        {
            var result = Controller.GetDownloadFile(ToBase64("../evil.txt"));

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void GetDownloadFile_Returns_BadRequest_Given_Nested_Traversal_Path()
        {
            var result = Controller.GetDownloadFile(ToBase64("subdir/../../evil.txt"));

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void GetDownloadFile_Returns_BadRequest_Given_Invalid_Base64()
        {
            var result = Controller.GetDownloadFile("!!!not-base64!!!");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void GetDownloadFile_Returns_NotFound_Given_Missing_File()
        {
            var result = Controller.GetDownloadFile(ToBase64("nope.flac"));

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public void GetDownloadFile_Returns_NotFound_Given_Absolute_Path_Outside_Downloads()
        {
            var outside = Path.Combine(Temp, "outside.txt");
            File.WriteAllText(outside, "secret");

            var result = Controller.GetDownloadFile(ToBase64(outside));

            // leading separators are stripped and the remainder resolves under downloads, where it doesn't exist
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task GetDownloadFile_Returns_Full_File_Given_No_Range()
        {
            var bytes = new byte[1024];
            new Random(42).NextBytes(bytes);
            File.WriteAllBytes(Path.Combine(Downloads, "test.bin"), bytes);

            var result = Controller.GetDownloadFile(ToBase64("test.bin"));

            var file = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal("application/octet-stream", file.ContentType);
            Assert.True(file.EnableRangeProcessing);

            var (context, body) = CreateContext(range: null);
            await file.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));

            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal(bytes, body.ToArray());
            Assert.Equal("bytes", context.Response.Headers.AcceptRanges.ToString());
        }

        [Fact]
        public async Task GetDownloadFile_Returns_Partial_Content_Given_Range()
        {
            var bytes = new byte[1024];
            new Random(42).NextBytes(bytes);
            File.WriteAllBytes(Path.Combine(Downloads, "test.bin"), bytes);

            var result = Controller.GetDownloadFile(ToBase64("test.bin"));

            var file = Assert.IsType<PhysicalFileResult>(result);

            var (context, body) = CreateContext(range: "bytes=100-199");
            await file.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));

            Assert.Equal(206, context.Response.StatusCode);
            Assert.Equal("bytes 100-199/1024", context.Response.Headers.ContentRange.ToString());
            Assert.Equal(bytes[100..200], body.ToArray());
        }

        [Fact]
        public async Task GetDownloadFile_Returns_416_Given_Unsatisfiable_Range()
        {
            var bytes = new byte[1024];
            new Random(42).NextBytes(bytes);
            File.WriteAllBytes(Path.Combine(Downloads, "test.bin"), bytes);

            var result = Controller.GetDownloadFile(ToBase64("test.bin"));

            var file = Assert.IsType<PhysicalFileResult>(result);

            var (context, _) = CreateContext(range: "bytes=9999-");
            await file.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));

            Assert.Equal(416, context.Response.StatusCode);
        }

        [Fact]
        public void GetDownloadFile_Accepts_UrlSafe_Base64()
        {
            // multibyte name so the standard encoding contains '+' and/or '/' that must survive as '-' and '_'
            var name = "müsic ♥ test.flac";
            File.WriteAllBytes(Path.Combine(Downloads, name), [1, 2, 3, 4]);

            var standard = ToBase64(name);
            var urlSafe = standard.TrimEnd('=').Replace('+', '-').Replace('/', '_');

            var result = Controller.GetDownloadFile(urlSafe);

            var file = Assert.IsType<PhysicalFileResult>(result);
            Assert.True(file.EnableRangeProcessing);
        }

        private static string ToBase64(string str) => Convert.ToBase64String(Encoding.UTF8.GetBytes(str));

        private static (DefaultHttpContext Context, MemoryStream Body) CreateContext(string range)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Get;
            var body = new MemoryStream();
            context.Response.Body = body;
            // PhysicalFileResult resolves its executor from request services at execution time
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Mock.Of<IWebHostEnvironment>());
            services.AddSingleton<IActionResultExecutor<PhysicalFileResult>, PhysicalFileResultExecutor>();
            context.RequestServices = services.BuildServiceProvider();

            if (range is not null)
            {
                context.Request.Headers["Range"] = range;
            }

            return (context, body);
        }
    }
}
