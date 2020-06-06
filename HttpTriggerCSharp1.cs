using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Web.Mvc;
using System.Web.WebPages;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using FileStreamResult = Microsoft.AspNetCore.Mvc.FileStreamResult;

namespace Company.Function
{
    public static class HttpTriggerCSharp1
    {
        private static readonly int BfSize = 1024;

        [FunctionName("HttpTriggerCSharp1")]
        public static async Task<IActionResult> RunWithRange(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "head", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            log.LogInformation($"[{req.Method}] {req.Path}, "
                               + $"Header: {JsonConvert.SerializeObject(req.Headers/*, Formatting.Indented*/)}, "
                               + $"Query: {JsonConvert.SerializeObject(req.Query/*, Formatting.Indented*/)}");

            try
            {
                ConfigWrapper config =
                    new ConfigWrapper(new ConfigurationBuilder()
                    //.SetBasePath(Directory.GetCurrentDirectory())
                    //.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build());

                var pathToFiles = config.DataDirectory;
                var file = config.TargetFileName;
                using var provider = new PhysicalFileProvider(pathToFiles);
                IFileInfo fileInfo = provider.GetFileInfo(file);
                var contentType = GetType(fileInfo.PhysicalPath);

                log.LogInformation($"File: {fileInfo.PhysicalPath}");

                //POC: for test run
                string startQStr = req.Query["start"];
                string endQStr = req.Query["end"];

                if (req.Headers["Range"].ToString() != "" || (startQStr != null && endQStr != null))
                {
                    //HACK: ref - https://triple-underscore.github.io/RFC7233-ja.html
                    // Better to use the library...
                    var firstRange = req.Headers["Range"].ToString().Split(',')[0];
                    var pattern = @"bytes=(\d*)-(\d*)";
                    var startStr = startQStr ?? Regex.Replace(firstRange, pattern, "$1");
                    var endStr = endQStr ?? Regex.Replace(firstRange, pattern, "$2");
                    var start = startStr.IsEmpty() ? 0 : Int64.Parse(startStr);
                    var end = endStr.IsEmpty() ? fileInfo.Length - 1 : Int64.Parse(endStr);

                    //TODO: not support 0 Byte file
                    end = (fileInfo.Length - 1 > end) ? end : fileInfo.Length - 1;
                    var count = (end > start) ? end - start + 1 : 1;

                    await using var oStream = File.OpenRead($"{pathToFiles}{Path.DirectorySeparatorChar}{file}");
                    var pos = oStream.Seek(start, SeekOrigin.Begin);

                    var res = req.HttpContext.Response;
                    res.Headers.Add("Content-Range", $"bytes {start}-{end}/{fileInfo.Length}");
                    res.Headers.Add("Content-Length", $"{count}");
                    log.LogInformation($"Send data: {count} bytes, {start}-{end}/{fileInfo.Length}");
                    //TODO: TBD
                    res.ContentType = contentType/*"application/octet-stream"*/;

                    if (req.Method.ToLower() == "head")
                    {
                        return new OkResult();
                    }

                    res.StatusCode = 206;

                    await using var iStream = res.Body;

                    //TODO: it is necessary to refactor because too slow...
                    long remain = count;
                    byte[] b = new byte[BfSize];
                    for (; remain > BfSize; remain -= BfSize)
                    {
                        int writable = await oStream.ReadAsync(b, 0, b.Length);
                        await iStream.WriteAsync(b, 0, writable);
                    }
                    await oStream.ReadAsync(b, 0, b.Length);
                    await iStream.WriteAsync(b, 0, (int)remain);
                    return null;
                }
                else
                {
                    var res = req.HttpContext.Response;
                    res.Headers.Add("Accept-Ranges", "bytes");

                    if (req.Method.ToLower() == "head")
                    {
                        res.Headers.Add("Content-Length", fileInfo.Length.ToString());
                        res.Headers.Add("Content-Type", contentType);
                        return new OkResult();
                    }

                    /*await using*/var oStream = fileInfo.CreateReadStream();
                    return new FileStreamResult(oStream, contentType);
                }
            }
            catch (Exception e)
            {
                log.LogError(e, "Error occured!");
                throw;
            }
        }

        [FunctionName("HttpTriggerCSharp2")]
        public static async Task<IActionResult> RunWithoutRange(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            log.LogInformation($"[{req.Method}] {req.Path}, "
                               + $"Header: {JsonConvert.SerializeObject(req.Headers/*, Formatting.Indented*/)}, "
                               + $"Query: {JsonConvert.SerializeObject(req.Query/*, Formatting.Indented*/)}");

            ConfigWrapper config =
                new ConfigWrapper(new ConfigurationBuilder()
                //.SetBasePath(Directory.GetCurrentDirectory())
                //.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build());
            var pathToVideos = config.DataDirectory;
            var file = config.TargetFileName;
            using var provider = new PhysicalFileProvider(pathToVideos);
            IFileInfo fileInfo = provider.GetFileInfo(file);
            var contentType = GetType(fileInfo.PhysicalPath);

            log.LogInformation($"File: {fileInfo.PhysicalPath}");

            /*await using*/var oStream = fileInfo.CreateReadStream();
            return new FileStreamResult(oStream, contentType);
        }

        private static string GetType(string fileName)
        {
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fileName, out var contentType))
            {
                contentType = "application/octet-stream";
            }
            return contentType;
        }
    }
}
