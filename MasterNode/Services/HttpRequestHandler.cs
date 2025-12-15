using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MasterNode.Models;
using MasterNode.Utils;
using Newtonsoft.Json;

namespace MasterNode.Services
{
    public class HttpRequestHandler
    {
        private readonly TaskDistributor _taskDistributor;
        private readonly string _uploadDirectory;
        private readonly string _baseDirectory;

        public HttpRequestHandler(TaskDistributor taskDistributor, string uploadDirectory)
        {
            _taskDistributor = taskDistributor;
            _uploadDirectory = uploadDirectory;
            _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }

        public async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                SetupCorsHeaders(response);

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                if (request.HttpMethod == "GET")
                {
                    await HandleGetRequest(request, response);
                    return;
                }
//
                if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/summarize-test")
                {
                    await HandlePerformanceTest(request, response);
                    return;
                }

                if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/summarize")
                {
                    if (request.ContentType?.StartsWith("multipart/form-data") == true)
                    {
                        await HandleFileUpload(request, response);
                    }
                    else
                    {
                        await HandleJsonRequest(request, response);
                    }
                }
                else
                {
                    await SendErrorResponse(response, 404, "Endpoint not found");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing request: {ex.Message}");
                await SendErrorResponse(response, 500, "Internal server error");
            }
            finally
            {
                response.Close();
            }
        }

        private async Task HandleFileUpload(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                var parser = new MultipartParser(request.InputStream, request.ContentType);

                if (parser.Files.Count == 0)
                {
                    await SendErrorResponse(response, 400, "No file uploaded");
                    return;
                }

                var filePart = parser.Files[0];
                string fileName = filePart.FileName ?? "uploaded_file.txt";

                float ratio = 0.3f;
                if (parser.Parameters.ContainsKey("ratio"))
                {
                    if (float.TryParse(parser.Parameters["ratio"], out float parsedRatio))
                    {
                        ratio = parsedRatio / 100f;
                    }
                }

                var processingResult = FileProcessor.ProcessFile(
                    filePart.DataBytes ?? Encoding.UTF8.GetBytes(filePart.Data),
                    fileName,
                    fileName
                );

                if (!processingResult.Success)
                {
                    await SendErrorResponse(response, 400, processingResult.ErrorMessage);
                    return;
                }

                string savedFilePath = Path.Combine(_uploadDirectory, fileName);
                File.WriteAllBytes(savedFilePath,
                    filePart.DataBytes ?? Encoding.UTF8.GetBytes(filePart.Data));

                var summary = await _taskDistributor.DistributeTaskAsync(
                    processingResult.TextContent, ratio, fileName);

                if (!string.IsNullOrEmpty(summary))
                {
                    var summaryResponse = new SummaryResponse
                    {
                        TaskId = Guid.NewGuid().ToString(),
                        OriginalLength = processingResult.CharacterCount,
                        SummaryLength = summary.Length,
                        Summary = summary,
                        Status = "success",
                        FileName = fileName,
                        CompressionRatio = 1 - (float)summary.Length / processingResult.CharacterCount
                    };

                    await SendSuccessResponse(response, summaryResponse);
                }
                else
                {
                    await SendErrorResponse(response, 500, "Failed to process text summary");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file upload: {ex.Message}");
                await SendErrorResponse(response, 500, $"Error processing file upload: {ex.Message}");
            }
        }

        private async Task HandleJsonRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                string requestBody = await reader.ReadToEndAsync();

