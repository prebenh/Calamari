#if KUBERNETES
using System;
using Alphaleonis.Win32.Filesystem;
using Calamari.Integration.FileSystem;
using Calamari.Integration.Processes;
using Calamari.Integration.Scripting;
using Calamari.Kubernetes.Integration;
using Calamari.Tests.Helpers;
using NUnit.Framework;

namespace Calamari.Tests.Kubernetes
{
    [TestFixture]
    [Ignore("Need creds in Envs")]
    public class KubernetesScriptEngineFixture
    {
        [Test]
        [Category(TestEnvironment.CompatibleOS.Windows)]
        public void PowershellKubeCtlScripts()
        {
            TestScript(new KubernetesPowershellScriptEngine(), "Test-Script.ps1");
        }
        
        [Test]
        
        [Category(TestEnvironment.CompatibleOS.Nix)]
        public void BashKubeCtlScripts()
        {
            TestScript(new KubernetesBashScriptEngine(), "Test-Script.sh");
        }

        private void TestScript(IScriptEngine scriptEngine, string scriptName)
        {
            using (var dir = TemporaryDirectory.Create())
            using (var temp = new TemporaryFile(Path.Combine(dir.DirectoryPath, scriptName)))
            {
                File.WriteAllText(temp.FilePath, "kubectl get nodes");
                var output = ExecuteScript(scriptEngine, temp.FilePath, new CalamariVariableDictionary()
                {
                    ["OctopusKubernetesServer"] = "ASKROB",
                    ["OctopusKubernetesUsername"] = "ASKROB",
                    ["OctopusKubernetesPassword"] = "ASKROB",
                    ["OctopusKubernetesInsecure"] = "true"
                    
                });
                output.AssertSuccess();
                output.AssertOutput("ASKROB");
            }
        }

        private CalamariResult ExecuteScript(IScriptEngine psse, string scriptName, CalamariVariableDictionary variables)
        {
            var capture = new CaptureCommandOutput();
            var runner = new CommandLineRunner(capture);
            var result = psse.Execute(new Script(scriptName), variables, runner);
            return new CalamariResult(result.ExitCode, capture);
        }
    }
}
#endif