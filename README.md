# AutoCodeFix

Applies Roslyn code fixes automatically during build for the chose "auto codefix" diagnostic ids.

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

The analyzers and code fixes available during compilation are the same used during design time in the 
IDE, as well as build time, added to the project via the `<Analyzer Include="..." />` item group. Analyzers 
distributed via nuget packages already add those automatically to your project (such as the StyleCop.Analyzers 
example above).


// To determine if the code fixers are being called from the build or from the IDE.
var isBuildTime = AppContext.TryGetSwitch("IsBuildTime", out var isEnabled) && isEnabled;

flag first-pass codegen required:

