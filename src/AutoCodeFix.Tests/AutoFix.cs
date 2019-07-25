using Xunit;
using Gherkinator;
using System.Diagnostics;

namespace AutoCodeFix.Tests
{
    public class AutoFix
    {
        [Fact]
        public void can_apply_custom_analyer_and_code_fix()
            => new BuildScenario()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.csproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.csproj", "Build").AssertSuccess())
                .Run();

        [Fact]
        public void can_apply_StyleCop_code_fix_automatically()
            => new BuildScenario()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.csproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.csproj", "Build"))
                .Then("build succeeds", c => c.AssertSuccess())
                .Run();

        [Fact]
        public void Can_preserve_preprocessor_symbols()
            => new BuildScenario()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.csproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.csproj", "Build"))
                .Then("build succeeds", c => c.AssertSuccess())
                .Run();

        [Fact]
        public void Can_preserve_preprocessor_symbols_vb()
            => new BuildScenario()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.vbproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.vbproj", "Build"))
                .Then("build succeeds", c => c.AssertSuccess())
                .Run();

        [Fact]
        public void Can_apply_NET_analyzer_code_fix_automatically_in_VB()
            => new BuildScenario()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.vbproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.vbproj", "Build"))
                .Then("build succeeds", c => c.AssertSuccess())
                .Run();

        [Fact]
        public void can_apply_StyleCop_batch_code_fix()
            => new BuildScenario()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.csproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.csproj", "Build"))
                .Then("build succeeds", c => c.AssertSuccess())
                .Run();

        [Fact]
        public void can_apply_RefactoringEssentials_code_fix_automatically()
            => new BuildScenario()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.csproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.csproj", "Build").AssertSuccess())
                .Run();

        [Fact]
        public void can_apply_Roslynator_code_fix_automatically()
            => new BuildScenario()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.csproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.csproj", "Build").AssertSuccess())
                .Run();
    }
}
