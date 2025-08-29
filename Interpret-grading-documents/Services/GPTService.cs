using System.Drawing.Imaging;
using System.Security;
using OpenAI.Chat;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;
using Interpret_grading_documents.Data;
using Spire.Pdf;

namespace Interpret_grading_documents.Services
{
    public static class GPTService
    {
        public class GraduationDocument
        {
            public Guid Id { get; set; } = Guid.NewGuid();

            public string DocumentName { get; set; }

            [JsonPropertyName("document_title")]
            public string Title { get; set; }

            [JsonPropertyName("full_name")]
            public string FullName { get; set; }

            [JsonPropertyName("personal_id")]
            public string PersonalId { get; set; }

            [JsonPropertyName("graduation_date")]
            public string GraduationDate { get; set; }

            [JsonPropertyName("school_name")]
            public string SchoolName { get; set; }

            [JsonPropertyName("program_name")]
            public string ProgramName { get; set; }

            [JsonPropertyName("specialization")]
            public string Specialization { get; set; }

            [JsonPropertyName("school_form")]
            public string SchoolForm { get; set; }

            [JsonPropertyName("curriculum")]
            public string Curriculum { get; set; }

            [JsonPropertyName("subjects")]
            public List<Subject> Subjects { get; set; }
            public string FilePath { get; set; }
            public string ContentType { get; set; }

            public ImageReliabilityResult ImageReliability { get; set; }

            public int TotalPoints
            {
                get
                {
                    return Subjects.Sum(s =>
                    {
                        if (int.TryParse(s.GymnasiumPoints, out int points))
                        {
                            return points;
                        }
                        return 0;
                    });
                }
            }
            public string HasValidDegree { get; set; }
        }

        public class Subject
        {
            [JsonPropertyName("subject_name")]
            public string SubjectName { get; set; }

            [JsonPropertyName("course_code")]
            public string? CourseCode { get; set; }

            [JsonPropertyName("grade")]
            public string Grade { get; set; }

            [JsonPropertyName("points")]
            public string? GymnasiumPoints { get; set; }

            public double FuzzyMatchScore { get; set; }

            public string? OriginalSubjectName { get; set; }
            public string? OriginalCourseCode { get; set; }
            public string? OriginalGymnasiumPoints { get; set; }
        }

        public static async Task<GraduationDocument> ProcessTextPrompts(IFormFile uploadedFile)

        {

            string tempFilePath = await SaveUploadedFileAsync(uploadedFile);
            string processedImagePath = null;
            string contentType;

            try
            {
                (processedImagePath, contentType) = await ProcessUploadedFileAsync(tempFilePath);
                //ImageReliabilityResult reliabilityResult = CheckImageReliability(processedImagePath);

                var extension = Path.GetExtension(tempFilePath).ToLower();
                ImageReliabilityResult reliabilityResult = new ImageReliabilityResult { ReliabilityScore = 75, FileFormat = extension };


                ChatClient client = InitializeChatClient();

                List<ChatMessage> messages = PrepareChatMessages(contentType, processedImagePath);
                ChatCompletion chatCompletion = await GetChatCompletionAsync(client, messages);

                GraduationDocument document = DeserializeResponse(chatCompletion.Content[0].Text);

                // Set the document name using the uploaded file's name
                document.DocumentName = Path.GetFileName(uploadedFile.FileName);

                //var test = RequirementChecker.DoesStudentMeetRequirement(document);
                //Console.WriteLine(test);

                var updatedDocument = await CompareCourses(document);
                ValidateDocument(updatedDocument, reliabilityResult);


                ExamValidator(updatedDocument);

                //var test1 = RequirementChecker.CalculateAverageGrade(document);


                return document;
            }
            finally
            {
                CleanUpTempFiles(tempFilePath, processedImagePath);
            }
        }

