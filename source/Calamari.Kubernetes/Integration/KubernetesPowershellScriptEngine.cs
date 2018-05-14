using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using Calamari.Integration.EmbeddedResources;
using Calamari.Integration.FileSystem;
using Calamari.Integration.Processes;
using Calamari.Integration.Scripting;
using Calamari.Integration.Scripting.Bash;
using Calamari.Integration.Scripting.WindowsPowerShell;

namespace Calamari.Kubernetes.Integration
{

    public class KubernetesBashScriptEngine : IScriptEngine
    {
        public ScriptType[] GetSupportedTypes()
        {
            return new[] {ScriptType.Bash};
        }

        public CommandResult Execute(Script script, CalamariVariableDictionary variables,
            ICommandLineRunner commandLineRunner, StringDictionary environmentVars = null)
        {
            var scriptEngine = new BashScriptEngine();
            throw new NotImplementedException();
//            
//            return new AwsEnvironmentGeneration(variables)
//                .Map(envDetails => new BashScriptEngine().Execute(
//                    script,
//                    variables,
//                    commandLineRunner))
        }
    }


    public class KubernetesPowershellScriptEngine : IScriptEngine
    {
        readonly ICalamariEmbeddedResources embeddedResources;
        private readonly WindowsPhysicalFileSystem fileSystem;

        public KubernetesPowershellScriptEngine()
        {
            this.fileSystem = new WindowsPhysicalFileSystem();
            //this.certificateStore = new CalamariCertificateStore();
            this.embeddedResources = new AssemblyEmbeddedResources();
        }

        public ScriptType[] GetSupportedTypes()
        {
            return new[] {ScriptType.Powershell};
        }

        public CommandResult Execute(Script script, CalamariVariableDictionary variables,
            ICommandLineRunner commandLineRunner, StringDictionary environmentVars = null)
        {
            variables.Set("OctopusKubernetesTargetScript", $"\"{script.File}\"");
            variables.Set("OctopusKubernetesTargetScriptParameters", script.Parameters);
            
            var scriptEngine = new PowerShellScriptEngine();
            var workingDirectory = Path.GetDirectoryName(script.File);
            
            
            variables.Set("OctopusKubernetesKubeCtlConfig", Path.Combine(workingDirectory, "kubectl-octo.yml"));

            using (new TemporaryFile(Path.Combine(workingDirectory, "AzureProfile.json")))
            using (var contextScriptFile = new TemporaryFile(CreateContextScriptFile(workingDirectory)))
            {
                //otherwise use management certificate
                //SetOutputVariable("OctopusUseServicePrincipal", false.ToString(), variables);
                //using (new TemporaryFile(CreateAzureCertificate(workingDirectory, variables)))
                //{
                return scriptEngine.Execute(new Script(contextScriptFile.FilePath), variables, commandLineRunner);
                //}
            }


//            return new AwsEnvironmentGeneration(variables)
//                .Map(envDetails => new PowerShellScriptEngine().Execute(
//                    script,
//                    variables,
//                    commandLineRunner))
        }

        string CreateContextScriptFile(string workingDirectory)
        {
            var azureContextScriptFile = Path.Combine(workingDirectory, "Octopus.KubectlPowershellContext.ps1");
            var contextScript = embeddedResources.GetEmbeddedResourceText(Assembly.GetExecutingAssembly(), "Calamari.Kubernetes.Scripts.KubectlPowershellContext.ps1");
            fileSystem.OverwriteFile(azureContextScriptFile, contextScript);
            return azureContextScriptFile;
        }
    }
}