using System;
using System.IO;
using Xunit;

namespace WebMVC.Tests;

public static class TestPathHelper
{
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ExtraEfficiency.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir != null, "Could not locate solution root (ExtraEfficiency.slnx)");
        return dir.FullName;
    }
}
