Feature: AutoFix
  Applies code fixes during build

  Background: A common import of the package targets directly
    Given Directory.Build.targets = 
"""
<Project>
    <Import Project="$(AutoCodeFixPath)build\AutoCodeFix.targets" />
</Project>
"""
    And NuGet.Config = 
"""
<configuration>
	<packageSources>
		<clear />
		<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
		<add key="offline" value="%USERPROFILE%\.nuget\packages" />
		<add key="dotnet" value="C:\Program Files\dotnet\sdk\NuGetFallbackFolder" />
        <add key="local" value="%TEMP%\packages" />
  </packageSources>
</configuration>
"""

  Scenario: Can apply custom analyer and code fix
    Given Foo.csproj =
"""
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>netstandard2.0</TargetFramework>
    </PropertyGroup>
    <ItemGroup>
        <Analyzer Include="$(CurrentDirectory)AutoCodeFix.Tests.dll" />
    </ItemGroup>
    <ItemGroup>
        <AutoFix Include="TEST001" />
    </ItemGroup>
</Project>
"""
    And Class1.cs = 
"""
public class Class1 
{
    public void Foo() { }
}
"""
    When restoring packages
    And building project
    Then Class1.cs = 
"""
public class Class1 
{
    public virtual void Foo() { }
}
"""

  Scenario: Can apply StyleCop code fix automatically
    Given Foo.csproj =
"""
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>netstandard2.0</TargetFramework>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="xunit" Version="2.4.0" PrivateAssets="all" />
        <PackageReference Include="StyleCop.Analyzers" Version="1.1.0-beta009" PrivateAssets="all" />
    </ItemGroup>
    <ItemGroup>
        <!-- System usings should go first --> 
        <AutoFix Include="SA1208" />
        <!-- Blank line required before single-line comment -->
        <AutoFix Include="SA1515" />
    </ItemGroup>
</Project>
"""
    And Class1.cs = 
"""
using Xunit;
using System;

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("Hello");
        // This comment is too tight
        Console.ReadLine();
    }
}
"""
    When restoring packages
    And building project
    Then build succeeds
    And Class1.cs = 
"""
using System;
using Xunit;

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("Hello");

        // This comment is too tight
        Console.ReadLine();
    }
}
"""

  Scenario: Can apply RefactoringEssentials code fix automatically
    Given Foo.csproj =
"""
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>netstandard2.0</TargetFramework>
        <CodeAnalysisRuleSet>Rules.ruleset</CodeAnalysisRuleSet>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="RefactoringEssentials" Version="5.6.0" PrivateAssets="all" />
    </ItemGroup>
    <ItemGroup>
        <AutoFix Include="RECS0085" />
    </ItemGroup>
</Project>
"""
    And Rules.ruleset = 
"""
<RuleSet Name="Rules" ToolsVersion="15.0">
  <Rules AnalyzerId="RefactoringEssentials" RuleNamespace="RefactoringEssentials">
    <Rule Id="RECS0085" Action="Warning" />
  </Rules>
</RuleSet>
"""
    And Class1.cs = 
"""
using System;

public static class Program
{
    public static void Main()
    {
        int[] values = new int[] { 1, 2, 3 };
        Console.WriteLine(values);
    }
}
"""
    When restoring packages
    And building project
    Then Class1.cs = 
"""
using System;

public static class Program
{
    public static void Main()
    {
        int[] values = { 1, 2, 3 };
        Console.WriteLine(values);
    }
}
"""

  Scenario: Can apply Roslynator code fix automatically
    Given Foo.csproj =
"""
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>netstandard2.0</TargetFramework>
        <CodeAnalysisRuleSet>Rules.ruleset</CodeAnalysisRuleSet>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="Roslynator.Analyzers" Version="1.9.0" />
        <PackageReference Include="Roslynator.CodeFixes" Version="1.9.0" />
    </ItemGroup>
    <ItemGroup>
        <AutoFix Include="RCS1003" />
    </ItemGroup>
</Project>
"""
    And Rules.ruleset = 
"""
<RuleSet Name="Rules" ToolsVersion="15.0">
  <Rules AnalyzerId="Roslynator.CSharp.Analyzers" RuleNamespace="Roslynator.CSharp.Analyzers">
    <Rule Id="RCS1003" Action="Warning" />
  </Rules>
</RuleSet>
"""
    And Class1.cs = 
"""
using System;

public static class Program
{
    public static void Main()
    {
        if (Console.ReadLine().Length == 0)
            Console
                .WriteLine("true");
        else
            Console
                .WriteLine("false");
    }
}
"""
    When restoring packages
    And building project
    Then Class1.cs = 
"""
using System;

public static class Program
{
    public static void Main()
    {
        if (Console.ReadLine().Length == 0)
        {
            Console
               .WriteLine("true");
        }
        else
        {
            Console
               .WriteLine("false");
        }
    }
}
"""
