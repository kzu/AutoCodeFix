using Microsoft.Build.Utilities;

namespace AutoCodeFix
{
    /// <summary>
    /// Initializes the <see cref="AssemblyResolver"/> so that 
    /// the additional assemblies can be loaded by the time the 
    /// <see cref="ApplyCodeFixes"/> task runs.
    /// </summary>
    public class ResolveLocalAssemblies : Task
    {
        public override bool Execute()
        {
            AssemblyResolver.Init();
            return true;
        }
    }
}