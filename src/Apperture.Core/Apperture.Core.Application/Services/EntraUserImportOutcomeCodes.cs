namespace Apperture.Core.Application.Services;

/// <summary>Stable per-item codes for Entra directory user → managed contact import (API/UI/tests).</summary>
public static class EntraUserImportOutcomeCodes
{
    public const string InvalidObjectId = "invalid_object_id";
    public const string DirectoryError = "directory_error";
    public const string UserNotFoundInDirectory = "user_not_found_in_directory";
    public const string EmailConflictExistingContact = "email_conflict_existing_contact";
    public const string Success = "success";
    public const string AlreadyLinked = "already_linked";
    public const string ImportFailed = "import_failed";
}
