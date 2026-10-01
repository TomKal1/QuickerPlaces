using System.IO;
using QuickerPlaces.Services;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>The command line (configurable canvas plan M3) and where --data-root puts the stores.</summary>
public sealed class StartupOptionsTests
{
    [Fact]
    public void No_arguments_means_the_everyday_start()
    {
        var options = StartupOptions.Parse(new string[0]);

        Assert.False(options.Tray);
        Assert.False(options.Workspace);
        Assert.Null(options.DataRoot);
        Assert.Empty(options.Problems);
    }

    [Fact]
    public void Switches_ignore_case()
    {
        var options = StartupOptions.Parse(new[] { "--TRAY", "--Workspace" });

        Assert.True(options.Tray);
        Assert.True(options.Workspace);
    }

    [Fact]
    public void Data_root_takes_the_next_argument()
    {
        var options = StartupOptions.Parse(new[] { "--workspace", "--data-root", @"C:\qp test" });

        Assert.Equal(@"C:\qp test", options.DataRoot);
        Assert.True(options.Workspace);
    }

    [Fact]
    public void Data_root_takes_an_equals_value_without_quotes()
    {
        var options = StartupOptions.Parse(new[] { "--data-root=\"D:\\fixtures\"" });

        Assert.Equal(@"D:\fixtures", options.DataRoot);
    }

    [Theory]
    [InlineData("--data-root")]
    [InlineData("--data-root=")]
    public void Data_root_without_a_folder_is_a_problem_not_a_root(string arg)
    {
        var options = StartupOptions.Parse(new[] { arg });

        Assert.Null(options.DataRoot);
        Assert.Single(options.Problems);
    }

    [Fact]
    public void Data_root_does_not_swallow_the_next_switch()
    {
        var options = StartupOptions.Parse(new[] { "--data-root", "--workspace" });

        Assert.Null(options.DataRoot);
        Assert.True(options.Workspace);
        Assert.Single(options.Problems);
    }

    [Fact]
    public void Unknown_arguments_are_ignored()
    {
        var options = StartupOptions.Parse(new[] { "--verbose", "file.txt" });

        Assert.Equal(StartupOptions.Parse(new string[0]) with { Problems = options.Problems }, options);
        Assert.Empty(options.Problems);
    }

    [Fact]
    public void A_data_root_splits_local_and_roaming_under_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "qp-root");

        Assert.Equal(Path.Combine(root, "Local"), AppDataFolders.LocalFor(root));
        Assert.Equal(Path.Combine(root, "Roaming"), AppDataFolders.RoamingFor(root));
        Assert.NotEqual(AppDataFolders.LocalFor(null), AppDataFolders.LocalFor(root));
    }

    [Fact]
    public void Instance_scope_is_null_for_the_everyday_stores()
        => Assert.Null(AppDataFolders.InstanceScope(null));

    [Fact]
    public void Instance_scope_is_stable_and_ignores_case_and_a_trailing_separator()
    {
        var root = Path.Combine(Path.GetTempPath(), "QP-Test");
        var scope = AppDataFolders.InstanceScope(root);

        Assert.NotNull(scope);
        Assert.Equal(16, scope!.Length);
        Assert.Equal(scope, AppDataFolders.InstanceScope(root.ToLowerInvariant() + Path.DirectorySeparatorChar));
        Assert.NotEqual(scope, AppDataFolders.InstanceScope(root + "2"));
    }
}
