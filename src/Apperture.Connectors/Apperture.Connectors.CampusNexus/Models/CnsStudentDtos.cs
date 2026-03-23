namespace Apperture.Connectors.CampusNexus.Models;

public sealed class Student
{
    public int Id { get; set; }
    public int CampusId { get; set; }
    public string StudentNumber { get; set; } = string.Empty;
}

public sealed class StudentEnrollmentPeriod
{
    public int Id { get; set; }
}

public sealed class StudentAcademicYear
{
    public int Id { get; set; }
    public int StudentEnrollmentPeriodId { get; set; }
    public int Sequence { get; set; }
    public decimal? CostOfAttendance { get; set; }
}
