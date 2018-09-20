using System;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace AutoCodeFix
{
    public class SetAutoCodeFixEnvironment : Task
    {
        [Required]
        public bool DesignTimeBuild { get; set; }

        public override bool Execute()
        {
            if (!DesignTimeBuild)
                Environment.SetEnvironmentVariable("AutoCodeFix", bool.TrueString, EnvironmentVariableTarget.Process);

            return true;
        }
    }
}
