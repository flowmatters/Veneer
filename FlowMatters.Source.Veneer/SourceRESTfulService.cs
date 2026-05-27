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
using FlowMatters.Source.Veneer.RemoteScripting;
using FlowMatters.Source.WebServer;
using RiverSystem.ApplicationLayer.Interfaces;
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
        private RiverSystemScenario _scenario;
        private List<int> _registeredOnPorts = new List<int>();
        private bool _allowScript;

        public bool AllowRemoteConnections { get; set; }

        public bool AllowSsl { get; set; }

        public bool RunningInGUI { get; set; } = true;

        public IProjectHandler<RiverSystemProject> ProjectHandler { get; set; }

        public CustomEndPoint[] CustomEndpoints { get; set; }

        public override bool AllowScript
        {
            get => _allowScript;
            set
            {
                _allowScript = value;
                InitializeStaticServiceState();
            }
        }

        public SourceRESTfulService(int port) : base(port)
        {

        }

        public override async Task Start()
        {
            // TODO: RM-20834 RM-21455 This doesn't look to be necessary anymore with CoreWCF, but leaving commented out in case it needs to be revisited
            //LeaveDotsAndSlashesEscaped();

            // Initialize static state before starting the service
            InitializeStaticServiceState();

            var builder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    // Add base service model services
                    services.AddServiceModelServices();

                    // Add web services
                    services.AddServiceModelWebServices();

                    // Register as transient for PerCall behavior
                    services.AddTransient<SourceService>();

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
                        var binding = new WebHttpBinding
                        {
                            MaxReceivedMessageSize = 1024 * 1024 * 1024 // 1 gigabyte
                        };

                        var protocol = "http";
                        if (AllowSsl)
                        {
                            protocol = "https";
                            binding.Security = new WebHttpSecurity { Mode = WebHttpSecurityMode.Transport };
                        }

                        var host = AllowRemoteConnections ? "0.0.0.0" : "localhost";

                        builder.AddService<SourceService>();
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
                                    endpoint.EndpointBehaviors.Add(cors);
                                }
                            }
                        });
                    });
                });

            try
            {
                Running = false;
                _host = builder.Build();

                // StartAsync performs the Kestrel bind. AddressInUse / permission errors surface here,
                // so the readiness logs below only run after the listen socket is actually bound.
                await _host.StartAsync();

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

                await _host.WaitForShutdownAsync();
            }
            catch (Exception ex) when (IsAddressInUse(ex))
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
                Log("Enabling SSL requires a server certificate.", LogLevel.Warning);
                Log("To generate a developer certificate, run 'dotnet dev-certs https' in a Powershell/Command Prompt window.", LogLevel.Warning);
                Log("To trust the certificate (Windows and macOS only) run 'dotnet dev-certs https --trust'.", LogLevel.Warning);
                Log("Falling back to HTTP only.", LogLevel.Warning);
                AllowSsl = false;
                await Start();
            }
            catch (Exception e)
            {
                Log("COULD NOT START VENEER", LogLevel.Error);
                Log(e.Message, LogLevel.Error);
                Log(e.StackTrace, LogLevel.Error);
                if (e.InnerException != null)
                {
                    Log("INNER EXCEPTION:", LogLevel.Error);
                    Log(e.InnerException.Message, LogLevel.Error);
                    Log(e.InnerException.StackTrace, LogLevel.Error);
                }
            }
        }

        private static bool IsAddressInUse(Exception ex)
        {
            // Walk the exception chain looking for socket address-in-use indicators
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is System.Net.Sockets.SocketException)
                    return true;
                if (current.Message.Contains("Only one usage of each socket address"))
                    return true;
            }
            return false;
        }

        private void InitializeStaticServiceState()
        {
            SourceService.InitializeSharedState(
                scenario: _scenario,
                projectHandler: ProjectHandler,
                allowScript: _allowScript,
                runningInGUI: RunningInGUI,
                customEndpoints: CustomEndpoints
            );

            SourceService.SetLogHandler((sender, message, level) => Log(message, level));
        }

        private void LogVeneerPermissionsIssue()
        {
            Log("For details, see: https://github.com/flowmatters/veneer", LogLevel.Error);

            if (AllowRemoteConnections)
            {
                Log("If you require external connections, you must select a port where Veneer has permissions to accept external connections.", LogLevel.Error);
                Log($"Veneer does not have permission to accept external (ie non-local) connections on port {_port}", LogLevel.Error);
            }
            else
            {
                Log("Alternatively, enable 'Allow Remote Connections' and restart Veneer.", LogLevel.Error);
                Log("To establish a local-only connection, select a port where Veneer is NOT registered for external connections.", LogLevel.Error);
                Log($"This is most likely because Veneer is registered to accept external/non-local connections on port {_port}.", LogLevel.Error);
                Log($"Veneer does not have permission to accept local-only connections on port {_port}", LogLevel.Error);
            }
            Log($"COULD NOT START VENEER ON PORT {_port}", LogLevel.Error);
        }

        private void RetrieveVeneerStatus()
        {
            try
            {
                var response = _httpClient.GetStringAsync(STATUS_URL).GetAwaiter().GetResult();
                dynamic status = JsonConvert.DeserializeObject(response);
                JArray messages = status.message;
                messages.Reverse().Select(e=>e.ToString()).ForEachItem(m => Log(m));
            }
            catch (Exception ex)
            {
                Log($"Failed to retrieve Veneer status: {ex.Message}", LogLevel.Warning);
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

        public override async Task Stop()
        {
            if (_host != null)
            {
                Log("Stopping Service");
                await _host.StopAsync();
                _host = null;
            }
            Running = false;
        }

        public override RiverSystemScenario Scenario
        {
            get { return _scenario; }
            set
            {
                _scenario = value;
                if (Running)
                {
                    SourceService.UpdateSharedScenario(_scenario);
                }
            }
        }
    }
}
