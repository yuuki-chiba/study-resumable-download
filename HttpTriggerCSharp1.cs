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
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using FileStreamResult = Microsoft.AspNetCore.Mvc.FileStreamResult;

namespace Company.Function
{
    public static class HttpTriggerCSharp1
    {
        private static readonly int BfSize = 1024;

        [FunctionName("HttpTriggerCSharp1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            var pathToVideos = @"path\to\videos\dir";
            var file = @"filename.extension";
            using var provider = new PhysicalFileProvider(pathToVideos);
            IFileInfo fileInfo = provider.GetFileInfo(file);

            //POC: 
            string startQStr = req.Query["start"];
            string endQStr = req.Query["end"];

            if (req.Headers["Range"].ToString() != "" || (startQStr != null && endQStr != null))
            {
                var firstRange = req.Headers["Range"].ToString().Split(',')[0];
                var pattern = @"bytes=(\d+)-(\d+)";
                var start = Int64.Parse(startQStr ?? Regex.Replace(firstRange, pattern, "$1"));
                var end = Int64.Parse(endQStr ?? Regex.Replace(firstRange, pattern, "$2"));

                //TODO: not support 0 Byte file
                end = (fileInfo.Length - 1 > end) ? end : fileInfo.Length;
                var count = (end > start) ? end - start + 1 : 1;
                
                await using var oStream = File.OpenRead($"{pathToVideos}{Path.DirectorySeparatorChar}{file}");
                //TODO: it is necessary to refactor
                var pos = oStream.Seek(start, SeekOrigin.Begin);

                var res = req.HttpContext.Response;
                res.Headers.Add("Content-Range", $"bytes {start}-{end}/{fileInfo.Length}");
                res.Headers.Add("Content-Length", $"{count}");
                res.StatusCode = 206;
                //TODO: TBD
                res.ContentType = "application/octet-stream";
                await using var iStream = res.Body;

                //TODO: it is necessary to refactor
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
                req.HttpContext.Response.Headers.Add("Accept-Ranges", "bytes");

                await using var oStream = fileInfo.CreateReadStream();
                var contentType = GetType(fileInfo.PhysicalPath);
                return new FileStreamResult(oStream, contentType);
            }
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
