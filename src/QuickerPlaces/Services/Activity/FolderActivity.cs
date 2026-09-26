using System;

namespace QuickerPlaces.Services.Activity;

/// <summary>One folder's row in a period: the grid's Folder, Time, Visits and Last visited (plan 5.4, D23).</summary>
public sealed record FolderActivity(string Folder, TimeSpan Time, int Visits, DateTimeOffset LastVisited);
