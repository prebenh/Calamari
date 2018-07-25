﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Calamari.Commands.Support;
using Calamari.Deployment;
using Calamari.Deployment.Conventions;
using Calamari.Integration.FileSystem;
using Calamari.Integration.Processes;
using Calamari.Integration.Scripting;
using Newtonsoft.Json;
using Octostache;

namespace Calamari.Kubernetes.Conventions
{
    public class HelmUpgradeConvention: IInstallConvention
    {
        private readonly IScriptEngine scriptEngine;
        private readonly ICommandLineRunner commandLineRunner;
        private readonly ICalamariFileSystem fileSystem;

        public HelmUpgradeConvention(IScriptEngine scriptEngine, ICommandLineRunner commandLineRunner, ICalamariFileSystem fileSystem)
        {
            this.scriptEngine = scriptEngine;
            this.commandLineRunner = commandLineRunner;
            this.fileSystem = fileSystem;
        }
        
        public void Install(RunningDeployment deployment)
        {
            var releaseName = deployment.Variables.Get(SpecialVariables.Helm.ReleaseName)?.ToLower();
            if (string.IsNullOrWhiteSpace(releaseName))
            {
                throw new CommandException("ReleaseName has not been set");
            }
            
            var packagePath = GetChartLocation(deployment);

            var sb = new StringBuilder($"helm upgrade --reset-values"); //Force reset to use values now in release
           
            if (deployment.Variables.GetFlag(SpecialVariables.Helm.Install, true))
            {
                sb.Append(" --install");
            }

            if (TryGenerateVariablesFile(deployment, out var valuesFile))
            {
                sb.Append($" --values \"{valuesFile}\"");
            }
            
            foreach (var additionalValuesFile in AdditionalValuesFiles(deployment))
            {
                sb.Append($" --values \"{additionalValuesFile}\"");
            }
         
            sb.Append($" \"{releaseName}\" \"{packagePath}\"");
            
            Log.Verbose(sb.ToString());
            
            
            var fileName = Path.Combine(deployment.CurrentDirectory, "Calamari.HelmUpgrade.ps1");
            using (new TemporaryFile(fileName))
            {
                fileSystem.OverwriteFile(fileName, sb.ToString());
                
                var result = scriptEngine.Execute(new Script(fileName), deployment.Variables, commandLineRunner);
                if (result.ExitCode != 0)
                {
                    throw new CommandException(string.Format(
                        "Helm Upgrade returned non-zero exit code: {0}. Deployment terminated.", result.ExitCode));
                }

                if (result.HasErrors &&
                    deployment.Variables.GetFlag(Deployment.SpecialVariables.Action.FailScriptOnErrorOutput, false))
                {
                    throw new CommandException(
                        $"Helm Upgrade returned zero exit code but had error output. Deployment terminated.");
                }
            }
        }


        private IEnumerable<string> AdditionalValuesFiles(RunningDeployment deployment)
        {
            var variables = deployment.Variables;
            var packageReferenceNames = variables.GetIndexes(Deployment.SpecialVariables.Packages.PackageCollection);
            foreach (var packageReferenceName in packageReferenceNames)
            {
                if (!variables.GetFlag(SpecialVariables.Helm.Packages.PerformVariableReplace(packageReferenceName)))
                {
                    continue;
                }
                
                var sanitizedPackageReferenceName = fileSystem.RemoveInvalidFileNameChars(packageReferenceName);
                var paths = variables.GetPaths(SpecialVariables.Helm.Packages.ValuesFilePath(packageReferenceName));
                
                foreach (var providedPath in paths)
                {
                    var relativePath = Path.Combine(sanitizedPackageReferenceName, providedPath);
                    var files = fileSystem.EnumerateFilesWithGlob(deployment.CurrentDirectory, relativePath).ToList();
                    if (!files.Any())
                    {
                        var packageId = variables.Get(Deployment.SpecialVariables.Packages.PackageId(packageReferenceName));
                        var version = variables.Get(Deployment.SpecialVariables.Packages.PackageVersion(packageReferenceName));
                        throw new CommandException($"Unable to find file `{providedPath}` for package {packageId} v{version}");
                    }
                    
                    foreach (var file in files)
                    {
                        yield return Path.GetFullPath(file);
                    }
                }
            }
        }

        private string GetChartLocation(RunningDeployment deployment)
        {
            var packagePath = deployment.Variables.Get(Deployment.SpecialVariables.Package.Output.InstallationDirectoryPath);
            
            var packageId = deployment.Variables.Get(Deployment.SpecialVariables.Package.PackageId);

            if (fileSystem.FileExists(Path.Combine(packagePath, "Chart.yaml")))
            {
                return Path.Combine(packagePath, "Chart.yaml");
            }

            packagePath = Path.Combine(packagePath, packageId);
            if (!fileSystem.DirectoryExists(packagePath) || !fileSystem.FileExists(Path.Combine(packagePath, "Chart.yaml")))
            {
                throw new CommandException($"Unexpected error. Chart.yaml was not found in {packagePath}");
            }

            return packagePath;
        }

        private static bool TryGenerateVariablesFile(RunningDeployment deployment, out string fileName)
        {
            fileName = null;
            var variables = deployment.Variables.Get(SpecialVariables.Helm.KeyValues, "{}");
            var values = JsonConvert.DeserializeObject<Dictionary<string, string>>(variables);
            if (values.Keys.Any())
            {
                fileName = Path.Combine(deployment.CurrentDirectory, "newValues.yaml");
                using (var outputFile = new StreamWriter(fileName, false))
                {
                    foreach (var kvp in values)
                    {
                        outputFile.WriteLine($"{kvp.Key}: \"{kvp.Value}\"");
                    }
                }
                return true;
            }
            return false;
        }
    }
}