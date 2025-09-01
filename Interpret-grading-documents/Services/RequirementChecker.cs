using System.Text.Json;
using System.Text.Json.Serialization;
using Interpret_grading_documents.Models;

namespace Interpret_grading_documents.Services
{
    public class CourseEquivalents
    {
        [JsonPropertyName("subjects")]
        public List<Subject> Subjects { get; set; }
    }

    public class Subject
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("courses")]
        public List<Course> Courses { get; set; }
    }

    public class Course
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("alternatives")]
        public List<AlternativeCourse> Alternatives { get; set; } = new List<AlternativeCourse>();

        [JsonPropertyName("requiredGrade")]
        public string RequiredGrade { get; set; }

        [JsonPropertyName("includeInAverage")]
        public bool IncludeInAverage { get; set; }
    }

    public class AlternativeCourse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; }
    }

    // Removed RequirementResult class from here

    public static class RequirementChecker
    {
        private static readonly Dictionary<string, double> GradeMappings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "A", 20 },
            { "B", 17.5 },
            { "C", 15 },
            { "D", 12.5 },
            { "E", 10 },
            { "F", 0 },

            { "MVG", 20 },
            { "VG", 15 },
            { "G", 10 },
            { "IG", 0 },

            { "5", 20 },
            { "4", 17.5 },
            { "3", 15 },
            { "2", 12.5 },
            { "1", 10 },
            { "0", 0 }
        };

        private static CourseEquivalents LoadCourseEquivalents(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
            {
                throw new FileNotFoundException("Course equivalents JSON file not found at " + jsonFilePath);
            }

            string jsonContent = File.ReadAllText(jsonFilePath);
            return JsonSerializer.Deserialize<CourseEquivalents>(jsonContent);
        }

        private static List<CourseForAverage> LoadCoursesForAverage(string coursesForAverageFilePath)
        {
            if (System.IO.File.Exists(coursesForAverageFilePath))
            {
                var jsonContent = System.IO.File.ReadAllText(coursesForAverageFilePath);
                return JsonSerializer.Deserialize<List<CourseForAverage>>(jsonContent);
            }
            return null;
        }

        public static Dictionary<string, RequirementResult> DoesStudentMeetRequirement(
            GPTService.GraduationDocument document,
            string jsonFilePath)
        {
            var courseEquivalents = LoadCourseEquivalents(jsonFilePath);
            return DoesStudentMeetRequirement(document, courseEquivalents);
        }

        public static Dictionary<string, RequirementResult> DoesStudentMeetRequirement(GPTService.GraduationDocument document, CourseEquivalents courseEquivalents)
        {
            if (courseEquivalents?.Subjects == null)
                throw new InvalidOperationException("Course equivalents are missing or malformed.");

            var allRequirementsMet = new Dictionary<string, RequirementResult>(StringComparer.OrdinalIgnoreCase);

            foreach (var subject in courseEquivalents.Subjects)
            {
                foreach (var course in subject.Courses)
                {
                    string requiredCourseNameOrCode = course.Name;
                    string requiredGrade = course.RequiredGrade;
                    int requiredLevel = course.Level;

                    double requiredGradeValue = GetGradeValue(requiredGrade);
                    var equivalentCourses = GetEquivalentCourses(requiredCourseNameOrCode, courseEquivalents);

                    bool courseRequirementMet = false;
                    string studentGradeInRequiredCourse = null;
                    string studentGrade = "N/A";
                    bool metByAlternativeCourse = false;
                    bool metByHigherLevelCourse = false;
                    string fulfillingCourseName = requiredCourseNameOrCode;
                    string higherLevelCourseGrade = null;
                    string higherLevelCourseName = null;
                    var otherAlternativeGrades = new List<string>();
                    bool failedRequiredCourseOrAlternative = false;

                    foreach (var studentSubject in document.Subjects)
                    {
                        if (studentSubject.SubjectName.Trim().Equals(requiredCourseNameOrCode, StringComparison.OrdinalIgnoreCase) ||
                            studentSubject.CourseCode.Trim().Equals(requiredCourseNameOrCode, StringComparison.OrdinalIgnoreCase))
                        {
                            studentGradeInRequiredCourse = studentSubject.Grade.Trim();
                            double studentGradeValue = GetGradeValue(studentGradeInRequiredCourse);

                            if (studentGradeValue >= requiredGradeValue && studentGradeValue > 0)
                            {
                                courseRequirementMet = true;
                                studentGrade = studentGradeInRequiredCourse;
                                fulfillingCourseName = requiredCourseNameOrCode;
                            }
                            else if (studentGradeValue == 0)
                            {
                                failedRequiredCourseOrAlternative = true;
                                studentGrade = studentGradeInRequiredCourse;
                                fulfillingCourseName = requiredCourseNameOrCode;
                            }
                        }
                        else
                        {
                            foreach (var equivalentCourse in equivalentCourses)
                            {
                                if (equivalentCourse.Name.Equals(studentSubject.SubjectName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                    equivalentCourse.Code.Equals(studentSubject.CourseCode.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    double studentGradeValue = GetGradeValue(studentSubject.Grade.Trim());

                                    if (studentGradeValue >= requiredGradeValue && studentGradeValue > 0)
                                    {
                                        if (equivalentCourse.Level == requiredLevel)
                                        {
                                            metByAlternativeCourse = true;
                                            studentGrade = studentSubject.Grade;
                                            fulfillingCourseName = equivalentCourse.Name;
                                            courseRequirementMet = true;
                                        }
                                        else if (equivalentCourse.Level > requiredLevel)
                                        {
                                            metByHigherLevelCourse = true;
                                            higherLevelCourseGrade = studentSubject.Grade;
                                            higherLevelCourseName = equivalentCourse.Name;
                                        }
                                    }
                                    else if (studentGradeValue == 0)
                                    {
                                        failedRequiredCourseOrAlternative = true;
                                        otherAlternativeGrades.Add($"{studentSubject.SubjectName}: {studentSubject.Grade}");
                                    }
                                    else
                                    {
                                        otherAlternativeGrades.Add($"{studentSubject.SubjectName}: {studentSubject.Grade}");
                                    }

                                    break;
                                }
                            }
                        }
                    }

                    if (failedRequiredCourseOrAlternative)
                    {
                        courseRequirementMet = false;
                    }

                    if (failedRequiredCourseOrAlternative)
                    {
                        studentGrade = "F";
                    }

                    if (courseRequirementMet && metByAlternativeCourse)
                    {
                        fulfillingCourseName = equivalentCourses.First(ec => ec.Name.Equals(fulfillingCourseName, StringComparison.OrdinalIgnoreCase)).Name;
                    }

                    allRequirementsMet[requiredCourseNameOrCode] = new RequirementResult
                    {
                        CourseName = fulfillingCourseName,
                        RequiredGrade = requiredGrade,
                        IsMet = courseRequirementMet,
                        StudentGrade = studentGrade,
                        MetByAlternativeCourse = metByAlternativeCourse,
                        AlternativeCourseName = metByAlternativeCourse ? fulfillingCourseName : null,
                        AlternativeCourseGrade = metByAlternativeCourse ? studentGrade : null,
                        MetByHigherLevelCourse = metByHigherLevelCourse,
                        HigherLevelCourseName = metByHigherLevelCourse ? higherLevelCourseName : null,
                        HigherLevelCourseGrade = metByHigherLevelCourse ? higherLevelCourseGrade : null,
                        OtherGradesInAlternatives = otherAlternativeGrades
                    };
                }
            }

            return allRequirementsMet;
        }



        public static double GetGradeValue(string grade)
        {
            if (GradeMappings.TryGetValue(grade.Trim().ToUpper(), out double value))
            {
                return value;
            }
            else
            {
                return 0;
            }
        }

        private static List<Course> GetEquivalentCourses(string courseNameOrCode, CourseEquivalents courseEquivalents)
        {

            if (courseEquivalents?.Subjects == null)
                throw new InvalidOperationException("Course equivalents are missing or malformed.");

            var equivalentCourses = new List<Course>();

            if (courseEquivalents?.Subjects == null) return equivalentCourses;

            foreach (var subject in courseEquivalents.Subjects)
            {
                var required = subject.Courses?.FirstOrDefault(c =>
                    string.Equals(c.Name?.Trim(), courseNameOrCode?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Code?.Trim(), courseNameOrCode?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    (c.Alternatives != null && c.Alternatives.Any(a =>
                        string.Equals(a.Name?.Trim(), courseNameOrCode?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a.Code?.Trim(), courseNameOrCode?.Trim(), StringComparison.OrdinalIgnoreCase)))
                );

                if (required != null)
                {
                    int requiredLevel = required.Level;

                    var higherOrEqual = (subject.Courses ?? Enumerable.Empty<Course>()).Where(c => c.Level >= requiredLevel);

                    foreach (var c in higherOrEqual)
                    {
                        equivalentCourses.Add(new Course { Name = c.Name, Code = c.Code, Level = c.Level });
                        if (c.Alternatives != null)
                        {
                            foreach (var alt in c.Alternatives)
                                equivalentCourses.Add(new Course { Name = alt.Name, Code = alt.Code, Level = c.Level });
                        }
                    }
                    break;
                }
            }

            if (equivalentCourses.Count == 0)
            {
                // Fallback: treat the original as its own equivalent
                equivalentCourses.Add(new Course { Name = courseNameOrCode, Code = courseNameOrCode, Level = 0 });
            }

            return equivalentCourses;
        }


        public static double CalculateAverageGrade(GPTService.GraduationDocument document, CourseEquivalents courseEquivalents)
        {
            if (courseEquivalents?.Subjects == null)
                throw new InvalidOperationException("Course equivalents are missing or malformed.");

            double totalWeightedGradePoints = 0;
            int totalCoursePoints = 0;

            foreach (var subject in courseEquivalents.Subjects)
            {
                foreach (var course in subject.Courses)
                {
                    if (course.IncludeInAverage)
                    {
                        var equivalentCourses = GetEquivalentCourses(course.Name, courseEquivalents);

                        foreach (var studentSubject in document.Subjects)
                        {
                            if (equivalentCourses.Any(ec =>
                                    ec.Name.Equals(studentSubject.SubjectName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                    ec.Code.Equals(studentSubject.CourseCode.Trim(), StringComparison.OrdinalIgnoreCase)))
                            {
                                double studentGradeValue = GetGradeValue(studentSubject.Grade.Trim());
                                int studentCoursePoints = int.Parse(studentSubject.GymnasiumPoints);

                                totalWeightedGradePoints += studentGradeValue * studentCoursePoints;
                                totalCoursePoints += studentCoursePoints;
                                Console.WriteLine($"Course Name: {studentSubject.SubjectName}");
                                Console.WriteLine($"Grade: {studentSubject.Grade}");
                                Console.WriteLine($"Points: {studentSubject.GymnasiumPoints}");
                                Console.WriteLine($"GradeValue: {studentGradeValue}");

                                Console.WriteLine($"{studentGradeValue} * {studentSubject.GymnasiumPoints} = {totalWeightedGradePoints} ");

                                break; // Move to the next course after a match
                            }
                        }
                    }
                }
            }

            if (totalCoursePoints == 0)
                return 0;

            double average = totalWeightedGradePoints / totalCoursePoints;
            Console.WriteLine($"Average points: {Math.Round(average, 2)}");

            return Math.Round(average, 2); // Round to 2 decimal places if desired
        }

        public static Dictionary<string, MeritPointResult> GetCourseMeritPoints(GPTService.GraduationDocument document, string jsonFilePathForAverage)
        {
            var courseMeritPoints = new Dictionary<string, MeritPointResult>();
            var coursesForAverage = LoadCoursesForAverage(jsonFilePathForAverage);
            if (coursesForAverage == null) return courseMeritPoints;

            foreach (var courseForAverage in coursesForAverage)
            {
                // Create a list of equivalent course codes (original + alternatives)
                var equivalentCourses = new List<string> { courseForAverage.Code };
                equivalentCourses.AddRange(courseForAverage.AlternativeCourses.Select(alt => alt.Code));

                bool matchFound = false;

                foreach (var studentSubject in document.Subjects)
                {
                    // Check if the student's course code matches any equivalent course code
                    if (equivalentCourses.Contains(studentSubject.CourseCode.Trim(), StringComparer.OrdinalIgnoreCase))
                    {
                        double studentGradeValue = RequirementChecker.GetGradeValue(studentSubject.Grade.Trim());
                        double meritPoints = studentGradeValue;

                        // Create a MeritPointResult object for the course
                        var meritPointResult = new MeritPointResult
                        {
                             CourseName = studentSubject.SubjectName,
                             StudentGrade = studentSubject.Grade,
                             MeritPoint = meritPoints,
                             OriginalCourseGrade = courseForAverage.Name,
                             AlternativeCourseGrade = courseForAverage.AlternativeCourses.FirstOrDefault()?.Name,
                             OtherGradesInAlternatives = courseForAverage.AlternativeCourses.Select(alt => alt.Name).ToList()
                        };

                        // Add the course result to the dictionary
                        courseMeritPoints[studentSubject.SubjectName] = meritPointResult;
                        matchFound = true;
                        break; // Move to the next course after a match
                    }
                }

                // If no match was found, add the course with "N/A" grade and 0 merit points
                if (!matchFound)
                {
                    var meritPointResult = new MeritPointResult
                    {
                        CourseName = courseForAverage.Name,
                        StudentGrade = "N/A",
                        MeritPoint = 0,
                        OriginalCourseGrade = courseForAverage.Name,
                        AlternativeCourseGrade = courseForAverage.AlternativeCourses.FirstOrDefault()?.Name,
                        OtherGradesInAlternatives = courseForAverage.AlternativeCourses.Select(alt => alt.Name).ToList()
                    };

                    courseMeritPoints[courseForAverage.Name] = meritPointResult;
                }
            }

            return courseMeritPoints;
        }
    }
}
