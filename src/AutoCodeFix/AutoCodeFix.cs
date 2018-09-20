using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Options;

/// <summary>
/// AutoCodeFix helpers.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AutoCodeFix
{
    /// <summary>
    /// Gets the optional auto code fix settings from the configured analyzer options.
    /// </summary>
    /// <remarks>
    /// To persist additional settings for your code fixes, add <c>AutoFixSetting</c> items 
    /// via your project. The following is an example of the default settings already 
    /// included by AutoCodeFix:
    /// 
    /// <code>
    ///     <ItemGroup>
    ///       <AutoFixSetting Include="IntermediateOutputPath" Value="$([MSBuild]::NormalizeDirectory($(IntermediateOutputPath)))" />
    ///       <AutoFixSetting Include="AutoFix" Value="@(AutoFix)" />
    ///     </ItemGroup>
    /// </code>
    /// </remarks>
    public static IDictionary<string, string> GetAutoFixSettings(this AnalyzerOptions options)
    {
        var metadataFile = options?.AdditionalFiles.FirstOrDefault(x => x.Path.EndsWith("AutoCodeFix.ini", StringComparison.OrdinalIgnoreCase));

        var settings = metadataFile == null ? new Dictionary<string, string>() : 
            File.ReadAllLines(metadataFile.Path)
                .Where(line => !string.IsNullOrEmpty(line))
                .Select(line => line.Split('='))
                .Where(pair => pair.Length == 2)
                .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim());

        if (AppContext.TryGetSwitch("IsBuildTime", out var enabled) && enabled)
            settings["IsBuildTime"] = bool.TrueString;

        return settings;
    }
}