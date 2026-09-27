namespace QuickerPlaces.Services.Activity;

/// <summary>Finds the UNC spelling of a folder reached through a mapped drive, if any.</summary>
public interface INetworkDriveResolver
{
    string? GetNetworkPath(string driveLetterPath);
}
