# AutoCodeFix

Applies Roslyn code fixes automatically during build for the chosen "auto codefix" diagnostics, fixing 
the code for you automatically, instead of having to manually apply code fixes in the IDE.

In the project file, specify the diagnostic identifiers you want to apply code fixes automatically to. 
This example uses two diagnostics from the [StyleCop.Analyzers](https://www.nuget.org/packages/StyleCop.Analyzers) 
package:

```xml
<ItemGroup>
        <!-- System usings should go first --> 
        <AutoFix Include="SA1208" />
        <!-- Blank line required before single-line comment -->
        <AutoFix Include="SA1515" />
</ItemGroup>
```

The analyzers and code fixes available during build are the same used during design time in the 
IDE, added to the project via the `<Analyzer Include="..." />` item group. Analyzers 
distributed via nuget packages already add those automatically to your project (such as the StyleCop.Analyzers, 
RefactoringEssentials, Roslynator.Analyzers and Roslynator.CodeFixes, etc).

It's important to note that by default, the compiler *has* to emit the diagnostics you intend to auto 
fix. For diagnostics that are of `Info` severity by default (i.e. [RCS1003: Add braces to if-else](https://github.com/JosefPihrt/Roslynator/blob/master/docs/analyzers/RCS1003.md)) 
you can bump its severity to `Warning` so that `AutoCodeFix` can process them automatically on the next build. 

You [configure analyzers](https://docs.microsoft.com/en-us/visualstudio/code-quality/use-roslyn-analyzers?view=vs-2017) using 
the built-in editor in VS, which for the example above, would result in a rule set like the following:

```xml
<RuleSet Name="MyRules" ToolsVersion="15.0">
  <Rules AnalyzerId="Roslynator.CSharp.Analyzers" RuleNamespace="Roslynator.CSharp.Analyzers">
    <Rule Id="RCS1003" Action="Warning" />
  </Rules>
</RuleSet>
```

With that configuration in place, you can add the `AutoFix` just like before:

```xml
<ItemGroup>
        <AutoFix Include="RCS1003" />
</ItemGroup>
```

When no fixable diagnostics are emitted by the compiler, the impact of `AutoCodeFix` on build times 
is virtually none.