        public static void ExamValidator(GraduationDocument document)
        {
            if (document.Title.Contains("Examensbevis", StringComparison.OrdinalIgnoreCase) ||
                document.Title.Contains("Gymnasieexamen", StringComparison.OrdinalIgnoreCase) ||
                document.Title.Contains("Slutbetyg", StringComparison.OrdinalIgnoreCase))
            {
                if (document.SchoolForm.Contains("Gymnasieskola", StringComparison.OrdinalIgnoreCase))
                {
                    document.HasValidDegree = "Gymnasieexamen i gymnasieskolan";
                }
                else if (document.SchoolForm.Contains("Kommunal vuxenutbildning", StringComparison.OrdinalIgnoreCase) ||
                         document.SchoolForm.Contains("Komvux utbildning", StringComparison.OrdinalIgnoreCase))
                {
                    document.HasValidDegree = "Examen i kommunal vuxenutbildning";
                }
            }
            else
            {
                document.HasValidDegree = "Dokumentet påvisar ej examensbevis";
            }

        }

        public static string GetHighestExamStatus(List<GraduationDocument> documents)
        {
            // Define the order of precedence for exam statuses
            var examStatuses = new List<string>
            {
                "Gymnasieexamen i gymnasieskolan",
                "Examen i kommunal vuxenutbildning",
                "Dokumentet påvisar ej examensbevis"
            };

            // Iterate through all documents and find the highest precedence status
            foreach (var status in examStatuses)
            {
                if (documents.Any(d => d.HasValidDegree == status))
                {
                    return status;
                }
            }

            return "Dokumentet påvisar ej examensbevis"; // Default if no exam is found
        }

        #region Private Methods

        private static async Task<string> SaveUploadedFileAsync(IFormFile uploadedFile)
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{Path.GetExtension(uploadedFile.FileName).ToLower()}");
            using (var stream = new FileStream(tempFilePath, FileMode.Create))
            {
                await uploadedFile.CopyToAsync(stream);
            }
            return tempFilePath;
        }

        private static async Task<(string processedImagePath, string contentType)> ProcessUploadedFileAsync(string tempFilePath)
        {
            string extension = Path.GetExtension(tempFilePath).ToLower();
            string processedImagePath = tempFilePath;
            string contentType;

            if (extension == ".pdf")
            {
                processedImagePath = await ConvertToJpgAsync(tempFilePath);
                contentType = "image/jpeg";
            }
            else if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".webp")
            {
                contentType = GetImageContentType(extension);
            }
            else
            {
                throw new NotSupportedException("Unsupported file type. Please provide a PDF, JPG, PNG or WEBP file.");
            }

