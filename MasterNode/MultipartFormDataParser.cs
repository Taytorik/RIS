using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MasterNode
{
    public class MultipartParser
    {
        public List<FilePart> Files { get; } = new List<FilePart>();
        public Dictionary<string, string> Parameters { get; } = new Dictionary<string, string>();

        public MultipartParser(Stream stream, string contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                throw new ArgumentException("Content type cannot be null or empty");

            Parse(stream, contentType);
        }

        private void Parse(Stream stream, string contentType)
        {
            string boundary = ExtractBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
                throw new ArgumentException("Boundary not found in content type");

            // Читаем поток как бинарные данные
            using (var memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                memoryStream.Position = 0;
                ParseBinaryContent(memoryStream.ToArray(), boundary);
            }
        }

        private void ParseBinaryContent(byte[] content, string boundary)
        {
            byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
            List<byte[]> parts = SplitByteArray(content, boundaryBytes);

            foreach (byte[] part in parts)
            {
                if (part.Length == 0) continue;

                ParseBinaryPart(part);
            }
        }

        private void ParseBinaryPart(byte[] part)
        {
            byte[] headerEndPattern = new byte[] { 0x0D, 0x0A, 0x0D, 0x0A }; 
            int headerEndIndex = FindPattern(part, headerEndPattern);

            if (headerEndIndex == -1) return;

            byte[] headerBytes = new byte[headerEndIndex];
            Array.Copy(part, 0, headerBytes, 0, headerEndIndex);
            byte[] bodyBytes = new byte[part.Length - headerEndIndex - headerEndPattern.Length];
            Array.Copy(part, headerEndIndex + headerEndPattern.Length, bodyBytes, 0, bodyBytes.Length);

            string headers = Encoding.UTF8.GetString(headerBytes);
            ParseHeaders(headers, bodyBytes);
        }

        private void ParseHeaders(string headers, byte[] body)
        {
            var headerLines = headers.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            string contentDisposition = "";

            foreach (string line in headerLines)
            {
                if (line.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
                {
                    contentDisposition = line;
                    break;
                }
            }

            if (string.IsNullOrEmpty(contentDisposition))
                return;

            if (contentDisposition.Contains("filename="))
            {
                ParseFilePart(contentDisposition, body);
            }
            else
            {
                ParseParameterPart(contentDisposition, body);
            }
        }

        private void ParseFilePart(string contentDisposition, byte[] body)
        {
            string fileName = ExtractValue(contentDisposition, "filename");
            string fieldName = ExtractValue(contentDisposition, "name");

            if (!string.IsNullOrEmpty(fileName))
            {
                Files.Add(new FilePart
                {
                    FieldName = fieldName,
                    FileName = fileName,
                    DataBytes = body,
                    Data = Encoding.UTF8.GetString(body) 
                });
            }
        }

        private void ParseParameterPart(string contentDisposition, byte[] body)
        {
            string fieldName = ExtractValue(contentDisposition, "name");
            if (!string.IsNullOrEmpty(fieldName))
            {
                Parameters[fieldName] = Encoding.UTF8.GetString(body);
            }
        }

        private List<byte[]> SplitByteArray(byte[] source, byte[] separator)
        {
            var parts = new List<byte[]>();
            int startIndex = 0;

            while (true)
            {
                int index = FindPattern(source, separator, startIndex);
                if (index == -1) break;

                if (index > startIndex)
                {
                    byte[] part = new byte[index - startIndex];
                    Array.Copy(source, startIndex, part, 0, part.Length);
                    parts.Add(part);
                }

                startIndex = index + separator.Length;
            }

            return parts;
        }

        private int FindPattern(byte[] source, byte[] pattern, int startIndex = 0)
        {
            for (int i = startIndex; i <= source.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        private string ExtractValue(string contentDisposition, string key)
        {
            var pattern = key + @"=""([^""]*)""";
            var match = Regex.Match(contentDisposition, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        private string ExtractBoundary(string contentType)
        {
            var match = Regex.Match(contentType, @"boundary=(?<boundary>[^\s;]+)");
            return match.Success ? match.Groups["boundary"].Value.Trim('"') : null;
        }
    }

    public class FilePart
    {
        public string FieldName { get; set; }
        public string FileName { get; set; }
        public string Data { get; set; } 
        public byte[] DataBytes { get; set; } 
    }
}