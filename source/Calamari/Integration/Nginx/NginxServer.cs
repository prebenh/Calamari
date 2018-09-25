﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Calamari.Integration.FileSystem;
using Newtonsoft.Json.Linq;

namespace Calamari.Integration.Nginx
{
    public abstract class NginxServer
    {
        private readonly ICalamariFileSystem fileSystem = CalamariPhysicalFileSystem.GetPhysicalFileSystem();
        
        private readonly List<KeyValuePair<string, string>> serverBindingDirectives =
            new List<KeyValuePair<string, string>>();

        private bool useHostName;
        private string hostName;
        private readonly IDictionary<string, string> additionalLocations = new Dictionary<string, string>();
        private readonly IDictionary<string, string> sslCerts = new Dictionary<string, string>();
        private string virtualServerName;
        private string virtualServerConfigRoot;
        private dynamic rootLocation;

        private string virtualServerConfig;

        public static NginxServer AutoDetect()
        {
            if (CalamariEnvironment.IsRunningOnNix || CalamariEnvironment.IsRunningOnMac)
                return new NixNginxServer();

            return new WindowsNginxServer();
        }

        public NginxServer WithVirtualServerName(string name)
        {
            virtualServerName = name;
            virtualServerConfigRoot = $"{GetConfigRootDirectory()}/{name}.conf.d";
            return this;
        }

        public NginxServer WithServerBindings(IEnumerable<Binding> bindings, IDictionary<string, (string SubjectCommonName, string CertificatePem, string PrivateKeyPem)> certificates)
        {
            foreach (var binding in bindings)
            {
                if (string.Equals("http", binding.Protocol, StringComparison.InvariantCultureIgnoreCase))
                {
                    AddServerBindingDirective(NginxDirectives.Server.Listen,
                        GetListenValue(binding.IpAddress, binding.Port));
                }
                else
                {
                    string certificatePath = null;
                    string certificateKeyPath = null;
                    if (string.IsNullOrEmpty(binding.CertificateLocation))
                    {
                        if (!certificates.TryGetValue(binding.CertificateVariable, out var certificate))
                        {
                            Log.Warn($"Couldn't find certificate {binding.CertificateVariable}");
                            continue;
                        }

                        var certificateRootPath = Path.Combine(GetSslCertRootDirectory(),
                            fileSystem.RemoveInvalidFileNameChars(certificate.SubjectCommonName));

                        certificatePath = Path.Combine(certificateRootPath,
                            $"{fileSystem.RemoveInvalidFileNameChars(certificate.SubjectCommonName)}.crt");

                        certificateKeyPath = Path.Combine(certificateRootPath,
                            $"{fileSystem.RemoveInvalidFileNameChars(certificate.SubjectCommonName)}.key");

                        sslCerts.Add(certificatePath, certificate.CertificatePem);

                        sslCerts.Add(certificateKeyPath, certificate.PrivateKeyPem);
                    }

                    AddServerBindingDirective(NginxDirectives.Server.Listen,
                        GetListenValue(binding.IpAddress, binding.Port, true));

                    AddServerBindingDirective(NginxDirectives.Server.Certificate,
                        certificatePath ?? binding.CertificateLocation);

                    AddServerBindingDirective(NginxDirectives.Server.CertificateKey,
                        certificateKeyPath ?? binding.CertificateKeyLocation);

                    var securityProtocols = binding.SecurityProtocols;
                    if (securityProtocols != null && securityProtocols.Any())
                    {
                        AddServerBindingDirective(NginxDirectives.Server.SecurityProtocols, string.Join(" ", binding.SecurityProtocols));
                    }

//                    if (!string.IsNullOrWhiteSpace((string) binding.ciphers))
//                    {
//                        AddServerBindingDirective(NginxDirectives.Server.SslCiphers, string.Join(":", binding.ciphers));
//                    }
                }
            }

            return this;
        }

        public NginxServer WithHostName(string serverHostName)
        {
            if (string.IsNullOrWhiteSpace(serverHostName) || serverHostName.Equals("*")) return this;

            useHostName = true;
            hostName = serverHostName;

            return this;
        }