            return (processedImagePath, contentType);
        }

        private static async Task<string> ConvertToJpgAsync(string pdfPath)
        {
            string jpgPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");

            using (PdfDocument pdf = new PdfDocument())
            {
                pdf.LoadFromFile(pdfPath);

                if (pdf.Pages.Count > 0)
                {
                    // Save the first page to an image with 300 DPI
                    var image = pdf.SaveAsImage(0, 300, 300); // Page index starts at 0

                    // Save the image as a JPEG file
                    await Task.Run(() => image.Save(jpgPath, ImageFormat.Jpeg));

                    image.Dispose();
                }
                else
                {
                    throw new InvalidOperationException("No images found in PDF.");
                }
            }

            return jpgPath;
        }

        private static string GetImageContentType(string extension)
        {
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => throw new NotSupportedException("Unsupported image format.")
            };
        }

        private static ImageReliabilityResult CheckImageReliability(string imagePath)
        {
            var checker = new ImageReliabilityChecker();
            try
            {
                return checker.CheckImageReliability(imagePath);
            }
            catch (Exception)
            {
                return new ImageReliabilityResult { ReliabilityScore = 0, FileFormat = "Unknown" };
            }
        }

        private static ChatClient InitializeChatClient()
        {
            string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("API key for GPT is not set in the environment variables.");
            }

            return new ChatClient("gpt-4o", apiKey);
        }

        private static List<ChatMessage> PrepareChatMessages(string contentType, string originalImagePath)
        {
            var chatMessages = new List<ChatMessage>
            {
                new UserChatMessage(
                    ChatMessageContentPart.CreateTextPart("Vänligen extrahera följande data från bilden, svara endast med JSON, formatera det inte med <pre> eller liknande. Säkerställ att alla betygen är korrekta och överenstämmer med deras ämne."),
                    ChatMessageContentPart.CreateTextPart(
                        "0. Dokumentets titel (Exempel: Examensbevis, Slutbetyg, Studiebevis, Samlat betygsdokument\n" +
                        "1. Fullständigt namn\n" +
                        "2. Personnummer\n" +
                        "3. Examensdatum\n" +
                        "4. Skolans namn (Exempel: Bromma gymnasium, YrkesAkademin, NTI Gymnasiet Luleå, Okänt gymnasium)\n" +
                        "5. Programnamn (Exempel: El- och energiprogrammet, Estetiska programmet, Hantverksprogrammet, Okänt program)\n" +
                        "6. Specialisering och detaljer om utbildning\n" +
                        "7. Skolform (En av de följande: Grundskola, Gymnasieskola, Komvux utbildning, Kommunal vuxenutbildning på gymnasienivå,  Högskola, Okänd utbildningsform)\n" +
                        "8. Lista över ämnen med följande detaljer:\n" +
                        "   - Ämnesnamn\n" +
                        "   - Kurskod\n" +
                        "   - Betyg\n" +
                        "   - Poäng (Detta ska alltid vara en sträng)\n" +
                        "Vänligen se till att formatera svaret i JSON-format som detta:\n" +
                        "{\n" +
                        "   'document_title': 'Dokumentets Titel',\n" +
                        "   'full_name': 'Fullständigt Namn',\n" +
                        "   'personal_id': 'xxxxxx-xxxx',\n" +
                        "   'graduation_date': 'ÅÅÅÅ-MM-DD',\n" +
                        "   'school_name': 'Skolans Namn',\n" +
                        "   'program_name': 'Programnamn',\n" +
                        "   'specialization': 'Specialisering',\n" +
                        "   'school_form': 'School Form',\n" +
                        "   'subjects': [\n" +
                        "       {\n" +
                        "           'subject_name': 'Ämnesnamn',\n" +
                        "           'course_code': 'Ämneskod',\n" +
                        "           'grade': 'Betyg',\n" +
                        "           'points': 'Poäng',\n" +
                        "       },\n" +
                        "       ... fler ämnen\n" +
                        "   ]\n" +
                        "}")
                )
            };

            byte[] imageBytes = File.ReadAllBytes(originalImagePath);
            chatMessages.Add(new UserChatMessage(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), contentType)));

            return chatMessages;
        }

        private static async Task<ChatCompletion> GetChatCompletionAsync(ChatClient client, List<ChatMessage> messages)
        {
            var completionOptions = new ChatCompletionOptions
            {
                Temperature = 0.2f,
            };
            return await client.CompleteChatAsync(messages, completionOptions);
        }

        private static GraduationDocument DeserializeResponse(string jsonResponse)
        {
            return JsonSerializer.Deserialize<GraduationDocument>(jsonResponse);
        }

        private static async Task<GraduationDocument> CompareCourses(GraduationDocument document)
        {
            var coursesFromApi = ValidationData.GetCoursesFromApi();

            var updatedDocument = CourseComparator.CompareCourses(coursesFromApi, document, ValidationData.GetCourses());

            return updatedDocument;
        }

        private static void ValidateDocument(GraduationDocument document, ImageReliabilityResult reliabilityResult)
        {
            var checker = new ImageReliabilityChecker();
            bool isDataValid = checker.ValidateData(document);

            if (!isDataValid)
            {
                reliabilityResult.ReliabilityScore = 0;
            }

            document.ImageReliability = reliabilityResult;
        }

        private static void CleanUpTempFiles(string tempFilePath, string processedImagePath)
        {
            try
            {
                File.Delete(tempFilePath);
                if (!tempFilePath.Equals(processedImagePath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(processedImagePath);
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Kunde inte ta bort temporärfilen: {ex.Message}");
            }
        }

        #endregion
    }
}
