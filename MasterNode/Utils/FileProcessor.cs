using System;
using System.IO;
using System.Text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;

namespace MasterNode.Utils
{
    public class FileProcessor
    {
        public static FileProcessingResult ProcessFile(byte[] fileData, string fileName, string originalFileName)
        {
            try
            {
                string extension = System.IO.Path.GetExtension(fileName).ToLower();
                string textContent;

                switch (extension)
                {
                    case ".txt":
                        textContent = ProcessTextFile(fileData);
                        break;
                    case ".pdf":
                        textContent = ProcessPdfFile(fileData);
                        break;
                    case ".doc":
                    case ".docx":
                        textContent = ProcessWordFile(fileData, extension);
                        break;
                    case ".rtf":
                        textContent = ProcessRtfFile(fileData);
                        break;
                    case ".odt":
                        textContent = ProcessOdtFile(fileData);
                        break;
                    default:
                        throw new NotSupportedException($"File format {extension} is not supported");
                }

                return new FileProcessingResult
                {
                    Success = true,
                    TextContent = textContent,
                    FileName = originalFileName,
                    FileSize = fileData.Length,
                    CharacterCount = textContent.Length
                };
            }
            catch (Exception ex)
            {
                return new FileProcessingResult
                {
                    Success = false,
                    ErrorMessage = $"Error processing file: {ex.Message}"
                };
            }
        }

        private static string ProcessTextFile(byte[] fileData)
        {
            // Автоматическое определение кодировки
            Encoding encoding = DetectEncoding(fileData) ?? Encoding.UTF8;
            return encoding.GetString(fileData);
        }

        private static string ProcessPdfFile(byte[] fileData)
        {
            using (var stream = new MemoryStream(fileData))
            using (var reader = new PdfReader(stream))
            {
                var text = new StringBuilder();

                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    text.Append(PdfTextExtractor.GetTextFromPage(reader, i));
                }

                return text.ToString();
            }
        }

        private static string ProcessWordFile(byte[] fileData, string extension)
        {
            if (extension == ".docx")
            {
                // Простая обработка DOCX через регулярные выражения
                return ExtractTextFromDocx(fileData);
            }

            throw new NotSupportedException("Word document processing requires additional libraries. Please install DocumentFormat.OpenXml for DOCX support.");
        }

        private static string ProcessRtfFile(byte[] fileData)
        {
            // Простая обработка RTF - удаляем RTF теги
            string rtfContent = Encoding.UTF8.GetString(fileData);
            return StripRtf(rtfContent);
        }

        private static string ProcessOdtFile(byte[] fileData)
        {
            throw new NotSupportedException("ODT file processing requires additional libraries. Please install appropriate ODT processing library.");
        }

        private static string ExtractTextFromDocx(byte[] fileData)
        {
            try
            {
                string content = Encoding.UTF8.GetString(fileData);
                return System.Text.RegularExpressions.Regex.Replace(content, @"<[^>]+>", string.Empty)
                                 .Replace("</w:t>", " ")
                                 .Replace("<w:t>", "")
                                 .Replace("&gt;", ">")
                                 .Replace("&lt;", "<")
                                 .Replace("&amp;", "&")
                                 .Replace("&#xA;", "\n")
                                 .Replace("&#xD;", "\r")
                                 .Trim();
            }
            catch (Exception ex)
            {
                return $"DOCX file content extraction requires DocumentFormat.OpenXml library. Error: {ex.Message}";
            }
        }

        private static string StripRtf(string rtfContent)
        {
            try
            {
                string result = System.Text.RegularExpressions.Regex.Replace(rtfContent, @"\\[^\s]+\s?", " ")
                                   .Replace("\\par", "\n")
                                   .Replace("\\tab", "\t")
                                   .Replace("\\'", "") 
                                   .Replace("{", "")
                                   .Replace("}", "")
                                   .Trim();

                result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
                return result;
            }
            catch
            {
                return "RTF content could not be processed";
            }
        }

        private static Encoding DetectEncoding(byte[] fileData)
        {
            if (fileData.Length >= 3 && fileData[0] == 0xEF && fileData[1] == 0xBB && fileData[2] == 0xBF)
                return Encoding.UTF8;
            if (fileData.Length >= 2 && fileData[0] == 0xFF && fileData[1] == 0xFE)
                return Encoding.Unicode;
            if (fileData.Length >= 2 && fileData[0] == 0xFE && fileData[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            try
            {
                string utf8String = Encoding.UTF8.GetString(fileData);
                return Encoding.UTF8;
            }
            catch
            {
                try
                {
                    return Encoding.GetEncoding(1251);
                }
                catch
                {
                    return Encoding.ASCII;
                }
            }
        }
    }

    public class FileProcessingResult
    {
        public bool Success { get; set; }
        public string TextContent { get; set; }
        public string FileName { get; set; }
        public int FileSize { get; set; }
        public int CharacterCount { get; set; }
        public string ErrorMessage { get; set; }
    }
}