using System;
using Calamari.Commands.Support;
using Calamari.Integration.Scripting;
using Calamari.Kubernetes.Integration;
using Calamari.Util.Environments;

namespace Calamari.Kubernetes
{
    class Program : Calamari.Program
    {
        public Program() : base("Calamari.Azure", typeof(Kubernetes.Program).Assembly.GetInformationalVersion(), EnvironmentHelper.SafelyGetEnvironmentInformation())
        {
            ScriptEngineRegistry.Instance.ScriptEngines[ScriptType.Powershell] = new KubernetesPowershellScriptEngine();
            ScriptEngineRegistry.Instance.ScriptEngines[ScriptType.Bash] = new KubernetesBashScriptEngine();
        }

        static int Main(string[] args)
        {
            var program = new Kubernetes.Program();
            return program.Execute(args);
        }

        protected override void RegisterCommandAssemblies()
        {
            CommandLocator.Instance.RegisterAssemblies(typeof(Calamari.Program).Assembly, typeof(Program).Assembly);
        }
    }
}
