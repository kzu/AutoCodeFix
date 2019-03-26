using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Host;

namespace AutoCodeFix
{
    public static class Internals
    {
        public static IEnumerable<Lazy<TExtension, TMetadata>> GetExports<TExtension, TMetadata>(this HostServices services)
        {
            var getExports = services.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name.EndsWith("GetExports") && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2)
                ?.MakeGenericMethod(typeof(TExtension), typeof(TMetadata))
                ?? throw new NotSupportedException("Failed to retrieve exports from host services. Plase report the issue.");

            var exports = getExports.Invoke(services, null);
            
            return (IEnumerable<Lazy<TExtension, TMetadata>>)exports;
        }
    }
}