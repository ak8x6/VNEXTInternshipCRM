using System;
using System.Linq;
using VNEXT.CRM.Plugins.Core;
using Xunit;

namespace VNEXT.CRM.Plugins.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void PluginAssemblyInfo_HasExpectedSolutionAndPrefix()
        {
            Assert.Equal("VNEXTInternshipCRM", PluginAssemblyInfo.SolutionName);
            Assert.Equal("vn", PluginAssemblyInfo.PublisherPrefix);
        }

        [Fact]
        public void PluginAssembly_IsStrongNameSignedWithVersion1000()
        {
            var assemblyName = typeof(PluginAssemblyInfo).Assembly.GetName();
            var token = assemblyName.GetPublicKeyToken();

            Assert.NotNull(token);
            Assert.Equal(8, token.Length);
            Assert.Equal(new Version(1, 0, 0, 0), assemblyName.Version);
        }

        [Fact]
        public void PluginAssembly_ReferencesOnlyPlatformAndXrmSdkAssemblies()
        {
            var referenced = typeof(PluginAssemblyInfo).Assembly.GetReferencedAssemblies();

            foreach (var asm in referenced)
            {
                var name = asm.Name;
                var isAllowed = string.Equals(name, "mscorlib", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Microsoft.Xrm.Sdk", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Microsoft.Crm.Sdk.Proxy", StringComparison.OrdinalIgnoreCase);

                Assert.True(isAllowed, "Unexpected runtime assembly reference: " + name);
            }
        }
    }
}
