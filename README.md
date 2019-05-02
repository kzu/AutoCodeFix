![Icon](https://raw.github.com/kzu/AutoCodeFix/master/icon/32.png) AutoCodeFix
============

Applies Roslyn code fixes automatically during build for the chosen "auto codefix" diagnostics, fixing 
the code for you automatically, instead of having to manually apply code fixes in the IDE.

In the project file, specify the diagnostic identifiers you want to apply code fixes automatically to. 
This example uses two diagnostics from the [StyleCop.Analyzers](https://www.nuget.org/packages/StyleCop.Analyzers) 
package:

```xml
<ItemGroup>
    <PackageReference Include="StyleCop.Analyzers" Version="1.1.1-rc*" />
    <PackageReference Include="AutoCodeFix" Version="*" />
</ItemGroup>

<ItemGroup>
    <!-- System usings should go first --> 
    <AutoCodeFix Include="SA1208" />
    <!-- Field names should not begin with underscore -->
    <AutoCodeFix Include="SA1309" />
</ItemGroup>
```

The analyzers and code fixes available during build are the same used during design time in the 
IDE, added to the project via the `<Analyzer Include="..." />` item group. Analyzers 
distributed via nuget packages already add those automatically to your project (such as the 
[StyleCop.Analyzers](https://www.nuget.org/packages/StyleCop.Analyzers) , 
[RefactoringEssentials](https://www.nuget.org/packages/RefactoringEssentials), 
[Roslynator.Analyzers](https://www.nuget.org/packages/Roslynator.Analyzers) and 
[Roslynator.CodeFixes](https://www.nuget.org/Roslynator.CodeFixes), etc).

It's important to note that by default, the compiler *has* to emit the diagnostics you want them
fixed automatically. For diagnostics that are of `Info` severity by default (i.e. [RCS1003: Add braces to if-else](https://github.com/JosefPihrt/Roslynator/blob/master/docs/analyzers/RCS1003.md)) 
you can bump its severity to `Warning` so that `AutoCodeFix` can properly process them automatically on the next build.

You [configure analyzers](https://docs.microsoft.com/en-us/visualstudio/code-quality/use-roslyn-analyzers?view=vs-2017) using 
the built-in editor in VS, which for the example above, would result in a rule set like the following:

```xml
<RuleSet Name="MyRules" ToolsVersion="15.0">
  <Rules AnalyzerId="Roslynator.CSharp.Analyzers" RuleNamespace="Roslynator.CSharp.Analyzers">
    <Rule Id="RCS1003" Action="Warning" />
  </Rules>
</RuleSet>
```

With that configuration in place, you can add the `AutoCodeFix` just like before:

```xml
<ItemGroup>
    <AutoCodeFix Include="RCS1003" />
</ItemGroup>
```

When no fixable diagnostics are emitted by the compiler, the impact of `AutoCodeFix` on build times 
is virtually none.


> NOTE: the main use case for `AutoCodeFix` is to fix the code as you go, on every build. Therefore, it performs
> best when warnings to fix are few and introduced in between builds. Although it can be used to apply fixes to 
> entire code bases for normalization/compliance purposes as a one-time fixup, that can take some time, even if 
> a [Fix All](https://github.com/dotnet/roslyn/blob/master/docs/analyzers/FixAllProvider.md) provider exists 
> for the diagnostics. Run time is also impacted by the complexity of the code fix itself. As an example, 
> the StyleCop code fix for [usings sorting](https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/SA1208.md) 
> can fix ~60 instances per second on my Dell XPS 13 9370.


> Icon [Gear](https://thenounproject.com/term/gear/2069169/) by 
> [Putra Theoo](https://thenounproject.com/tnputra555), 
> from [The Noun Project](https://thenounproject.com/)