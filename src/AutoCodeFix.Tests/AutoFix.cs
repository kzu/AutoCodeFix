using Xunit;
using Gherkinator;
using static Gherkinator.Syntax;

namespace AutoCodeFix.Tests
{
    public class AutoFix
    {
        [Fact]
        public void can_apply_custom_analyer_and_code_fix()
            => Scenario()
                .UseMSBuild()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.csproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.csproj", "Build").AssertSuccess())
                .Run();

        [Fact]
        public void can_apply_StyleCop_code_fix_automatically()
            => Scenario()
                .UseMSBuild()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.csproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.csproj", "Build"))
                .Then("build succeeds", c => c.AssertSuccess())
                .Run();

        [Fact]
        public void can_apply_StyleCop_batch_code_fix()
            => Scenario()
                .UseMSBuild()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.csproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.csproj", "Build"))
                .Then("build succeeds", c => c.AssertSuccess())
                .Run();

        [Fact]
        public void can_apply_RefactoringEssentials_code_fix_automatically()
            => Scenario()
                .UseMSBuild()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.csproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.csproj", "Build").AssertSuccess())
                .Run();

        [Fact]
        public void can_apply_Roslynator_code_fix_automatically()
            => Scenario()
                .UseMSBuild()
                .UseAutoCodeFix()
                .When("restoring packages", c => c.Build("Foo.csproj", "Restore").AssertSuccess())
                .And("building project", c => c.Build("Foo.csproj", "Build").AssertSuccess())
                .Run();
    }
}