        public NginxServer WithAdditionalLocations(IEnumerable<Location> locations)
        {
            if (!locations.Any()) return this;

            foreach (var location in locations)
            {
                var locationConfig = GetLocationConfig(location);
                var locationConfFile = Path.Combine(virtualServerConfigRoot,
                    $"location.{(location.Path).Trim('/')}.conf");

                additionalLocations.Add(locationConfFile, locationConfig);
            }

            return this;
        }

        public NginxServer WithRootLocation(Location location)
        {
            rootLocation = location;

            return this;
        }

        public void BuildConfiguration()
        {
            virtualServerConfig =
                $@"
server {{
{string.Join(Environment.NewLine, serverBindingDirectives.Select(binding => $"    {binding.Key} {binding.Value};"))}
{(useHostName ? $"    {NginxDirectives.Server.HostName} {hostName};" : "")}
{(additionalLocations.Any() ? $"    {NginxDirectives.Include} {virtualServerConfigRoot}/location.*.conf;" : "")}
{GetLocationConfig(rootLocation)}
}}
";
        }

        public void SaveConfiguration()
        {
            foreach (var sslCert in sslCerts)
            {
                fileSystem.EnsureDirectoryExists(Path.GetDirectoryName(sslCert.Key));
                fileSystem.OverwriteFile(sslCert.Key, sslCert.Value);
            }

            foreach (var additionalLocation in additionalLocations)
            {
                fileSystem.EnsureDirectoryExists(Path.GetDirectoryName(additionalLocation.Key));
                fileSystem.OverwriteFile(additionalLocation.Key, additionalLocation.Value);
            }

            var virtualServerConfFile = Path.Combine(GetConfigRootDirectory(), $"{virtualServerName}.conf");
            fileSystem.OverwriteFile(virtualServerConfFile, virtualServerConfig);
        }

        protected abstract string GetConfigRootDirectory();
        protected abstract string GetSslCertRootDirectory();

        private string GetListenValue(string ipAddress, string port, bool isHttps = false)
        {
            var sslParameter = isHttps ? " ssl" : "";
            var ipAddressParameter =
                !string.IsNullOrWhiteSpace(ipAddress) && !ipAddress.Equals("*") ? $"{ipAddress}:" : "";

            return $"{ipAddressParameter}{port}{sslParameter}";
        }

        private string GetLocationConfig(Location location)
        {
            return
                $@"
    location {location.Path} {{
{GetLocationDirectives(location.Directives)}
{GetLocationHeaders(location.Headers)}
    }}
";
        }

        private static string GetLocationDirectives(string directivesString)
        {
            if (string.IsNullOrEmpty(directivesString)) return string.Empty;
            
            IEnumerable<dynamic> directives = JObject.Parse(directivesString);
            return !directives.Any()
                ? string.Empty
                : string.Join(Environment.NewLine, directives.Select(d => $"        {d.Name} {d.Value};"));
        }

        private static string GetLocationHeaders(string headersString)
        {
            if (string.IsNullOrEmpty(headersString)) return string.Empty;
            
            IEnumerable<dynamic> headers = JObject.Parse(headersString);
            return !headers.Any()
                ? string.Empty
                : string.Join(Environment.NewLine,
                    headers.Select(h => $"        {NginxDirectives.Location.Proxy.SetHeader} {h.Name} {h.Value};"));
        }

        private void AddServerBindingDirective(string key, string value)
        {
            serverBindingDirectives.Add(new KeyValuePair<string, string>(key, value));
        }
    }

    public class Binding
    {
        public string Protocol { get; set; }
        public string Port { get; set; }
        public string IpAddress { get; set; }
        public string CertificateLocation { get; set; }
        public string CertificateKeyLocation { get; set; }
        public string CertificateVariable { get; set; }
        public IEnumerable<string> SecurityProtocols { get; set; }
        public bool Enabled { get; set; }
    }

    public class Location
    {
        public string Path { get; set; }
        public string Directives { get; set; }
        public string Headers { get; set; }
    }
}