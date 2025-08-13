using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CoreWCF;
using CoreWCF.Configuration;
using CoreWCF.Description;
using FlowMatters.Source.Veneer.CORS;
using FlowMatters.Source.Veneer.Formatting;
using FlowMatters.Source.WebServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RiverSystem;
using RiverSystem.ManagedExtensions;

namespace FlowMatters.Source.Veneer
{
    public class SourceRESTfulService : AbstractSourceServer
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        public const int DEFAULT_PORT = 9876;
        public const string STATUS_URL = "https://www.flowmatters.com.au/veneer/status.json";
        private IWebHost _host;
        private SourceService _singletonInstance;
        private RiverSystemScenario _scenario;
        private List<int> _registeredOnPorts = new List<int>();
        private bool _isEndpointRegistered = false;
         
        public bool AllowRemoteConnections { get; set; }

        public bool AllowSsl { get; set; }

        public override SourceService Service
        {
            get { return _singletonInstance; }
        }

        public SourceRESTfulService(int port) : base(port)
        {
            
        }

        public override async Task Start()
        {
            // TODO: RM-20834 RM-21455 This doesn't look to be necessary anymore with CoreWCF, but leaving commented out in case it needs to be revisited
            //LeaveDotsAndSlashesEscaped();
            
            _singletonInstance = new SourceService();
            _singletonInstance.LogGenerator += _singletonInstance_LogGenerator;
            _singletonInstance.Scenario = Scenario;

            var builder = Microsoft.AspNetCore.WebHost.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    // Add base service model services
                    services.AddServiceModelServices();
                    
                    // Add web services
                    services.AddServiceModelWebServices();

                    // Register the service instance as singleton
                    services.AddSingleton(_singletonInstance);
                    
                    // Add any custom behaviors as singletons
                    services.AddSingleton<IServiceBehavior, UseRequestHeadersForMetadataAddressBehavior>();
                })
                .UseKestrel(options =>
                {
                    if (AllowSsl)
                    {
                        if (AllowRemoteConnections){
                            options.ListenAnyIP(_port, listenOptions =>
                            {
                                listenOptions.UseHttps();
                            });
                        }
                        else
                        {
                            options.ListenLocalhost(_port, listenOptions =>
                            {
                                listenOptions.UseHttps();
                            });
                        }
                    }
                    else
                    {
                        if (AllowRemoteConnections)
                            options.ListenAnyIP(_port);
                        else
                            options.ListenLocalhost(_port);
                    }
                    
                })
                .Configure(app =>
                {
                    app.UseServiceModel(builder =>
                    {
                        if (!_isEndpointRegistered)
                        {
                            // Create the web binding
                            var binding = new WebHttpBinding
                            {
                                MaxReceivedMessageSize = 1024 * 1024 * 1024 // 1 gigabyte
                            };

                            // Set https if applicable
                            var protocol = "http";
                            if (AllowSsl)
                            {
                                protocol = "https";
                                binding.Security = new WebHttpSecurity { Mode = WebHttpSecurityMode.Transport };
                            }

                            // Different urls for local and remote connections
                            var host = AllowRemoteConnections ? "0.0.0.0" : "localhost";

                            // Register the service type
                            builder.AddService<SourceService>();

                            // In CoreWCF, we handle host restrictions through the endpoint address
                            builder.AddServiceWebEndpoint<SourceService, ISourceService>(binding, $"{protocol}://{host}:{_port}");

                            builder.ConfigureServiceHostBase<SourceService>(serviceHost =>
                            {
                                var reply = new ReplyFormatSwitchBehaviour();
                                var cors = new EnableCrossOriginResourceSharingBehavior();

                                foreach (var endpoint in serviceHost.Description.Endpoints)
                                {
                                    if (endpoint.Binding is WebHttpBinding b)
                                    {
                                        endpoint.EndpointBehaviors.Add(reply);

                                        // Only add CORS if not ssl
                                        if (b.Security.Mode != WebHttpSecurityMode.Transport && AllowSsl)
                                            endpoint.EndpointBehaviors.Add(cors);
                                    }
                                }
                            });
                            
                            _isEndpointRegistered = true;
                        }
                    });
                });

            try
            {
                Running = false;
                _host = builder.Build();
                var task = _host.RunAsync();

                Log("Veneer, by Flow Matters: https://www.flowmatters.com.au");
                try
                {
                    RetrieveVeneerStatus();
                }
                catch
                {
                    // Pass
                }

                Log(AllowSsl
                        ? $"Started Source RESTful Service on https port:{_port}"
                        : $"Started Source RESTful Service on http port:{_port}");

                Running = true;

                await task;
            }
            catch (AddressAlreadyInUseException)
            {
                _port++; // Keep retrying until we run out of allocated ports
                await Start();
            }
            catch (IOException ex) when (ex.HResult == -2147024891) // ERROR_ACCESS_DENIED
            {
                LogVeneerPermissionsIssue();
            }
            catch (HttpRequestException hre) when (hre.Message.Contains("access denied") ||
                                                   hre.Message.Contains("permission denied"))
            {
                LogVeneerPermissionsIssue();
            }
            catch (InvalidOperationException ioe) when (AllowSsl &&
                                                        ioe.Message.Contains("No server certificate was specified") &&
                                                        ioe.Message.Contains("Unable to configure HTTPS endpoint"))
            {
                Log("Enabling SSL requires a server certificate.");
                Log("To generate a developer certificate, run 'dotnet dev-certs https' in a Powershell/Command Prompt window.");
                Log("To trust the certificate (Windows and macOS only) run 'dotnet dev-certs https --trust'.");
                Log("Falling back to HTTP only.");
                AllowSsl = false;
                await Start();
            }
            catch (Exception e)
            {
                Log("COULD NOT START VENEER");
                Log(e.Message);
                Log(e.StackTrace);
            }
        }

        private void LogVeneerPermissionsIssue()
        {
            Log("For details, see: https://github.com/flowmatters/veneer");

            if (AllowRemoteConnections)
            {
                Log("If you require external connections, you must select a port where Veneer has permissions to accept external connections.");
                Log($"Veneer does not have permission to accept external (ie non-local) connections on port {_port}");
            }
            else
            {
                Log("Alternatively, enable 'Allow Remote Connections' and restart Veneer.");
                Log("To establish a local-only connection, select a port where Veneer is NOT registered for external connections.");
                Log($"This is most likely because Veneer is registered to accept external/non-local connections on port {_port}.");
                Log($"Veneer does not have permission to accept local-only connections on port {_port}");
            }
            Log($"COULD NOT START VENEER ON PORT {_port}");
        }

        private void RetrieveVeneerStatus()
        {
            try
            {
                var response = _httpClient.GetStringAsync(STATUS_URL).GetAwaiter().GetResult();
                dynamic status = JsonConvert.DeserializeObject(response);
                JArray messages = status.message;
                messages.Reverse().Select(e=>e.ToString()).ForEachItem(Log);
            }
            catch (Exception ex)
            {
                Log($"Failed to retrieve Veneer status: {ex.Message}");
            }
        }

        // TODO: RM-20834 RM-21455 This doesn't look to be necessary anymore with CoreWCF, but leaving commented out in case it needs to be revisited
        //private void LeaveDotsAndSlashesEscaped()
        //{
        //    var getSyntaxMethod =
        //        typeof(UriParser).GetMethod("GetSyntax", BindingFlags.Static | BindingFlags.NonPublic);
        //    if (getSyntaxMethod == null)
        //    {
        //        throw new MissingMethodException("UriParser", "GetSyntax");
        //    }

        //    var uriParser = getSyntaxMethod.Invoke(null, new object[] { "http" });

        //    var setUpdatableFlagsMethod =
        //        uriParser.GetType().GetMethod("SetUpdatableFlags", BindingFlags.Instance | BindingFlags.NonPublic);
        //    if (setUpdatableFlagsMethod == null)
        //    {
        //        throw new MissingMethodException("UriParser", "SetUpdatableFlags");
        //    }

        //    setUpdatableFlagsMethod.Invoke(uriParser, new object[] { 0 });
        //}

        void _singletonInstance_LogGenerator(object sender, string msg)
        {
            Log(msg);
        }

        public override async Task Stop()
        {
            if (_host != null)
            {
                Log("Stopping Service");
                await _host.StopAsync();
                _host = null;
                _isEndpointRegistered = false;
            }
            Running = false;
        }

        public override RiverSystemScenario Scenario
        {
            get { return _scenario; }
            set
            {
                _scenario = value;
                if (_singletonInstance != null)
                    _singletonInstance.Scenario = _scenario;
            }
        }
    }
}
