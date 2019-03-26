using System;
using System.Collections.Generic;
using System.Reflection;
using AutoCodeFix.Properties;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;

namespace AutoCodeFix
{
    static class BuildExtensions
    {
        public static IDictionary<string, string> GetGlobalProperties(this IBuildEngine buildEngine)
        {
            ProjectInstance project;

            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var engineType = buildEngine.GetType();
            var callbackField = engineType.GetField("targetBuilderCallback", flags);

            if (callbackField != null)
            {
                // .NET field naming convention.
                var callback = callbackField.GetValue(buildEngine);
                var projectField = callback.GetType().GetField("projectInstance", flags);
                project = (ProjectInstance)projectField.GetValue(callback);
            }
            else
            {
                callbackField = engineType.GetField("_targetBuilderCallback", flags);
                if (callbackField == null)
                    throw new NotSupportedException($"Failed to introspect current MSBuild Engine '{engineType.AssemblyQualifiedName}'.");

                // OSS field naming convention.
                var callback = callbackField.GetValue(buildEngine);
                var projectField = callback.GetType().GetField("_projectInstance", flags);
                project = (ProjectInstance)projectField.GetValue(callback);
            }

            return project.GlobalProperties;
        }

        /// <summary>
        /// Gets a previously registered task object of a given type, 
        /// with a lifetime determined by the <paramref name="buildingInsideVisualStudio"/> 
        /// value (<see cref="RegisteredTaskObjectLifetime.AppDomain"/> if <see langword="true"/>).
        /// </summary>
        /// <typeparam name="T">The type of object to retrieve, which is also the registration key.</typeparam>
        /// <param name="buildEngine">The MSBuild engine.</param>
        /// <param name="buildingInsideVisualStudio">Whether the build is being run from Visual Studio.</param>
        /// <returns>The registered object.</returns>
        /// <exception cref="InvalidOperationException">The expected object is not registered with the build engine.</exception>
        /// <exception cref="ArgumentException">The registered object under the full type name of <typeparamref name="T"/> is of the wrong type.</exception>
        public static T GetRegisteredTaskObject<T>(this IBuildEngine4 buildEngine, bool buildingInsideVisualStudio)
        {
            var lifetime = buildingInsideVisualStudio ?
                RegisteredTaskObjectLifetime.AppDomain :
                RegisteredTaskObjectLifetime.Build;
            var key = typeof(T).FullName;
            var registered = buildEngine.GetRegisteredTaskObject(key, lifetime);

            if (registered == null)
                throw new InvalidOperationException(string.Format(Resources.TaskObjectNotRegistered, key));

            if (!(registered is T))
                throw new InvalidOperationException(string.Format(Resources.TaskObjectWrongType, key, registered.GetType().FullName));

            return (T)registered;
        }

        public static MessageImportance ForVerbosity(this MessageImportance importance, LoggerVerbosity? verbosity)
        {
            if (verbosity == null)
                return importance;

            if (verbosity == LoggerVerbosity.Quiet)
                return MessageImportance.Low;

            if (verbosity >= LoggerVerbosity.Detailed)
                return MessageImportance.High;

            return importance;
        }
    }
}
