using System;
using System.Collections.Generic;

namespace QuickerPlaces.Services.Activity;

/// <summary>One folder's row in a period: the grid's Folder, Time, Visits and Last visited (plan 5.4, D23).</summary>
public sealed record FolderActivity(string Folder, TimeSpan Time, int Visits, DateTimeOffset LastVisited);

/// <summary>One day of a root's kept folder detail: each folder's time and visits that day.</summary>
public sealed record FolderDay(DateOnly Date, IReadOnlyList<FolderActivity> Folders);
