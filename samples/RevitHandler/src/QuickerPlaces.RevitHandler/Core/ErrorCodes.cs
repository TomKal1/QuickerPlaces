namespace QuickerPlaces.RevitHandler.Core
{
    /// <summary>The <c>errorCode</c> values of protocol version 1.</summary>
    public static class ErrorCodes
    {
        public const string InvalidRequest = "invalidRequest";
        public const string UnsupportedProtocol = "unsupportedProtocol";
        public const string UnsupportedAction = "unsupportedAction";
        public const string Expired = "expired";
        public const string CentralNotFound = "centralNotFound";
        public const string NotCentral = "notCentral";
        public const string ReleaseMismatch = "releaseMismatch";
        public const string LocalFolderUnavailable = "localFolderUnavailable";
        public const string CreateLocalFailed = "createLocalFailed";
        public const string OpenFailed = "openFailed";
        public const string InternalError = "internalError";
    }
}