                try
                {
                    var summaryRequest = JsonConvert.DeserializeObject<SummaryRequest>(requestBody);

                    if (summaryRequest == null || string.IsNullOrWhiteSpace(summaryRequest.Text))
                    {
                        await SendErrorResponse(response, 400, "Invalid request body");
                        return;
                    }

                    if (summaryRequest.Ratio <= 0 || summaryRequest.Ratio > 1)
                    {
                        summaryRequest.Ratio = 0.3f;
                    }

                    if (string.IsNullOrEmpty(summaryRequest.FileName))
                    {
                        summaryRequest.FileName = "text_input.txt";
                    }

                    var summary = await _taskDistributor.DistributeTaskAsync(
                        summaryRequest.Text,
                        summaryRequest.Ratio,
                        summaryRequest.FileName
                    );

                    if (!string.IsNullOrEmpty(summary))
                    {
                        var summaryResponse = new SummaryResponse
                        {
                            TaskId = Guid.NewGuid().ToString(),
                            OriginalLength = summaryRequest.Text.Length,
                            SummaryLength = summary.Length,
                            Summary = summary,
                            Status = "success",
                            FileName = summaryRequest.FileName
                        };

                        await SendSuccessResponse(response, summaryResponse);
                    }
                    else
                    {
                        await SendErrorResponse(response, 500, "Failed to process text summary");
                    }
                }
                catch (JsonException jsonEx)
                {
                    Console.WriteLine($"JSON parsing error: {jsonEx.Message}");
                    await SendErrorResponse(response, 400, "Invalid JSON format");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing JSON request: {ex.Message}");
                    await SendErrorResponse(response, 500, $"Error processing request: {ex.Message}");
                }
            }
        }

        private async Task HandleGetRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.Url.AbsolutePath == "/status")
            {
                await HandleStatusRequest(response);
            }
            else
            {
                await ServeStaticFile(request, response);
            }
        }

        private async Task HandleStatusRequest(HttpListenerResponse response)
        {
            try
            {
                var status = _taskDistributor.GetSystemStatus();
                await SendSuccessResponse(response, status);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting system status: {ex.Message}");
                await SendErrorResponse(response, 500, $"Error getting system status: {ex.Message}");
            }
        }

        private async Task ServeStaticFile(HttpListenerRequest request, HttpListenerResponse response)
        {
            string filePath = GetFilePath(request.Url.AbsolutePath);

            if (!File.Exists(filePath))
            {
                filePath = Path.Combine(_baseDirectory, "index.html");
            }

            try
            {
                string extension = Path.GetExtension(filePath).ToLower();
                var mimeTypes = new Dictionary<string, string>
                {
                    [".html"] = "text/html; charset=utf-8",
                    [".htm"] = "text/html; charset=utf-8",
                    [".css"] = "text/css",
                    [".js"] = "application/javascript",
                    [".png"] = "image/png",
                    [".jpg"] = "image/jpeg",
                    [".jpeg"] = "image/jpeg",
                    [".gif"] = "image/gif",
                    [".ico"] = "image/x-icon",
                    [".txt"] = "text/plain",
                    [".json"] = "application/json"
                };

                string contentType = mimeTypes.ContainsKey(extension) ?
                    mimeTypes[extension] : "application/octet-stream";
                byte[] buffer = File.ReadAllBytes(filePath);

                response.ContentType = contentType;
                response.ContentLength64 = buffer.Length;
                response.StatusCode = 200;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error serving file {filePath}: {ex.Message}");
                response.StatusCode = 404;
            }
        }

        private string GetFilePath(string urlPath)
        {
            if (urlPath == "/" || string.IsNullOrEmpty(urlPath))
            {
                return Path.Combine(_baseDirectory, "index.html");
            }

            string relativePath = urlPath.TrimStart('/');
            string fullPath = Path.Combine(_baseDirectory, relativePath);

            if (!fullPath.StartsWith(_baseDirectory))
            {
                return Path.Combine(_baseDirectory, "index.html");
            }

            return fullPath;
        }

        private void SetupCorsHeaders(HttpListenerResponse response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        }

        private async Task SendSuccessResponse(HttpListenerResponse response, object data)
        {
            response.StatusCode = 200;
            response.ContentType = "application/json; charset=utf-8";

            string jsonResponse = JsonConvert.SerializeObject(data, Formatting.Indented);
            byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task SendErrorResponse(HttpListenerResponse response, int statusCode, string message)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";

            var errorResponse = new { error = message, status = "error" };
            string jsonResponse = JsonConvert.SerializeObject(errorResponse, Formatting.Indented);

            byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        //
        private async Task HandlePerformanceTest(HttpListenerRequest request, HttpListenerResponse response)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                string requestBody = await reader.ReadToEndAsync();

                try
                {
                    var summaryRequest = JsonConvert.DeserializeObject<SummaryRequest>(requestBody);

                    if (summaryRequest == null || string.IsNullOrWhiteSpace(summaryRequest.Text))
                    {
                        await SendErrorResponse(response, 400, "Invalid request body");
                        return;
                    }

                    // Параметр для выбора режима
                    bool forceSingleThread = request.QueryString["mode"] == "single";

                    string summary;
                    string mode;

                    if (forceSingleThread)
                    {
                        // Однопоточный режим
                        mode = "SINGLE_THREAD";
                        summary = _taskDistributor.ProcessInSingleThreadMode(summaryRequest.Text, summaryRequest.Ratio);
                    }
                    else
                    {
                        // Многопоточный режим
                        mode = "MULTI_THREAD";
                        summary = await _taskDistributor.DistributeTaskAsync(
                            summaryRequest.Text, summaryRequest.Ratio, summaryRequest.FileName);
                    }

                    var summaryResponse = new SummaryResponse
                    {
                        TaskId = Guid.NewGuid().ToString(),
                        OriginalLength = summaryRequest.Text.Length,
                        SummaryLength = summary.Length,
                        Summary = summary,
                        Status = "success",
                        FileName = summaryRequest.FileName,
                        CompressionRatio = 1 - (float)summary.Length / summaryRequest.Text.Length
                    };

                    // Добавить информацию о времени в ответ
                    var enhancedResponse = new
                    {
                        summaryResponse.TaskId,
                        summaryResponse.OriginalLength,
                        summaryResponse.SummaryLength,
                        summaryResponse.Summary,
                        summaryResponse.Status,
                        summaryResponse.FileName,
                        summaryResponse.CompressionRatio,
                        ProcessingMode = mode,
                        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };

                    await SendSuccessResponse(response, enhancedResponse);
                }
                catch (Exception ex)
                {
                    await SendErrorResponse(response, 500, $"Error: {ex.Message}");
                }
            }
        }
    }
}