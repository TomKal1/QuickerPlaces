using System;
using System.IO;

namespace QuickerPlaces.RevitHandler.Core
{
    public static class ProtocolRoot
    {
        public const string EnvironmentVariable = "QUICKERPLACES_REVIT_ROOT";

        /// <summary>
        /// The protocol root: <c>QUICKERPLACES_REVIT_ROOT</c> when it is set to an absolute path,
        /// otherwise <c>%LocalAppData%\QuickerPlaces\revit</c>. A relative value is ignored.
        /// </summary>
        public static string Resolve(Func<string, string?> getEnvironmentVariable, string localAppData)
        {
            string? overridden = getEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridden) && PathRules.IsAbsolute(overridden!.Trim()))
                return overridden.Trim();

            return Path.Combine(localAppData, "QuickerPlaces", "revit");
        }

        public static string Resolve()
            => Resolve(Environment.GetEnvironmentVariable,
                       Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }
}
